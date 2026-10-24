using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 启命英（Dandelifeon 变体）：在「肉床」上跑生命游戏，按**变化量**产魔。
    ///
    /// 设计取向与性能约束（都是写死的，别改）：
    ///  · 场地 ≤ 11×11（121 格），用两个 ulong 当位图，邻居数用整数位运算 + 小循环算；
    ///  · 只走 TickRare（250 tick 一代 ≈ 4 秒），**不实现 CompTick**；
    ///  · 产量只看「这一代新生格 + 死亡格」，静态方块（block/beehive）变化为 0 → 产量为 0；
    ///  · 每格连续存活超过 cellLifespan 代就自然凋亡，堵死「摆好走人」；
    ///  · 需要异象 DLC（ModsConfig.AnomalyActive）才有意义，未装时只提示、不跑模拟；
    ///  · 种子来源见 CompFleshSeedSource（血肉心脏）与「拆解扭曲肉」配方。
    /// </summary>
    public class CompProperties_ManaLifeOrchid : CompProperties
    {
        /// <summary>场地边长，必须是奇数，上限 11。</summary>
        public int gridSize = 11;

        /// <summary>每个「新生 / 死亡」格折算多少魔力。</summary>
        public float manaPerChange = 12f;

        /// <summary>单代产量上限。</summary>
        public float maxManaPerGeneration = 600f;

        /// <summary>一格连续存活多少代后自然凋亡。</summary>
        public int cellLifespan = 30;

        /// <summary>一枚血肉之种能供多少代。</summary>
        public int generationsPerSeed = 20;

        /// <summary>至少要铺多少格肉床才启动。</summary>
        public int minFleshCells = 25;

        /// <summary>魔力池输送半径。</summary>
        public float poolRadius = 6f;

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
                yield return "gridSize 必须是 3..11 之间的奇数（居中放置，性能上限写死为 11）。";
            }
            if (manaPerChange <= 0f)
            {
                yield return "manaPerChange must be greater than 0.";
            }
        }
    }

    public class CompManaLifeOrchid : ThingComp
    {
        public const string FleshBedDefName = "Botania_FleshBed";
        public const string FleshSeedDefName = "Botania_FleshSeed";

        private const int Words = 2;             // 121 位落在两个 ulong 里
        private const int GenerationTicks = 250; // TickRare

        private ulong[] live = new ulong[Words];
        private byte[] age = new byte[121];

        private float fuelGenerations;
        private int generation;

        public CompProperties_ManaLifeOrchid Props => (CompProperties_ManaLifeOrchid)props;

        private int Size => Props.gridSize;
        private int Cells => Size * Size;
        private int Radius => Size / 2;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned)
            {
                return;
            }
            if (!ModsConfig.AnomalyActive)
            {
                return;
            }

            // 种子补给：肉床上的血肉之种会被吃掉换代数
            ConsumeSeeds();

            if (fuelGenerations < 1f)
            {
                return;
            }
            if (!GridReady())
            {
                return;
            }

            Step();
        }

        // ---------- 位图工具 ----------

        private bool Get(int index)
        {
            return (live[index >> 6] & (1UL << (index & 63))) != 0UL;
        }

        private void Set(int index, bool value)
        {
            ulong mask = 1UL << (index & 63);
            if (value)
            {
                live[index >> 6] |= mask;
            }
            else
            {
                live[index >> 6] &= ~mask;
            }
        }

        // ---------- 主循环 ----------

        /// <summary>
        /// 场地检查。返回 null 表示可以工作，否则返回「为什么不能工作」的翻译文本。
        /// 除了肉床不够，范围内有别的建筑也要罢工——那些建筑会挡住/干扰仿真区。
        /// </summary>
        private string GridProblem()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            int flesh = 0;
            IntVec3 blockedAt = IntVec3.Invalid;

            for (int dz = -Radius; dz <= Radius; dz++)
            {
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    IntVec3 cell = origin + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    TerrainDef terrain = cell.GetTerrain(map);
                    if (terrain != null && terrain.defName == FleshBedDefName)
                    {
                        flesh++;
                    }
                    if (!blockedAt.IsValid)
                    {
                        Building building = cell.GetFirstBuilding(map);
                        if (building != null && building != parent)
                        {
                            blockedAt = cell;
                        }
                    }
                }
            }

            if (blockedAt.IsValid)
            {
                return "Botania.LifeOrchidBlocked".Translate();
            }
            if (flesh < Props.minFleshCells)
            {
                return "Botania.LifeOrchidNeedBed".Translate(
                    Props.minFleshCells.ToString("0"), Size.ToString("0"));
            }
            return null;
        }

        private bool GridReady()
        {
            return GridProblem() == null;
        }

        private void Step()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            int size = Size;
            int cells = Cells;

            ulong[] next = new ulong[Words];
            int born = 0;
            int died = 0;

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = z * size + x;
                    IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                    TerrainDef terrain = cell.InBounds(map) ? cell.GetTerrain(map) : null;
                    bool onBed = terrain != null && terrain.defName == FleshBedDefName;
                    bool alive = onBed && Get(index);

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
                            int nIndex = zz * size + xx;
                            IntVec3 nCell = origin + new IntVec3(xx - Radius, 0, zz - Radius);
                            TerrainDef nTerrain = nCell.InBounds(map) ? nCell.GetTerrain(map) : null;
                            if (nTerrain != null && nTerrain.defName == FleshBedDefName && Get(nIndex))
                            {
                                neighbours++;
                            }
                        }
                    }

                    bool nextAlive = alive ? (neighbours == 2 || neighbours == 3) : neighbours == 3;

                    // 连续存活太久 → 自然凋亡（这条把静态图案彻底打死）
                    if (nextAlive)
                    {
                        age[index] = (byte)Math.Min(255, age[index] + 1);
                        if (age[index] > Props.cellLifespan)
                        {
                            nextAlive = false;
                        }
                    }
                    else
                    {
                        age[index] = 0;
                    }

                    if (!alive && nextAlive)
                    {
                        born++;
                    }
                    else if (alive && !nextAlive)
                    {
                        died++;
                    }

                    if (nextAlive)
                    {
                        next[index >> 6] |= 1UL << (index & 63);
                    }
                }
            }

            int changes = born + died;
            live = next;
            DrawBirths(map, origin);
            generation++;
            fuelGenerations -= 1f;

            if (changes > 0)
            {
                PayMana(changes);
            }
        }

        /// <summary>把这一代新生的格点亮一下（借原版 fleck，零额外 Def）。</summary>
        private void DrawBirths(Map map, IntVec3 origin)
        {
            int size = Size;
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = z * size + x;
                    if (!Get(index))
                    {
                        continue;
                    }
                    // 只点「刚活过来」的：年龄为 1 说明这一代才出生
                    if (age[index] != 1)
                    {
                        continue;
                    }
                    IntVec3 cell = origin + new IntVec3(x - Radius, 0, z - Radius);
                    if (cell.InBounds(map))
                    {
                        FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 0.6f);
                    }
                }
            }
        }

        private void PayMana(int changes)
        {
            float amount = Math.Min(Props.maxManaPerGeneration, changes * Props.manaPerChange);
            if (amount <= 0f)
            {
                return;
            }
            CompManaStorage pool = FindPool();
            if (pool != null)
            {
                pool.AddMana(amount);
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

        /// <summary>把肉床上的血肉之种吃掉，换代数。</summary>
        private void ConsumeSeeds()
        {
            Map map = parent.Map;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.poolRadius, true))
            {
                if (thing.def != null && thing.def.defName == FleshSeedDefName)
                {
                    thing.Destroy(DestroyMode.Vanish);
                    fuelGenerations += Props.generationsPerSeed;
                    if (AliveCount() == 0)
                    {
                        Randomize();
                    }
                    else
                    {
                        // 手动扰动：每一枚种子除了买代数，还会随机点活几格
                        InjectCells(5);
                    }
                    break; // 一次 tick 只吃一枚，避免瞬间爆仓
                }
            }
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
            string status = "Botania.LifeOrchidStatus".Translate(
                generation.ToString(),
                fuelGenerations.ToString("0"),
                Props.cellLifespan.ToString("0"));
            if (!GridReady())
            {
                status += "\n" + "Botania.LifeOrchidNeedBed".Translate(Props.minFleshCells.ToString("0"), Size.ToString("0"));
            }
            return status;
        }

        private int AliveCount()
        {
            int count = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (Get(i))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>在随机空位上点活若干格（玩家的「扰动」手段）。</summary>
        private void InjectCells(int count)
        {
            Random random = new Random();
            int injected = 0;
            for (int attempt = 0; attempt < count * 8 && injected < count; attempt++)
            {
                int index = random.Next(0, Cells);
                if (Get(index))
                {
                    continue;
                }
                Set(index, true);
                age[index] = 0;
                injected++;
            }
        }

        /// <summary>播一个随机局面（约 30% 存活），给玩家一个能演化起来的起点。</summary>
        private void Randomize()
        {
            Random random = new Random();
            live = new ulong[Words];
            for (int i = 0; i < age.Length; i++)
            {
                age[i] = 0;
            }
            for (int i = 0; i < Cells; i++)
            {
                if (random.NextDouble() < 0.3)
                {
                    Set(i, true);
                }
            }
        }

        /// <summary>诊断用的一行摘要（临时调试工具会调）。</summary>
        public string DebugLine()
        {
            int alive = AliveCount();
            return string.Format(
                "代数={0} 燃料={1:0.#} 活细胞={2} 场地铺好={3} 异象={4}",
                generation, fuelGenerations, alive, GridReady(), ModsConfig.AnomalyActive);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fuelGenerations, "fuelGenerations", 0f);
            Scribe_Values.Look(ref generation, "generation", 0);
            string bits = BitMaskUtility.ToString(live);
            Scribe_Values.Look(ref bits, "liveBits", "");
            string ages = BitMaskUtility.ByteArrayToString(age);
            Scribe_Values.Look(ref ages, "cellAges", "");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                live = BitMaskUtility.Parse(bits, Words);
                age = BitMaskUtility.ParseBytes(ages, 121);
            }
        }
    }

    /// <summary>位图与存档字符串的互转（避免给存档加复杂结构）。</summary>
    public static class BitMaskUtility
    {
        public static string ToString(ulong[] words)
        {
            if (words == null)
            {
                return "";
            }
            string result = "";
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0)
                {
                    result += ",";
                }
                result += words[i].ToString("X16");
            }
            return result;
        }

        public static ulong[] Parse(string text, int length)
        {
            ulong[] words = new ulong[length];
            if (string.IsNullOrEmpty(text))
            {
                return words;
            }
            string[] parts = text.Split(',');
            for (int i = 0; i < words.Length && i < parts.Length; i++)
            {
                ulong value;
                if (ulong.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out value))
                {
                    words[i] = value;
                }
            }
            return words;
        }

        public static string ByteArrayToString(byte[] bytes)
        {
            if (bytes == null)
            {
                return "";
            }
            char[] chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)('A' + Math.Min(25, bytes[i] / 10));
            }
            return new string(chars);
        }

        public static byte[] ParseBytes(string text, int length)
        {
            byte[] bytes = new byte[length];
            if (string.IsNullOrEmpty(text))
            {
                return bytes;
            }
            for (int i = 0; i < length && i < text.Length; i++)
            {
                bytes[i] = (byte)Math.Max(0, (text[i] - 'A') * 10);
            }
            return bytes;
        }
    }
}
