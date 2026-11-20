using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 根须缠绕的持续伤害：挂上后每 intervalTicks 扣一次自然伤害，优先伤害腿部。
    /// 定身本身由 HediffDef 的 capMods（Moving 0%）负责，这里只管掉血。
    /// </summary>
    public class HediffCompProperties_GaiaRooted : HediffCompProperties
    {
        public float damagePerTickInterval = 2f;
        public int intervalTicks = 120;
        public float armorPenetration = 0.5f;

        public HediffCompProperties_GaiaRooted()
        {
            compClass = typeof(HediffComp_GaiaRooted);
        }
    }

    public class HediffComp_GaiaRooted : HediffComp
    {
        private int nextTick;

        private HediffCompProperties_GaiaRooted Props => (HediffCompProperties_GaiaRooted)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            nextTick = Find.TickManager.TicksGame + Props.intervalTicks;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.Dead || pawn.Map == null)
            {
                return;
            }
            // 定身：每 tick 停住脚步（不能把 Moving 压 0，否则小人会倒地）
            pawn.pather?.StopDead();

            if (Find.TickManager.TicksGame < nextTick)
            {
                return;
            }
            nextTick = Find.TickManager.TicksGame + Props.intervalTicks;

            DamageInfo damage = new DamageInfo(DamageDefOf.Blunt, Props.damagePerTickInterval,
                Props.armorPenetration, -1f, null);
            damage.SetBodyRegion(BodyPartHeight.Bottom, BodyPartDepth.Outside);
            pawn.TakeDamage(damage);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref nextTick, "gaiaRootedNextTick", 0);
        }
    }
}
