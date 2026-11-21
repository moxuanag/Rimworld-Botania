using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉套装的快速愈合：每 2 秒治疗一个伤口。
    ///
    /// 实现方式：挂在 Hediff 上（Botania_TerrasteelBlessing），
    /// 每 intervalTicks 找一处「非永久、未愈合」的 Hediff_Injury，按 healAmount 愈合它；
    /// 愈合到 0 就移除该伤口。
    /// </summary>
    public class HediffCompProperties_TerrasteelMending : HediffCompProperties
    {
        /// <summary>治疗间隔（tick）。120 = 2 秒。</summary>
        public int intervalTicks = 120;

        /// <summary>保留字段（当前实现是直接治愈整处伤口，不再按严重度削减）。</summary>
        public float healAmount = 2f;

        public HediffCompProperties_TerrasteelMending()
        {
            compClass = typeof(HediffComp_TerrasteelMending);
        }
    }

    public class HediffComp_TerrasteelMending : HediffComp
    {
        private int nextHealTick = -1;

        private HediffCompProperties_TerrasteelMending Props =>
            (HediffCompProperties_TerrasteelMending)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.health?.hediffSet == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (nextHealTick < 0)
            {
                nextHealTick = now + Props.intervalTicks;
                return;
            }
            if (now < nextHealTick)
            {
                return;
            }
            nextHealTick = now + Props.intervalTicks;

            // 找一处最严重的、可愈合的伤口
            Hediff_Injury worst = null;
            float worstSeverity = 0f;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury inj && inj.Severity > 0.01f && !inj.IsPermanent())
                {
                    if (inj.Severity > worstSeverity)
                    {
                        worstSeverity = inj.Severity;
                        worst = inj;
                    }
                }
            }
            if (worst == null)
            {
                return;
            }

            // 用户要求：每 2 秒「治愈一个伤口」，直接移除整处伤口，而不是缓慢减严重度
            pawn.health.RemoveHediff(worst);
        }

        public override string CompTipStringExtra => "Botania.TerrasteelMending.Tip".Translate();
    }
}
