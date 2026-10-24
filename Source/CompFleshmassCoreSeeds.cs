using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 血肉核心（FleshmassNucleus）上的「抽取血肉之种」：
    /// 走原版 CompUsable 的 UseItem 作业——玩家右键核心 → 选一名小人 → 小人走过去干 20 秒 →
    /// 拿到血肉之种，然后核心要休息一天。这样「抽取」是小人的工作，不是天上掉下来的按钮。
    /// </summary>
    public class CompProperties_FleshmassCoreSeeds : CompProperties_Usable
    {
        public string seedDefName = "Botania_FleshSeed";
        public int seedsPerUse = 2;
        public int cooldownTicks = 60000; // 1 天

        public CompProperties_FleshmassCoreSeeds()
        {
            compClass = typeof(CompFleshmassCoreSeeds);
            useJob = DefDatabase<JobDef>.GetNamed("UseItem", false);
            useDuration = 1200;
        }
    }

    public class CompFleshmassCoreSeeds : CompUsable
    {
        private int nextUseTick;

        public CompProperties_FleshmassCoreSeeds CoreProps => (CompProperties_FleshmassCoreSeeds)props;

        private ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed(CoreProps.seedDefName, false);

        public override AcceptanceReport CanBeUsedBy(Pawn p, bool forced = false, bool ignoreReserveAndReachable = false)
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextUseTick)
            {
                return "Botania.CoreSeedCooling".Translate(((nextUseTick - now) / 60000f).ToString("0.0"));
            }
            return base.CanBeUsedBy(p, forced, ignoreReserveAndReachable);
        }

        public override void UsedBy(Pawn p)
        {
            base.UsedBy(p);
            ThingDef seedDef = SeedDef;
            if (seedDef == null || parent.Map == null)
            {
                return;
            }
            nextUseTick = Find.TickManager.TicksGame + CoreProps.cooldownTicks;

            Thing seeds = ThingMaker.MakeThing(seedDef, null);
            seeds.stackCount = CoreProps.seedsPerUse;
            GenPlace.TryPlaceThing(seeds, parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        /// <summary>给「选中小人 → 右键核心」用的菜单项。核心是 Pawn，原版不会遍历它的 comps 菜单。</summary>
        public FloatMenuOption MakeFloatMenuOption(Pawn selPawn)
        {
            string label = "Botania.ExtractCoreSeed".Translate();
            AcceptanceReport report = CanBeUsedBy(selPawn, false, true);
            if (!report.Accepted)
            {
                return new FloatMenuOption(label + "（" + report.Reason + "）", null);
            }
            JobDef useJob = DefDatabase<JobDef>.GetNamed("UseItem", false);
            if (useJob == null)
            {
                return null;
            }
            return new FloatMenuOption(label, delegate
            {
                Job job = new Job(useJob, parent);
                job.count = 1;
                selPawn.jobs.TryTakeOrderedJob(job);
            }, MenuOptionPriority.Default, null, parent);
        }

        public override string CompInspectStringExtra()
        {
            int now = Find.TickManager.TicksGame;
            if (now >= nextUseTick)
            {
                return "Botania.CoreSeedReady".Translate();
            }
            return "Botania.CoreSeedCooling".Translate(((nextUseTick - now) / 60000f).ToString("0.0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextUseTick, "nextUseTick", 0);
        }
    }
}
