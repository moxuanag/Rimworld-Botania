using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 被动产能花（太阳花 / 夜颠茄）：不需要燃料，只在白天或夜间缓慢产出魔力。
    /// 与火红莲的区别是「零输入」——代价是有枯萎周期，到点自己消失。
    /// </summary>
    public class CompProperties_ManaPassiveFlower : CompProperties
    {
        /// <summary>每秒产出的魔力量（一个 RimWorld 秒 = 60 tick）。</summary>
        public float manaPerSecond = 8f;

        /// <summary>搜索魔力池的半径（格）。</summary>
        public float radius = 6f;

        /// <summary>只在白天工作。</summary>
        public bool dayOnly = true;

        /// <summary>只在夜间工作。</summary>
        public bool nightOnly;

        /// <summary>枯萎周期（天）。0 表示永不枯萎。</summary>
        public float witherDays = 15f;

        public CompProperties_ManaPassiveFlower()
        {
            compClass = typeof(CompManaPassiveFlower);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (dayOnly && nightOnly)
            {
                yield return "dayOnly and nightOnly cannot both be true.";
            }
            if (manaPerSecond <= 0f)
            {
                yield return "manaPerSecond must be greater than 0.";
            }
            if (radius < 0f)
            {
                yield return "radius must not be negative.";
            }
            if (witherDays < 0f)
            {
                yield return "witherDays must not be negative.";
            }
        }
    }

    public class CompManaPassiveFlower : ThingComp
    {
        /// <summary>每满这么多 tick 结算一次（60 tick = 1 秒）。</summary>
        private const int TicksPerSettle = 60;

        private int ticksAccum;

        /// <summary>已经存活的 tick 数，用于枯萎判定与存档。</summary>
        private long ageTicks;

        public CompProperties_ManaPassiveFlower Props => (CompProperties_ManaPassiveFlower)props;

        /// <summary>当前时辰是否属于它的工作时段。日出 25%、日落 75%。</summary>
        private bool IsActiveNow
        {
            get
            {
                if (parent.Map == null)
                {
                    return false;
                }
                float dayPercent = GenLocalDate.DayPercent(parent.Map);
                bool isDay = dayPercent > 0.25f && dayPercent < 0.75f;
                if (Props.dayOnly)
                {
                    return isDay;
                }
                if (Props.nightOnly)
                {
                    return !isDay;
                }
                return true;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }

            ageTicks++;
            if (Props.witherDays > 0f && ageTicks >= (long)(Props.witherDays * GenDate.TicksPerDay))
            {
                parent.Destroy(DestroyMode.Vanish);
                return;
            }

            ticksAccum++;
            if (ticksAccum < TicksPerSettle)
            {
                return;
            }
            ticksAccum = 0;

            if (!IsActiveNow)
            {
                return;
            }

            CompManaStorage pool = FindPool();
            if (pool == null)
            {
                return;
            }
            pool.AddMana(Props.manaPerSecond);
        }

        /// <summary>找出半径内最近且还有容量的魔力池。</summary>
        private CompManaStorage FindPool()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
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

            string state;
            if (!IsActiveNow)
            {
                state = "Botania.PassiveDormant".Translate();
            }
            else if (FindPool() == null)
            {
                state = "Botania.FlowerNoPool".Translate();
            }
            else
            {
                state = "Botania.FlowerWorking".Translate();
            }

            if (Props.witherDays > 0f)
            {
                float daysLeft = Props.witherDays - ageTicks / (float)GenDate.TicksPerDay;
                if (daysLeft < 0f)
                {
                    daysLeft = 0f;
                }
                state = state + "\n" + "Botania.PassiveLifespan".Translate(daysLeft.ToString("0.0"));
            }
            return state;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ageTicks, "ageTicks", 0L);
        }
    }
}
