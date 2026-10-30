using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力通量场：把附近魔力池中的魔力转化为电力（对应 Botania 的 Mana Fluxfield）。
    /// 每 60 tick 结算一次：池里有足够魔力就满功率输出，没有就完全停机。
    /// </summary>
    public class CompProperties_ManaFluxfield : CompProperties_Power
    {
        /// <summary>满功率运行时每秒消耗的魔力。</summary>
        public float manaPerSecond = 5f;

        /// <summary>搜索魔力池的半径（格）。</summary>
        public float searchRadius = 6f;

        public CompProperties_ManaFluxfield()
        {
            compClass = typeof(CompManaFluxfield);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerSecond <= 0f)
            {
                yield return "manaPerSecond must be greater than 0.";
            }
            if (searchRadius < 0f)
            {
                yield return "searchRadius must not be negative.";
            }
            // PowerConsumption 对应 XML 里的 basePowerConsumption（原字段是私有的）。
            // 发电设备的消耗应为负数。
            if (PowerConsumption >= 0f)
            {
                yield return "basePowerConsumption must be negative (this comp produces power).";
            }
        }
    }

    public class CompManaFluxfield : CompPowerTrader
    {
        private const int TicksPerSettle = 60;

        private int ticksAccum;
        private bool hasMana;

        public CompProperties_ManaFluxfield ManaProps => (CompProperties_ManaFluxfield)props;

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
            // 被玩家关掉（或者断电）时：不扣魔力、输出归零
            CompFlickable flickable = parent.TryGetComp<CompFlickable>();
            if (flickable != null && !flickable.SwitchIsOn)
            {
                hasMana = false;
                PowerOutput = 0f;
                return;
            }

            float needed = ManaProps.manaPerSecond;
            // 就近取魔力：魔力池与魔力中继塔都算（中继塔让供给范围向外延伸）
            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, parent.Map, ManaProps.searchRadius);
            // 凑不够就别扣：以前是「有多少扣多少」，池子会被慢慢抽干却发不出电
            float drained = 0f;
            if (source != null && source.ManaAmount + 0.01f >= needed)
            {
                drained = source.DrainMana(needed);
            }
            // 允许 1% 误差，避免浮点抖动导致频繁启停
            hasMana = needed <= 0f || drained >= needed * 0.99f;
            PowerOutput = hasMana ? -ManaProps.PowerConsumption : 0f;
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            string ours = hasMana
                ? "Botania.FluxfieldWorking".Translate()
                : "Botania.FluxfieldNoMana".Translate();
            if (baseString.NullOrEmpty())
            {
                return ours;
            }
            return baseString + "\n" + ours;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasMana, "hasMana", false);
        }
    }
}
