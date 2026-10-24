using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 启命英（Dandelifeon 变体）——玩家自己摆图案的生命游戏。
    ///
    /// 玩法：
    ///  · 在 11×11 的肉床（TerrainDef Botania_FleshBed）上铺「血肉之种」，每一枚 = 一个活细胞；
    ///  · 图案摆好后，选中启命英按「启动」，它每 TickRare 推一代（约 4 秒）；
    ///  · 产魔只按**变化量**计价：`(新生格 + 死亡格) × manaPerChange`，静止图案为 0；
    ///  · 每格连续存活超过 cellLifespan 代自然凋亡，逼迫玩家周期性扰动；
    ///  · 场地里站着人（或别的建筑）时暂停——所以玩家摆完图案要退到范围外再启动。
    ///
    /// 性能：只用 TickRare；场地上限 11×11；位图 + 小循环；每代只增删有变化的格子。
    /// </summary>
    public class CompProperties_ManaLifeOrchid : CompProperties
    {
        public int gridSize = 11;
        public float manaPerChange = 12f;
        public float maxManaPerGeneration = 600f;
        public int cellLifespan = 30;
        public float poolRadius = 6f;
        public string seedDefName = "Botania_FleshSeed";
        public string bedDefName = "Botania_FleshBed";

        public CompProperties_ManaLifeOrchid()
        {
            compClass = typeof(CompManaLifeOrchid);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (gridSize < 3 || gridSize % 2 == 0 || gridSize > 11)
            {
                yield return "gridSize 必须是 3..11 的奇数（性能上限写死为 11）。";
            }
        }
    }

    public class CompManaLifeOrchid : ThingComp
    {
        public const string SeedDefName = "Botania_FleshSeed";
        public const string BedDefName = "Botania_FleshBed";


        private bool running;
        private int generation;
        private readonly byte[] age = new byte[121];

        public CompProperties_ManaLifeOrchid Props => (CompProperties_ManaLifeOrchid)props;

        public int Size => Props.gridSize;
        public int Cells => Size * Size;
        public int Radius => Size / 2;
        public bool Running => running;
        public int Generation => generation;

        private ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed(Props.seedDefName, false);

        // ---------- 场地检查 ----------

        /// <summary>null 表示可以工作，否则返回不能工作的原因（已翻译）。</summary>
        public string GridProblem()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            IntVec3 origin = parent.Position;
            int bed = 0;
            bool blockedByBuilding = false;
            bool blockedByPawn = false;

            for (int dz = -Radius; dz <= Radius && !blockedByBuilding; dz++)
            {
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    IntVec3 cell = origin + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    TerrainDef terrain = cell.GetTerrain(map);
                    if (terrain != null && terrain.defName == Props.bedDefName)
                    {
                        bed++;
                    }
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (thing == parent)
                        {
                            continue;
                        }
                        Building building = thing as Building;
                        if (building != null && building.def.building != null && building.def.building.isEdifice)
                        {
                            blockedByBuilding = true;
                            break;
                        }
                        Pawn pawn = thing as Pawn;
                        if (pawn != null)
                        {
                            blockedByPawn = true;
                        }
                    }
                }
            }

            if (blockedByBuilding)
            {
                return "Botania.LifeOrchidBlocked".Translate();
            }
            if (blockedByPawn)
            {
                return "Botania.LifeOrchidPawnInside".Translate();
            }
            if (bed < 25)
            {
                return "Botania.LifeOrchidNeedBed".Translate("25", Size.ToString("0"));
            }
            return null;
        }

        // ---------- 主循环 ----------

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || !running || !ModsConfig.AnomalyActive)
            {
                return;
            }
            if (GridProblem() != null)
            {
                return;
            }
            Step();
        }

        private void Step()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            ThingDef seedDef = SeedDef;
            if (seedDef == null)
            {
                return;
            }

            int size = Size;
            bool[] alive = new bool[Cells];
            bool[] next = new bool[Cells];

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = z * size + x;
                    IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                    alive[index] = cell.InBounds(map) && HasSeed(cell, map, seedDef);
                }
            }

            int born = 0;
            int died = 0;
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = z * size + x;
                    int neighbours = 0;
                    for (int nz = -1; nz <= 1; nz++)
                    {
                        int zz = z + nz;
                        if (zz < 0 || zz >= size)
                        {
                            continue;
                        }
                        for (int nx = -1; nx <= 1; nx++)
                        {
                            if (nx == 0 && nz == 0)
                            {
                                continue;
                            }
                            int xx = x + nx;
                            if (xx < 0 || xx >= size)
                            {
                                continue;
                            }
                            if (alive[zz * size + xx])
                            {
                                neighbours++;
                            }
                        }
                    }

                    bool nextAlive = alive[index] ? (neighbours == 2 || neighbours == 3) : neighbours == 3;

                    if (nextAlive)
                    {
                        age[index] = (byte)Math.Min(255, age[index] + 1);
                        if (age[index] > Props.cellLifespan)
                        {
                            nextAlive = false; // 自然凋亡
                        }
                    }
                    else
                    {
                        age[index] = 0;
                    }

                    next[index] = nextAlive;
                    if (!alive[index] && nextAlive)
                    {
                        born++;
                    }
                    else if (alive[index] && !nextAlive)
                    {
                        died++;
                    }
                }
            }

            // 落盘：只动有变化的格
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = z * size + x;
                    if (alive[index] == next[index])
                    {
                        continue;
                    }
                    IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    if (next[index])
                    {
                        Thing seed = ThingMaker.MakeThing(seedDef, null);
                        seed.stackCount = 1;
                        GenSpawn.Spawn(seed, cell, map);
                    }
                    else
                    {
                        DestroySeedAt(cell, map, seedDef);
                    }
                }
            }

            generation++;
            if (born + died > 0)
            {
                float amount = Math.Min(Props.maxManaPerGeneration, (born + died) * Props.manaPerChange);
                CompManaStorage pool = FindPool();
                if (pool != null)
                {
                    pool.AddMana(amount);
                }
            }
        }

        private static bool HasSeed(IntVec3 cell, Map map, ThingDef seedDef)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def == seedDef)
                {
                    return true;
                }
            }
            return false;
        }

        private static void DestroySeedAt(IntVec3 cell, Map map, ThingDef seedDef)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def == seedDef)
                {
                    things[i].Destroy(DestroyMode.Vanish);
                    return;
                }
            }
        }

        private CompManaStorage FindPool()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.poolRadius, true))
            {
                CompManaStorage storage = thing.TryGetComp<CompManaStorage>();
                if (storage != null && storage.AcceptsMana && !storage.IsFull)
                {
                    return storage;
                }
            }
            return null;
        }

        // ---------- 界面 ----------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Map == null || !ModsConfig.AnomalyActive)
            {
                yield break;
            }
            Command_Action toggle = new Command_Action();
            toggle.defaultLabel = (running ? "Botania.LifeOrchidStop" : "Botania.LifeOrchidStart").Translate();
            toggle.defaultDesc = "Botania.LifeOrchidToggleDesc".Translate();
            toggle.action = delegate
            {
                running = !running;
                if (running)
                {
                    generation = 0;
                    for (int i = 0; i < age.Length; i++)
                    {
                        age[i] = 0;
                    }
                }
            };
            if (running)
            {
                toggle.hotKey = KeyBindingDefOf.Misc1;
            }
            yield return toggle;
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            if (!ModsConfig.AnomalyActive)
            {
                return "Botania.LifeOrchidNoAnomaly".Translate();
            }

            int aliveCount = 0;
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                    if (cell.InBounds(map) && HasSeed(cell, map, SeedDef))
                    {
                        aliveCount++;
                    }
                }
            }

            string status = "Botania.LifeOrchidStatus2".Translate(
                running ? "Botania.LifeOrchidRunning".Translate() : "Botania.LifeOrchidPaused".Translate(),
                generation.ToString(),
                aliveCount.ToString(),
                Props.cellLifespan.ToString("0"));

            string problem = GridProblem();
            if (problem != null)
            {
                status += "\n" + problem;
            }
            return status;
        }

        /// <summary>调试摘要（临时调试工具会调）。</summary>
        public string DebugLine()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            int alive = 0;
            if (map != null)
            {
                ThingDef seedDef = SeedDef;
                for (int z = 0; z < Size; z++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                        if (cell.InBounds(map) && HasSeed(cell, map, seedDef))
                        {
                            alive++;
                        }
                    }
                }
            }
            return string.Format("运行={0} 代数={1} 活细胞={2} 场地问题={3} 异象={4}",
                running, generation, alive, GridProblem() ?? "无", ModsConfig.AnomalyActive);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref running, "running", false);
            Scribe_Values.Look(ref generation, "generation", 0);
        }
    }
}
