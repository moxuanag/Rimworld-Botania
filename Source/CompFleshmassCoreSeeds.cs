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
        /// <summary>
        /// 已生成的核全部登记在这里，由 MapComponent 统一按 TickRare 推进。
        /// 不依赖 ThingComp.CompTick（Pawn 上的 comp tick 路径实测不可靠）。
        /// </summary>
        public static readonly System.Collections.Generic.List<CompFleshmassCoreSeeds> Registry =
            new System.Collections.Generic.List<CompFleshmassCoreSeeds>();

        private int ticksSinceLastCycle;

        public CompProperties_FleshmassCoreSeeds Props => (CompProperties_FleshmassCoreSeeds)props;

        private ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed(Props.seedDefName, false);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            Registry.Remove(this);
            base.PostDeSpawn(map, mode);
        }

        /// <summary>由 MapComponent_BotaniaCores 每 TickRare 调用。</summary>
        public void Advance(int ticks)
        {
            if (!parent.Spawned || parent.Map == null || !ModsConfig.AnomalyActive)
            {
                return;
            }
            if (parent.Destroyed || parent.DestroyedOrNull())
            {
                return;
            }
            Pawn nucleus = parent as Pawn;
            if (nucleus != null && (nucleus.Dead || nucleus.Downed))
            {
                return;
            }
            // 只有真正「休眠」时才停（IsDormant）。
            // 注意不能用 !IsActive：IsActive 指的是实体处于激活态，
            // 核心平时是 Passive（活性缓慢涨落），用错了会让计时永远停住。
            CompActivity activity = parent.TryGetComp<CompActivity>();
            if (activity != null && activity.IsDormant)
            {
                return;
            }

            ticksSinceLastCycle += ticks;
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
            Map map = parent.Map;
            System.Collections.Generic.List<IntVec3> ring = new System.Collections.Generic.List<IntVec3>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, 3.5f, false))
            {
                ring.Add(c);
            }
            System.Random random = new System.Random();
            int placed = 0;
            for (int i = 0; i < Props.seedsPerCycle; i++)
            {
                IntVec3 cell = parent.Position;
                for (int attempt = 0; attempt < 12 && ring.Count > 0; attempt++)
                {
                    IntVec3 candidate = ring[random.Next(ring.Count)];
                    if (candidate.InBounds(map) && candidate.Standable(map) && !candidate.Fogged(map))
                    {
                        cell = candidate;
                        break;
                    }
                }
                Thing seed = ThingMaker.MakeThing(seedDef, null);
                seed.stackCount = 1;
                GenSpawn.Spawn(seed, cell, map);
                placed++;
            }
            if (placed == 0)
            {
                Thing seeds = ThingMaker.MakeThing(seedDef, null);
                seeds.stackCount = Props.seedsPerCycle;
                GenPlace.TryPlaceThing(seeds, parent.Position, map, ThingPlaceMode.Near);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned || !ModsConfig.AnomalyActive)
            {
                return null;
            }
            float left = (Props.cycleTicks - ticksSinceLastCycle) / 60000f;
            CompActivity activity = parent.TryGetComp<CompActivity>();
            string state = activity == null
                ? "no-activity-comp"
                : string.Format("active={0} dormant={1} level={2:0}%",
                    activity.IsActive, activity.IsDormant, activity.ActivityLevel * 100f);
            return "Botania.CoreSeedCycle".Translate(left.ToString("0.0"), Props.seedsPerCycle.ToString())
                + "\n[debug] ticks=" + ticksSinceLastCycle + " " + state;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksSinceLastCycle, "ticksSinceLastCycle", 0);
        }
    }
}
