using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 炉心蕙：只在封闭、有屋顶的房间里工作，室温越高、房间越大产得越多。
    /// 产量 ∝ (T − 20) × √A，A 是这朵花分到的有效面积（房间面积 ÷ 房间里的花数，上限 150）。
    /// 维持成本由游戏自身的温度模拟承担：房间越细长、墙越外露，掉温越快，玩家就得烧更多热。
    ///
    /// 框架迁移（0.8.4 S3-b）：找魔力池 / 结算节流 / 存档键交给 <see cref="CompManaProducer"/>；
    /// 本文件只保留「房间面积 → 有效面积 → 产量」的换算，以及过温掉血 / 自燃这两笔账。
    /// 过温与自燃的结算时机与旧版一致：仍是**每次结算（60 tick = 1 秒）一次**，
    /// 挂在产魔之后的 <see cref="OnManaAccepted"/> / <see cref="OnNoTarget"/> 上
    /// （旧版次序也是「先送池、再 Overheat」）。刻意不搬进每 tick 的 ExtraTick——
    /// 那里的伤害/概率没地方除 60，直接搬会让同一份「每秒」数值变成 60 倍。
    /// </summary>
    public class CompProperties_ManaFurnaceOrchid : CompProperties_ManaProducer
    {
        /// <summary>产量系数 k：每秒魔力 = k × (T − 20) × √A。0.20 时 12 格房在 150°C 正好摸到上限。</summary>
        public float manaScale = 0.2f;

        /// <summary>每朵花至少要分到这么多格有效面积才开始工作（花占的那格不计入）。</summary>
        public float minAreaPerFlower = 11f;

        /// <summary>房间面积软上限：超过部分不计入 A。</summary>
        public float maxRoomArea = 150f;

        /// <summary>产量硬上限（0.9.1：90 → 60，约 2 座火红莲）。</summary>
        public float maxManaPerSecond = 60f;

        /// <summary>
        /// 每个燃料单位能换多少魔力（0.9.1 起炉心蕙必须烧燃料）。
        /// 比火红莲的 3000 低 1/3，换来「占地 1 格 + 上限 2 倍」。
        /// </summary>
        public float manaPerFuel = 2000f;

        /// <summary>产魔的起始温度（°C），低于它不产魔（0.9.1：20 → 40）。</summary>
        public float baseTemperature = 40f;

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

            // 基类配置：每秒结算一次、无缓冲、只喂魔力池（均与旧版一致）。
            // 送魔半径沿用旧 poolRadius = 6（XML 里旧的 <poolRadius> 需同步改成 <radius>）。
            radius = 6f;
            settleIntervalTicks = 60;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaManaFurnaceOrchid";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
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
            if (manaPerFuel <= 0f)
            {
                yield return "manaPerFuel must be greater than 0.";
            }
        }
    }

    public class CompManaFurnaceOrchid : CompManaProducer
    {
        public CompProperties_ManaFurnaceOrchid Props => (CompProperties_ManaFurnaceOrchid)props;

        private CompRefuelable Refuelable => parent.GetComp<CompRefuelable>();

        /// <summary>这朵花当前分到的有效面积（供 UI 显示）。</summary>
        public float EffectiveArea { get; private set; }

        /// <summary>房间温度（供 UI 显示）。</summary>
        public float RoomTemperature { get; private set; }

        /// <summary>当前产量（按一次结算计；默认结算间隔 60 tick = 1 秒，所以等于每秒产量）。</summary>
        public float CurrentOutput { get; private set; }

        /// <summary>不能工作的原因，null 表示正常。</summary>
        public string BlockReason { get; private set; }

        /// <summary>
        /// 旧版 Settle() 里「先判断房间/拥挤/面积/温度」的那一半。
        /// 基类每结算一次调用一次，正好对应旧版每秒一次的房间检查与缓存刷新。
        /// </summary>
        protected override bool WantsProduce()
        {
            BlockReason = null;
            CurrentOutput = 0f;

            Map map = parent.Map;
            Room room = parent.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || !parent.Position.Roofed(map))
            {
                BlockReason = "Botania.FurnaceOpenAir".Translate();
                return false;
            }

            float area = Math.Min(room.CellCount, Props.maxRoomArea);
            int flowers = CountFlowersInRoom(room);
            float share = area / Math.Max(1, flowers);
            EffectiveArea = share;
            RoomTemperature = room.Temperature;

            if (IsCrowded())
            {
                BlockReason = "Botania.FurnaceTooClose".Translate();
                return false;
            }
            if (share <= Props.minAreaPerFlower)
            {
                BlockReason = "Botania.FurnaceTooSmall".Translate(Props.minAreaPerFlower.ToString("0"));
                return false;
            }
            if (room.Temperature - Props.baseTemperature <= 0f)
            {
                BlockReason = "Botania.FurnaceTooCold".Translate(Props.baseTemperature.ToString("0"));
                return false;
            }

            // 0.9.1：没有燃料一分不产——堵死「间歇泉白嫖」这条挂机路线
            CompRefuelable refuelable = Refuelable;
            if (refuelable == null)
            {
                BlockReason = "Botania.FurnaceNoFuel".Translate();
                return false;
            }
            if (!refuelable.HasFuel)
            {
                BlockReason = "Botania.FurnaceNoFuel".Translate();
                return false;
            }
            return true;
        }

        /// <summary>旧版 Settle() 里「算产量」的那一半；送池由基类完成。</summary>
        protected override float ProducePerSettle(int ticks)
        {
            // 配置语义是「每秒」，默认 60 tick 结算一次，换算系数正好是 1（改动间隔时按比例缩放）。
            float seconds = ticks / (float)GenTicks.TicksPerRealSecond;
            float mana = Props.manaScale * (RoomTemperature - Props.baseTemperature)
                * (float)Math.Sqrt(EffectiveArea) * seconds;
            float cap = Props.maxManaPerSecond * seconds;
            if (mana > cap)
            {
                mana = cap;
            }
            // 燃料只够换这么多魔力：先按剩余燃料封顶，再交给基类送池
            CompRefuelable refuelable = Refuelable;
            if (refuelable != null)
            {
                float fuelCap = refuelable.Fuel * Props.manaPerFuel;
                if (mana > fuelCap)
                {
                    mana = fuelCap;
                }
            }
            CurrentOutput = mana;
            return mana;
        }

        /// <summary>旧版：魔力送池成功之后结算过温掉血 / 自燃。</summary>
        protected override void OnManaAccepted(float acceptedMana)
        {
            // 只按「真的送进池子」的量烧燃料，没池子就不浪费
            CompRefuelable refuelable = Refuelable;
            if (refuelable != null && Props.manaPerFuel > 0f && acceptedMana > 0f)
            {
                refuelable.ConsumeFuel(acceptedMana / Props.manaPerFuel);
            }
            Overheat();
        }

        /// <summary>旧版：附近没有池子时魔力直接损失，但过温掉血 / 自燃照旧结算。</summary>
        protected override void OnNoTarget()
        {
            Overheat();
        }

        /// <summary>超过阈值掉 HP；逼近沸点有概率自燃。按一次结算（默认 1 秒）记账。</summary>
        private void Overheat()
        {
            float seconds = ProducerProps.settleIntervalTicks / (float)GenTicks.TicksPerRealSecond;
            float temperature = RoomTemperature;
            if (temperature > Props.overheatThreshold)
            {
                float damage = (temperature - Props.overheatThreshold) * Props.damagePerDegree * seconds;
                if (damage > 0f)
                {
                    parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, damage));
                }
            }
            if (temperature >= Props.ignitionThreshold
                && Rand.Chance(Props.igniteChancePerSecond * seconds))
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

            string text = "Botania.FurnaceStatus".Translate(
                RoomTemperature.ToString("0"),
                EffectiveArea.ToString("0.0"),
                CurrentOutput.ToString("0.0"));

            CompRefuelable refuelable = Refuelable;
            if (refuelable != null)
            {
                float fuelMana = refuelable.Fuel * Props.manaPerFuel;
                float secondsLeft = CurrentOutput > 0.01f ? fuelMana / CurrentOutput : 0f;
                text += "\n" + "Botania.FurnaceFuel".Translate(
                    refuelable.Fuel.ToString("0.0"),
                    secondsLeft.ToString("0"));
            }

            // 过热与自燃要让玩家看得见，否则花只会莫名其妙掉血
            if (RoomTemperature > Props.overheatThreshold)
            {
                float damage = (RoomTemperature - Props.overheatThreshold) * Props.damagePerDegree;
                text += "\n" + "Botania.FurnaceOverheating".Translate(damage.ToString("0.0"));
            }
            if (RoomTemperature >= Props.ignitionThreshold)
            {
                text += "\n" + "Botania.FurnaceIgnitionRisk".Translate();
            }
            return text;
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
