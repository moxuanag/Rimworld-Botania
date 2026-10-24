using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 挂在血肉核心（FleshmassNucleus）上：核心每完成一次「收缩产肉」的节律，
    /// 就顺带在脚边掉几枚血肉之种。
    ///
    /// 为什么不直接 hook CreateMeat：那是私有方法，实机验证时没触发（可能走的是别的分支），
    /// 而核心的产肉节律本身是「每天一次」，所以这里用同样的节律自己计时——
    /// 核心休眠/倒地时不产，保证「有肉才有种子」。
    /// </summary>
    public class CompProperties_FleshmassCoreSeeds : CompProperties
    {
        public string seedDefName = "Botania_FleshSeed";

        /// <summary>一个周期给几枚（默认 3，和「产肉一次」对齐）。</summary>
        public int seedsPerCycle = 3;

        /// <summary>周期长度，默认 60000 tick = 1 天。</summary>
        public int cycleTicks = 60000;

        public CompProperties_FleshmassCoreSeeds()
        {
            compClass = typeof(CompFleshmassCoreSeeds);
        }
    }

    public class CompFleshmassCoreSeeds : ThingComp
    {
        private int ticksSinceLastCycle;

        public CompProperties_FleshmassCoreSeeds Props => (CompProperties_FleshmassCoreSeeds)props;

        private ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed(Props.seedDefName, false);

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || parent.Map == null || !ModsConfig.AnomalyActive)
            {
                return;
            }
            Pawn nucleus = parent as Pawn;
            if (nucleus != null && (nucleus.Dead || nucleus.Downed))
            {
                return;
            }
            ticksSinceLastCycle++;
            if (ticksSinceLastCycle < Props.cycleTicks)
            {
                return;
            }
            ticksSinceLastCycle = 0;
            SpawnSeeds();
        }

        private void SpawnSeeds()
        {
            ThingDef seedDef = SeedDef;
            if (seedDef == null || parent.Map == null)
            {
                return;
            }
            Thing seeds = ThingMaker.MakeThing(seedDef, null);
            seeds.stackCount = Props.seedsPerCycle;
            GenPlace.TryPlaceThing(seeds, parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned || !ModsConfig.AnomalyActive)
            {
                return null;
            }
            float left = (Props.cycleTicks - ticksSinceLastCycle) / 60000f;
            return "Botania.CoreSeedCycle".Translate(left.ToString("0.0"), Props.seedsPerCycle.ToString());
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksSinceLastCycle, "ticksSinceLastCycle", 0);
        }
    }
}
