using System;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 炉心蕙：只在封闭、有屋顶的房间里工作，室温越高、房间越大产得越多。
    /// 产量 ∝ (T − 20) × √A，A 是这朵花分到的有效面积（房间面积 ÷ 房间里的花数，上限 150）。
    /// 维持成本由游戏自身的温度模拟承担：房间越细长、墙越外露，掉温越快，玩家就得烧更多热。
    /// </summary>
    public class CompProperties_ManaFurnaceOrchid : CompProperties
    {
        /// <summary>产量系数 k：每秒魔力 = k × (T − 20) × √A。0.20 时 12 格房在 150°C 正好摸到上限。</summary>
        public float manaScale = 0.2f;

        /// <summary>每朵花至少要分到这么多格有效面积才开始工作（花占的那格不计入）。</summary>
        public float minAreaPerFlower = 11f;

        /// <summary>房间面积软上限：超过部分不计入 A。</summary>
        public float maxRoomArea = 150f;

        /// <summary>产量硬上限：温度再高也不超过这个值（基准 = 3 座火红莲）。</summary>
        public float maxManaPerSecond = 90f;

        /// <summary>产魔的起始温度（°C），低于它不产魔。</summary>
        public float baseTemperature = 20f;

        /// <summary>搜索魔力池的半径（格）。</summary>
        public float poolRadius = 6f;

        /// <summary>超过这个温度开始掉 HP。</summary>
        public float overheatThreshold = 150f;

        /// <summary>每高 1°C 每秒掉多少 HP。</summary>
        public float damagePerDegree = 0.04f;

        /// <summary>到这个温度就可能在自燃。</summary>
        public float ignitionThreshold = 195f;

        /// <summary>每秒自燃概率。</summary>
        public float igniteChancePerSecond = 0.01f;

        public CompProperties_ManaFurnaceOrchid()
        {
            compClass = typeof(CompManaFurnaceOrchid);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaScale <= 0f)
            {
                yield return "manaScale must be greater than 0.";
            }
            if (minAreaPerFlower <= 0f)
            {
                yield return "minAreaPerFlower must be greater than 0.";
            }
            if (maxRoomArea < minAreaPerFlower)
            {
                yield return "maxRoomArea must not be smaller than minAreaPerFlower.";
            }
            if (maxManaPerSecond <= 0f)
            {
                yield return "maxManaPerSecond must be greater than 0.";
            }
        }
    }

    public class CompManaFurnaceOrchid : ThingComp
    {
        private const int TicksPerSettle = 60;

        private int ticksAccum;

        public CompProperties_ManaFurnaceOrchid Props => (CompProperties_ManaFurnaceOrchid)props;

        /// <summary>这朵花当前分到的有效面积（供 UI 显示）。</summary>
        public float EffectiveArea { get; private set; }

        /// <summary>房间温度（供 UI 显示）。</summary>
        public float RoomTemperature { get; private set; }

        /// <summary>当前每秒产量（供 UI 显示）。</summary>
        public float CurrentOutput { get; private set; }

        /// <summary>不能工作的原因，null 表示正常。</summary>
        public string BlockReason { get; private set; }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < TicksPerSettle)
            {
                return;
            }
            ticksAccum = 0;
            Settle();
        }

        private void Settle()
        {
            Map map = parent.Map;
            BlockReason = null;
            CurrentOutput = 0f;

            Room room = parent.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || !parent.Position.Roofed(map))
            {
                BlockReason = "Botania.FurnaceOpenAir".Translate();
                return;
            }

            float area = Math.Min(room.CellCount, Props.maxRoomArea);
            int flowers = CountFlowersInRoom(room);
            float share = area / Math.Max(1, flowers);
            EffectiveArea = share;
            RoomTemperature = room.Temperature;

            if (IsCrowded())
            {
                BlockReason = "Botania.FurnaceTooClose".Translate();
                return;
            }
            if (share <= Props.minAreaPerFlower)
            {
                BlockReason = "Botania.FurnaceTooSmall".Translate(Props.minAreaPerFlower.ToString("0"));
                return;
            }

            float delta = room.Temperature - Props.baseTemperature;
            if (delta <= 0f)
            {
                BlockReason = "Botania.FurnaceTooCold".Translate(Props.baseTemperature.ToString("0"));
                return;
            }

            float mana = Props.manaScale * delta * (float)Math.Sqrt(share);
            if (mana > Props.maxManaPerSecond)
            {
                mana = Props.maxManaPerSecond;
            }
            CurrentOutput = mana;

            CompManaStorage pool = FindPool();
            if (pool != null)
            {
                pool.AddMana(mana);
            }

            Overheat(room.Temperature);
        }

        /// <summary>超过阈值掉 HP；逼近沸点有概率自燃。</summary>
        private void Overheat(float temperature)
        {
            if (temperature > Props.overheatThreshold)
            {
                float damage = (temperature - Props.overheatThreshold) * Props.damagePerDegree;
                if (damage > 0f)
                {
                    parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, damage));
                }
            }
            if (temperature >= Props.ignitionThreshold && Rand.Chance(Props.igniteChancePerSecond))
            {
                FireUtility.TryAttachFire(parent, 0.4f, null);
            }
        }

        /// <summary>统计同一个房间里有多少朵炉心蕙。</summary>
        private int CountFlowersInRoom(Room room)
        {
            int count = 0;
            Map map = parent.Map;
            foreach (IntVec3 cell in room.Cells)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                {
                    if (thing.def == parent.def)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>与另一朵炉心蕙贴在一起（八邻接）就不工作——留一格既是美观也是防火。</summary>
        private bool IsCrowded()
        {
            Map map = parent.Map;
            IntVec3 pos = parent.Position;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    IntVec3 cell = pos + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                    {
                        if (thing.def == parent.def)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private CompManaStorage FindPool()
        {
            Map map = parent.Map;
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

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            if (BlockReason != null)
            {
                return BlockReason;
            }
            return "Botania.FurnaceStatus".Translate(
                RoomTemperature.ToString("0"),
                EffectiveArea.ToString("0.0"),
                CurrentOutput.ToString("0.0"));
        }
    }

    /// <summary>放置时拒绝与另一朵炉心蕙八邻接。</summary>
    public class PlaceWorker_FurnaceSpacing : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    IntVec3 cell = loc + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    foreach (Thing present in map.thingGrid.ThingsListAt(cell))
                    {
                        if (present == thingToIgnore)
                        {
                            continue;
                        }
                        if (present.def == checkingDef)
                        {
                            return "Botania.FurnaceTooClose".Translate();
                        }
                    }
                }
            }
            return true;
        }
    }
}
