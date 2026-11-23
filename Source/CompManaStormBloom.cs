using System;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 电暴花：监听电网短路（Zzztt），把电池爆炸释放的能量按比例转成魔力存进自己的小缓存。
    /// 缓存刻意做得小——它是「捡漏」，不是稳定电源。
    ///
    /// 框架迁移（0.8.4 S1.5）：找池子 / 缓冲 / 结算节流 / 存档 / 提示文案交给 <see cref="CompManaProducer"/>，
    /// 本文件只保留「瓦日能量 → 魔力」的换算与短路吸收半径。
    /// </summary>
    public class CompProperties_ManaStormBloom : CompProperties_ManaProducer
    {
        /// <summary>花自己的缓存上限（魔力）。</summary>
        public float bufferCapacity = 2400f;

        /// <summary>1 瓦日电池能量换多少魔力。</summary>
        public float manaPerWattDay = 200f;

        public CompProperties_ManaStormBloom()
        {
            compClass = typeof(CompManaStormBloom);

            // 基类配置：只喂魔力池、每秒结算一次、旧存档里叫 manaBuffer
            radius = 6f;
            settleIntervalTicks = 60;
            feedRelays = false;
            saveKeyPrefix = "botaniaStormBloom";
            legacyBufferKey = "manaBuffer";
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (bufferCapacity <= 0f)
            {
                yield return "bufferCapacity must be greater than 0.";
            }
            if (manaPerWattDay <= 0f)
            {
                yield return "manaPerWattDay must be greater than 0.";
            }
        }
    }

    public class CompManaStormBloom : CompManaProducer
    {
        /// <summary>
        /// 短路发生时，这个半径内的花才会分到能量。
        /// 改这里必须同步改 Def 上的 <c>CompProperties_RadiusRing</c>（放置预览与选中环靠它画）。
        /// </summary>
        public const float AbsorbRadius = 15f;

        public CompProperties_ManaStormBloom Props => (CompProperties_ManaStormBloom)props;

        /// <summary>吃掉若干瓦日的能量，返回实际吃下的瓦日数。</summary>
        public float AbsorbEnergy(float wattDays)
        {
            if (wattDays <= 0f || FreeBufferSpace <= 0f)
            {
                return 0f;
            }
            float usedMana = base.AbsorbIntoBuffer(wattDays * Props.manaPerWattDay);
            return usedMana / Props.manaPerWattDay;
        }

        /// <summary>电暴花不自己产能，只靠吸收短路能量进缓存。</summary>
        protected override bool WantsProduce()
        {
            return false;
        }

        /// <summary>提示文案沿用旧版：缓存 / 上限 / 吸收半径。</summary>
        protected override string ExtraInspectString()
        {
            return "Botania.StormBloomStored".Translate(
                Buffer.ToString("0"),
                Props.bufferCapacity.ToString("0"),
                AbsorbRadius.ToString("0"));
        }
    }
}
