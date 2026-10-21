using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 被动产能花：太阳花（白天）与夜颠茄（夜间）。
    ///
    /// 与火红莲的区别：不烧任何燃料，纯粹按时间产魔；代价是有寿命，
    /// 到期自己枯萎消失（设定上"只能撑一季"）。
    ///
    /// 判定白天用的是地图当前天空光强 map.skyManager.CurSkyGlow
    /// （0 = 全黑，1 = 正午），比算小时更贴合实际照明，
    /// 日食、火山灰这类异常天气也会自动跟着变暗。
    /// </summary>
    public class CompProperties_ManaPassiveFlower : CompProperties
    {
        /// <summary>每秒产出的魔力。</summary>
        public float manaPerSecond = 8f;

        /// <summary>搜索魔力池的半径（格）。</summary>
        public float radius = 6f;

        /// <summary>只在白天工作。</summary>
        public bool dayOnly = true;

        /// <summary>只在夜间工作。</summary>
        public bool nightOnly = false;

        /// <summary>存活天数，到点枯萎；小于等于 0 表示不枯萎。</summary>
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
            if (manaPerSecond <= 0f)
            {
                yield return "manaPerSecond must be greater than 0.";
            }
            if (radius < 0f)
            {
                yield return "radius must not be negative.";
            }
            if (dayOnly && nightOnly)
            {
                yield return "dayOnly and nightOnly cannot both be true.";
            }
            if (parentDef != null && !typeof(ThingWithComps).IsAssignableFrom(parentDef.thingClass))
            {
                yield return "parentDef.thingClass must derive from ThingWithComps.";
            }
        }
    }

    public class CompManaPassiveFlower : ThingComp
    {
        /// <summary>每满这么多 tick 结算一次（60 tick = 1 秒）。</summary>
        private const int TicksPerSettle = 60;

        /// <summary>一个游戏日的 tick 数。</summary>
        private const int TicksPerDay = 60000;

        /// <summary>天空光强高于这个值算白天。</summary>
        private const float DayGlowThreshold = 0.5f;

        private int ticksAccum;

        /// <summary>已经活了多久；随存档保存。</summary>
        private int ageTicks;

        public CompProperties_ManaPassiveFlower Props => (CompProperties_ManaPassiveFlower)props;

        /// <summary>距离枯萎还剩多少天；不枯萎时返回 -1。</summary>
        public float DaysLeft
        {
            get
            {
                if (Props.witherDays <= 0f)
                {
                    return -1f;
                }
                int total = (int)(Props.witherDays * TicksPerDay);
                int left = total - ageTicks;
                if (left <= 0)
                {
                    return 0f;
                }
                return left / (float)TicksPerDay;
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
            if (Props.witherDays > 0f && ageTicks >= Props.witherDays * TicksPerDay)
            {
                Wither();
                return;
            }

            ticksAccum++;
            if (ticksAccum < TicksPerSettle)
            {
                return;
            }
            ticksAccum = 0;
            Settle(TicksPerSettle);
        }

        /// <summary>结算这一次的魔力产出。</summary>
        private void Settle(int ticks)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            if (!IsActiveTime(map))
            {
                return;
            }

            float seconds = ticks / (float)GenTicks.TicksPerRealSecond;
            float mana = Props.manaPerSecond * seconds;
            if (mana <= 0f)
            {
                return;
            }

            CompManaStorage pool = FindPool();
            if (pool == null)
            {
                // 附近没有池子就什么都不会发生：不烧燃料的花没有损耗，
                // 只是空转而已，玩家把池子放过来就会自动开始工作。
                return;
            }
            pool.AddMana(mana);
        }

        /// <summary>当前时段是否该工作。</summary>
        private bool IsActiveTime(Map map)
        {
            if (!Props.dayOnly && !Props.nightOnly)
            {
                return true;
            }
            bool isDay = map.skyManager != null && map.skyManager.CurSkyGlow > DayGlowThreshold;
            if (Props.dayOnly)
            {
                return isDay;
            }
            return !isDay;
        }

        /// <summary>找出半径内最近且有剩余容量的魔力池。</summary>
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

        /// <summary>枯萎：静默消失，并在附近留一条提示（只对玩家可见的地方）。</summary>
        private void Wither()
        {
            if (parent.Destroyed)
            {
                return;
            }
            if (parent.Faction == Faction.OfPlayerSilentFail)
            {
                Messages.Message("Botania.FlowerWithered".Translate(parent.def.label),
                    parent, MessageTypeDefOf.SilentInput, false);
            }
            parent.Destroy(DestroyMode.Vanish);
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }

            string state;
            if (FindPool() == null)
            {
                state = "Botania.PassiveFlowerNoPool".Translate();
            }
            else if (!IsActiveTime(parent.Map))
            {
                state = Props.dayOnly
                    ? "Botania.PassiveFlowerWaitingDay".Translate()
                    : "Botania.PassiveFlowerWaitingNight".Translate();
            }
            else
            {
                state = "Botania.PassiveFlowerWorking".Translate();
            }

            float daysLeft = DaysLeft;
            if (daysLeft >= 0f)
            {
                state += "\n" + "Botania.PassiveFlowerWithers".Translate(daysLeft.ToString("F1"));
            }
            return state;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ageTicks, "ageTicks", 0);
            Scribe_Values.Look(ref ticksAccum, "ticksAccum", 0);
        }
    }
}
