using System;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 电暴花：监听电网短路（Zzztt），把电池爆炸释放的能量按比例转成魔力存进自己的小缓存。
    /// 缓存刻意做得小——它是「捡漏」，不是稳定电源。
    /// </summary>
    public class CompProperties_ManaStormBloom : CompProperties
    {
        /// <summary>花自己的缓存上限（魔力）。</summary>
        public float bufferCapacity = 2400f;

        /// <summary>1 瓦日电池能量换多少魔力。</summary>
        public float manaPerWattDay = 200f;

        /// <summary>向魔力池输送的半径。</summary>
        public float poolRadius = 6f;

        public CompProperties_ManaStormBloom()
        {
            compClass = typeof(CompManaStormBloom);
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

    public class CompManaStormBloom : ThingComp
    {
        private const int TicksPerFlush = 60;

        private float buffer;
        private int ticksAccum;

        public CompProperties_ManaStormBloom Props => (CompProperties_ManaStormBloom)props;

        public float Buffer => buffer;

        public float FreeSpace => Props.bufferCapacity - buffer;

        /// <summary>吃掉若干瓦日的能量，返回实际吃下的瓦日数。</summary>
        public float AbsorbEnergy(float wattDays)
        {
            if (wattDays <= 0f || FreeSpace <= 0f)
            {
                return 0f;
            }
            float maxWattDays = FreeSpace / Props.manaPerWattDay;
            float used = Math.Min(wattDays, maxWattDays);
            buffer += used * Props.manaPerWattDay;
            return used;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < TicksPerFlush)
            {
                return;
            }
            ticksAccum = 0;
            Flush();
        }

        /// <summary>把缓存里的魔力推给最近的魔力池。</summary>
        private void Flush()
        {
            if (buffer <= 0f)
            {
                return;
            }
            CompManaStorage pool = FindPool();
            if (pool == null)
            {
                return;
            }
            float accepted = pool.AddMana(buffer);
            if (accepted > 0f)
            {
                buffer -= accepted;
                if (buffer < 0.001f)
                {
                    buffer = 0f;
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

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            return "Botania.StormBloomStored".Translate(buffer.ToString("0"), Props.bufferCapacity.ToString("0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref buffer, "manaBuffer", 0f);
        }
    }
}
