using RimWorld;
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
    public class Pawn_GaiaBoss : Pawn
    {
        private const string ShieldHediffDefName = "Botania_GaiaShield";

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
                GiveDropsOnce();
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
            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaBossShieldBroken.Label".Translate(),
                "Botania.GaiaBossShieldBroken.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(this));
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

            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaBossPhase.Label".Translate(PhaseNumber),
                "Botania.GaiaBossPhase.Text".Translate(PhaseNumber),
                LetterDefOf.ThreatBig,
                new LookTargets(this));
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

        // ---------------- 界面 ----------------

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            string extra = IsParalyzed
                ? "Botania.GaiaBossInspect.Paralyzed".Translate()
                : (shield > 0f
                    ? "Botania.GaiaBossInspect.Shield".Translate(shield, ShieldMax)
                    : "Botania.GaiaBossInspect.NoShield".Translate());
            string phase = "Botania.GaiaBossInspect.Phase".Translate(PhaseNumber);
            return baseText.NullOrEmpty() ? phase + "\n" + extra : baseText + "\n" + phase + "\n" + extra;
        }

        // ---------------- 掉落 ----------------

        private void GiveDropsOnce()
        {
            if (dropsGiven)
            {
                return;
            }
            dropsGiven = true;
            Map map = Map;
            if (map == null)
            {
                return;
            }
            IntVec3 pos = Position;

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
