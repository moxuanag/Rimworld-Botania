using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚·世界之魂（Boss）本体逻辑。
    ///
    /// 为什么用 Pawn 子类而不是 ThingComp：
    ///  · 小人的伤害管线走 Pawn.PreApplyDamage，ThingComp 的前置伤害钩子在 Pawn 上不保证被调用
    ///  · 小人的属性面板文案由 Pawn.GetInspectString 自己拼，不包含 CompInspectStringExtra
    /// 两件事都只能在 Pawn 子类里可靠接管。
    ///
    /// 战斗循环（docs/盖亚Boss设计文档.md）：
    ///   带盾出场 → 破盾 → 瘫痪 20 秒（受伤 ×1.5）→ 瘫痪结束护盾瞬满
    ///   → 瘫痪窗口内打到 66% / 33% → 锁血 5 秒（无敌 + 眩晕）→ 形态升级、护盾瞬满
    ///   → 没打到阈值 → 只有护盾回满，阶段不变
    /// 护盾数值同时同步到一个 Hediff（健康面板可见）。
    /// </summary>
    [StaticConstructorOnStartup]
    public class Pawn_GaiaBoss : Pawn
    {
        private const string ShieldHediffDefName = "Botania_GaiaShield";

        // 原版护盾腰带用的那张气泡贴图
        private static readonly Material BubbleMat =
            MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent);

        private static readonly float[] PhaseShields = { 300f, 400f, 500f };
        private static readonly float[] PhaseThresholds = { 0.66f, 0.33f };

        private float shield = 300f;
        private int phaseIndex;
        private int invulnerableUntil = -1;
        private int paralyzedUntil = -1;
        private bool dropsGiven;
        private bool initialized;

        private int Now => Find.TickManager.TicksGame;

        public float Shield => shield;

        public float ShieldMax
        {
            get
            {
                int idx = phaseIndex < 0 ? 0 : (phaseIndex >= PhaseShields.Length ? PhaseShields.Length - 1 : phaseIndex);
                return PhaseShields[idx];
            }
        }

        public int PhaseNumber => phaseIndex + 1;

        public bool IsParalyzed => paralyzedUntil > Now;

        public bool IsInvulnerable => invulnerableUntil > Now;

        // ---------------- 生命周期 ----------------

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!initialized)
            {
                initialized = true;
                shield = ShieldMax;
                SyncShieldHediff();
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (Dead)
            {
                return;
            }

            // 瘫痪结束：护盾瞬满
            if (paralyzedUntil > 0 && Now >= paralyzedUntil)
            {
                paralyzedUntil = -1;
                shield = ShieldMax;
                SyncShieldHediff();
                Messages.Message("Botania.GaiaBossShieldBack".Translate(),
                    new LookTargets(this), MessageTypeDefOf.ThreatBig, false);
            }

            if (invulnerableUntil > 0 && Now >= invulnerableUntil)
            {
                invulnerableUntil = -1;
            }

            // 每 30 tick 同步一次护盾 Hediff：防止出生瞬间 health 尚未就绪导致没挂上
            if (Now % 30 == 0)
            {
                SyncShieldHediff();
            }

            TryAdvancePhase();
        }

        // ---------------- 伤害 ----------------

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            if (absorbed || Dead)
            {
                return;
            }

            // 锁血：完全免疫
            if (IsInvulnerable)
            {
                absorbed = true;
                return;
            }

            float amount = dinfo.Amount;

            // 瘫痪：受伤 ×1.5
            if (IsParalyzed)
            {
                amount *= 1.5f;
            }

            // 有盾：80% 减伤，并从护盾里扣
            if (shield > 0f)
            {
                float reduced = amount * 0.2f;
                shield -= reduced;
                if (shield <= 0f)
                {
                    shield = 0f;
                    StartParalysis();
                }
                amount = reduced;
            }

            dinfo.SetAmount(amount);
            SyncShieldHediff();
        }

        private void StartParalysis()
        {
            paralyzedUntil = Now + 1200;
            if (stances != null && stances.stunner != null)
            {
                stances.stunner.StunFor(1200, this, false, true, true);
            }
            SyncShieldHediff();
            Messages.Message(
                "Botania.GaiaBossShieldBroken.Text".Translate(),
                new LookTargets(this),
                MessageTypeDefOf.NeutralEvent);
        }

        // ---------------- 阶段 ----------------

        private void TryAdvancePhase()
        {
            if (IsInvulnerable)
            {
                return;
            }
            if (phaseIndex >= PhaseThresholds.Length)
            {
                return;
            }
            float pct = health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (pct > PhaseThresholds[phaseIndex])
            {
                return;
            }

            phaseIndex++;
            invulnerableUntil = Now + 300;
            paralyzedUntil = -1;
            shield = ShieldMax;
            SyncShieldHediff();

            if (stances != null && stances.stunner != null)
            {
                stances.stunner.StunFor(300, this, false, true, true);
            }

            Messages.Message(
                "Botania.GaiaBossPhase.Text".Translate(PhaseNumber),
                new LookTargets(this),
                MessageTypeDefOf.NeutralEvent);
        }

        // ---------------- 护盾 Hediff（健康面板显示） ----------------

        private void SyncShieldHediff()
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(ShieldHediffDefName);
            if (def == null || health == null)
            {
                return;
            }
            Hediff existing = null;
            for (int i = 0; i < health.hediffSet.hediffs.Count; i++)
            {
                if (health.hediffSet.hediffs[i].def == def)
                {
                    existing = health.hediffSet.hediffs[i];
                    break;
                }
            }

            if (shield <= 0f)
            {
                if (existing != null)
                {
                    health.RemoveHediff(existing);
                }
                return;
            }

            float severity = shield / Mathf_Max(1f, ShieldMax);
            if (existing == null)
            {
                Hediff hediff = HediffMaker.MakeHediff(def, this);
                hediff.Severity = severity;
                health.AddHediff(hediff);
            }
            else
            {
                existing.Severity = severity;
            }
        }

        private static float Mathf_Max(float a, float b)
        {
            return a > b ? a : b;
        }

        // ---------------- 绘制 ----------------

        /// <summary>
        /// 有盾时在小人身上画一层原版护盾气泡（和护盾腰带同一张 Other/ShieldBubble）。
        /// Pawn 的绘制入口是 DrawAt，DrawAt 里 base 之后再叠一层即可。
        /// </summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            if (shield <= 0f)
            {
                return;
            }

            try
            {
                float frac = Mathf.Clamp01(shield / Mathf.Max(1f, ShieldMax));
                float size = Mathf.Lerp(3.6f, 4.6f, frac);
                Vector3 pos = drawLoc;
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(pos, Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up),
                    new Vector3(size, 1f, size));
                Graphics.DrawMesh(MeshPool.plane10, matrix, BubbleMat, 0);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Botania] 护盾气泡绘制失败：" + e, 8172631);
            }
        }

        // ---------------- 界面 ----------------

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            string extra = IsParalyzed
                ? "Botania.GaiaBossInspect.Paralyzed".Translate()
                : (shield > 0f
                    ? "Botania.GaiaBossInspect.Shield".Translate(
                        Mathf.RoundToInt(shield).ToString(), Mathf.RoundToInt(ShieldMax).ToString())
                    : "Botania.GaiaBossInspect.NoShield".Translate());
            string phase = "Botania.GaiaBossInspect.Phase".Translate(PhaseNumber);
            return baseText.NullOrEmpty() ? phase + "\n" + extra : baseText + "\n" + phase + "\n" + extra;
        }

        // ---------------- 掉落 ----------------

        /// <summary>
        /// 掉落必须挂在 Kill 上：Pawn 死亡后会被 Corpse 替换并离开地图，
        /// Tick 不再执行，写在 Tick 里的掉落永远不会触发（实测踩过）。
        /// </summary>
        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            Map map = Map;
            IntVec3 pos = Position;
            base.Kill(dinfo, exactCulprit);
            if (map != null && pos.IsValid)
            {
                GiveDropsOnce(map, pos);
            }
        }


        private void GiveDropsOnce(Map map, IntVec3 pos)
        {
            if (dropsGiven)
            {
                return;
            }
            dropsGiven = true;

            SpawnItem(map, pos, "Botania_GaiaCore", 1);
            SpawnItem(map, pos, "Botania_PixieDust", Rand.RangeInclusive(8, 15));
            SpawnItem(map, pos, "Botania_TerrasteelIngot", Rand.RangeInclusive(3, 5));

            ResearchProjectDef gaia = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Botania_Gaia");
            bool unlocked = gaia != null && gaia.IsFinished;
            if (!unlocked || Rand.Chance(0.25f))
            {
                SpawnItem(map, pos, "Botania_GaiaSpirit", 1);
            }
        }

        private static void SpawnItem(Map map, IntVec3 pos, string defName, int count)
        {
            if (count <= 0)
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Warning("[Botania] 盖亚掉落找不到物品: " + defName);
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenSpawn.Spawn(thing, pos, map, WipeMode.VanishOrMoveAside);
        }

        // ---------------- 存档 ----------------

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shield, "gaiaShield", 300f);
            Scribe_Values.Look(ref phaseIndex, "gaiaPhaseIndex", 0);
            Scribe_Values.Look(ref invulnerableUntil, "gaiaInvulnUntil", -1);
            Scribe_Values.Look(ref paralyzedUntil, "gaiaParalyzedUntil", -1);
            Scribe_Values.Look(ref dropsGiven, "gaiaDropsGiven", false);
            Scribe_Values.Look(ref initialized, "gaiaInitialized", false);
        }
    }
}
