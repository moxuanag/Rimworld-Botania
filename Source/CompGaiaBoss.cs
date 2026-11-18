using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚·世界之魂（Boss）配置。
    ///
    /// 战斗循环（见 docs/盖亚Boss设计文档.md）：
    ///   带护盾出场 → 破盾 → 瘫痪 20 秒（受伤 ×1.5）→ 护盾瞬满
    ///   → 瘫痪窗口内打到阶段阈值 → 锁血 5 秒（无敌）→ 形态升级、护盾瞬满
    ///   → 窗口内没打到阈值 → 只有护盾回满，阶段不变，继续打
    /// </summary>
    public class CompProperties_GaiaBoss : CompProperties
    {
        /// <summary>护盾耐久。</summary>
        public float shieldMax = 300f;

        /// <summary>有盾时的伤害系数（0.2 = 80% 减伤）。</summary>
        public float shieldDamageFactor = 0.2f;

        /// <summary>破盾后的瘫痪时长。</summary>
        public int paralysisTicks = 1200;

        /// <summary>瘫痪期间的受伤系数（1.5 = 150%）。</summary>
        public float paralysisDamageFactor = 1.5f;

        /// <summary>阶段切换的锁血无敌时长。</summary>
        public int lockTicks = 300;

        /// <summary>阶段阈值（血量百分比）。</summary>
        public List<float> phaseThresholds = new List<float> { 0.66f, 0.33f };

        /// <summary>各阶段的护盾上限。</summary>
        public List<float> phaseShieldMax = new List<float> { 300f, 400f, 500f };

        /// <summary>死亡掉落的物品。</summary>
        public string coreDefName = "Botania_GaiaCore";
        public string blueprintDefName = "Botania_GaiaSpirit";
        public string pixieDustDefName = "Botania_PixieDust";
        public string terrasteelDefName = "Botania_TerrasteelIngot";

        public CompProperties_GaiaBoss()
        {
            compClass = typeof(CompGaiaBoss);
        }
    }

    /// <summary>
    /// Boss 的护盾 / 瘫痪 / 锁血 / 阶段 / 掉落。
    ///
    /// 伤害逻辑全部走 PostPreApplyDamage，不依赖 Hediff：
    ///  · 锁血期间：完全免疫（absorbed）
    ///  · 瘫痪期间：伤害 ×1.5
    ///  · 有盾期间：伤害 ×0.2，并从护盾耐久里扣；扣完立刻进入瘫痪
    ///  · 护盾在瘫痪结束时瞬满
    /// </summary>
    public class CompGaiaBoss : ThingComp
    {
        private float shield;
        private int phaseIndex;
        private int invulnerableUntil = -1;
        private int paralyzedUntil = -1;
        private bool dropsGiven;

        private CompProperties_GaiaBoss Props => (CompProperties_GaiaBoss)props;

        private Pawn Boss => parent as Pawn;

        private int Now => Find.TickManager.TicksGame;

        public float Shield => shield;

        public float ShieldMax
        {
            get
            {
                int idx = phaseIndex;
                if (Props.phaseShieldMax == null || Props.phaseShieldMax.Count == 0)
                {
                    return Props.shieldMax;
                }
                if (idx >= Props.phaseShieldMax.Count)
                {
                    idx = Props.phaseShieldMax.Count - 1;
                }
                return Props.phaseShieldMax[idx];
            }
        }

        public int PhaseNumber => phaseIndex + 1;

        public bool IsInvulnerable => invulnerableUntil > Now;

        public bool IsParalyzed => paralyzedUntil > Now;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (shield <= 0f)
            {
                shield = ShieldMax;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn boss = Boss;
            if (boss == null)
            {
                return;
            }

            if (boss.Dead)
            {
                GiveDropsOnce();
                return;
            }

            // 瘫痪结束：护盾瞬满
            if (paralyzedUntil > 0 && Now >= paralyzedUntil)
            {
                paralyzedUntil = -1;
                shield = ShieldMax;
                Messages.Message("Botania.GaiaBossShieldBack".Translate(),
                    new LookTargets(boss), MessageTypeDefOf.ThreatBig, false);
            }

            if (invulnerableUntil > 0 && Now >= invulnerableUntil)
            {
                invulnerableUntil = -1;
            }

            TryAdvancePhase(boss);
        }

        /// <summary>
        /// Pawn 的血量百分比必须走 health.summaryHealth：
        /// Pawn 的 HitPoints/MaxHitPoints 不是「整体血量」，比值恒为 0，
        /// 用它判断阶段会一进场就跳到下一阶段（实测踩过）。
        /// </summary>
        private static float HealthPercent(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.summaryHealth == null)
            {
                return 1f;
            }
            return Mathf_Clamp01(pawn.health.summaryHealth.SummaryHealthPercent);
        }

        private static float Mathf_Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>血量掉到阈值以下且当前不在瘫痪窗口时，锁血 5 秒并升级形态。</summary>
        private void TryAdvancePhase(Pawn boss)
        {
            if (IsInvulnerable)
            {
                return;
            }
            if (Props.phaseThresholds == null || phaseIndex >= Props.phaseThresholds.Count)
            {
                return;
            }
            float pct = HealthPercent(boss);
            if (pct > Props.phaseThresholds[phaseIndex])
            {
                return;
            }

            phaseIndex++;
            invulnerableUntil = Now + Props.lockTicks;
            paralyzedUntil = -1;
            shield = ShieldMax;

            if (boss.stances != null && boss.stances.stunner != null)
            {
                boss.stances.stunner.StunFor(Props.lockTicks, parent, false, true, true);
            }

            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaBossPhase.Label".Translate(PhaseNumber),
                "Botania.GaiaBossPhase.Text".Translate(PhaseNumber),
                LetterDefOf.ThreatBig,
                new LookTargets(boss));
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            if (Boss == null || Boss.Dead)
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
                amount *= Props.paralysisDamageFactor;
            }

            // 有盾：80% 减伤，并从护盾里扣
            if (shield > 0f)
            {
                float reduced = amount * Props.shieldDamageFactor;
                shield -= reduced;
                if (shield <= 0f)
                {
                    shield = 0f;
                    StartParalysis();
                }
                amount = reduced;
            }

            dinfo.SetAmount(amount);
        }

        private void StartParalysis()
        {
            paralyzedUntil = Now + Props.paralysisTicks;
            Pawn boss = Boss;
            if (boss != null && boss.stances != null && boss.stances.stunner != null)
            {
                boss.stances.stunner.StunFor(Props.paralysisTicks, parent, false, true, true);
            }
            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaBossShieldBroken.Label".Translate(),
                "Botania.GaiaBossShieldBroken.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(parent));
        }

        private void GiveDropsOnce()
        {
            if (dropsGiven)
            {
                return;
            }
            dropsGiven = true;
            Map map = parent.Map;
            IntVec3 pos = parent.Position;
            if (map == null)
            {
                return;
            }

            SpawnItem(map, pos, Props.coreDefName, 1);
            SpawnItem(map, pos, Props.pixieDustDefName, Rand.RangeInclusive(8, 15));
            SpawnItem(map, pos, Props.terrasteelDefName, Rand.RangeInclusive(3, 5));

            // 蓝图：还没解锁盖亚研究时必掉，之后 25%
            bool unlocked = false;
            ResearchProjectDef gaia = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Botania_Gaia");
            if (gaia != null && gaia.IsFinished)
            {
                unlocked = true;
            }
            if (!unlocked || Rand.Chance(0.25f))
            {
                SpawnItem(map, pos, Props.blueprintDefName, 1);
            }
        }

        private static void SpawnItem(Map map, IntVec3 pos, string defName, int count)
        {
            if (count <= 0 || defName.NullOrEmpty())
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

        public override string CompInspectStringExtra()
        {
            string shieldText = shield <= 0f
                ? (IsParalyzed ? "Botania.GaiaBossInspect.Paralyzed".Translate() : "Botania.GaiaBossInspect.NoShield".Translate())
                : "Botania.GaiaBossInspect.Shield".Translate(shield, ShieldMax);
            return "Botania.GaiaBossInspect.Phase".Translate(PhaseNumber) + "\n" + shieldText;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref shield, "gaiaShield", 0f);
            Scribe_Values.Look(ref phaseIndex, "gaiaPhaseIndex", 0);
            Scribe_Values.Look(ref invulnerableUntil, "gaiaInvulnUntil", -1);
            Scribe_Values.Look(ref paralyzedUntil, "gaiaParalyzedUntil", -1);
            Scribe_Values.Look(ref dropsGiven, "gaiaDropsGiven", false);
        }
    }
}
