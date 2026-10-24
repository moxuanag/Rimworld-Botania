using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 挂在原版「血肉心脏」（FleshmassHeart，异象 DLC）上的种子来源：
    ///  · 右键/按钮可以定期抽取血肉之种；
    ///  · 心脏被摧毁时，就地掉落一批种子。
    /// 该 Comp 由 Patches/Botania_FleshmassHeart.xml 用 MayRequire 挂上去，
    /// 没装异象 DLC 时那条补丁根本不会执行。
    /// </summary>
    public class CompProperties_FleshSeedSource : CompProperties
    {
        public string seedDefName = "Botania_FleshSeed";

        /// <summary>一次抽取给几枚。</summary>
        public int seedsPerExtraction = 2;

        /// <summary>抽取冷却（180000 tick = 3 天）。</summary>
        public int cooldownTicks = 180000;

        /// <summary>心脏被摧毁时掉几枚。</summary>
        public int dropOnDestroy = 4;

        public CompProperties_FleshSeedSource()
        {
            compClass = typeof(CompFleshSeedSource);
        }
    }

    public class CompFleshSeedSource : ThingComp
    {
        private int nextExtractTick;

        public CompProperties_FleshSeedSource Props => (CompProperties_FleshSeedSource)props;

        private ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed(Props.seedDefName, false);

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (SeedDef == null || parent.Map == null)
            {
                yield break;
            }

            int now = Find.TickManager.TicksGame;
            bool cooling = now < nextExtractTick;

            Command_Action command = new Command_Action();
            command.defaultLabel = "Botania.ExtractFleshSeed".Translate();
            command.defaultDesc = "Botania.ExtractFleshSeedDesc".Translate();
            command.icon = SeedDef.uiIcon;
            if (cooling)
            {
                int left = nextExtractTick - now;
                command.Disable("Botania.ExtractFleshSeedCooling".Translate((left / 60000f).ToString("0.0")));
            }
            else
            {
                command.action = Extract;
            }
            yield return command;
        }

        private void Extract()
        {
            ThingDef seedDef = SeedDef;
            if (seedDef == null || parent.Map == null)
            {
                return;
            }
            nextExtractTick = Find.TickManager.TicksGame + Props.cooldownTicks;
            GiveSeeds(seedDef, Props.seedsPerExtraction);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ThingDef seedDef = SeedDef;
            if (seedDef != null && previousMap != null && Props.dropOnDestroy > 0)
            {
                GiveSeeds(seedDef, Props.dropOnDestroy);
            }
        }

        private void GiveSeeds(ThingDef seedDef, int count)
        {
            if (count <= 0)
            {
                return;
            }
            Thing seeds = ThingMaker.MakeThing(seedDef, null);
            seeds.stackCount = count;
            GenPlace.TryPlaceThing(seeds, parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        public override string CompInspectStringExtra()
        {
            int now = Find.TickManager.TicksGame;
            if (now >= nextExtractTick)
            {
                return null;
            }
            return "Botania.ExtractFleshSeedReady".Translate(((nextExtractTick - now) / 60000f).ToString("0.0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextExtractTick, "nextExtractTick", 0);
        }
    }
}
