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
    ///
    /// 框架迁移（0.8.4 S1.5）：找池子 / 结算节流 / 存档交给 <see cref="CompManaProducer"/>，
    /// 本文件只保留「昼夜判定 + 每秒产多少 + 寿命枯萎」。
    /// </summary>
    public class CompProperties_ManaPassiveFlower : CompProperties_ManaProducer
    {
        /// <summary>每秒产出的魔力。</summary>
        public float manaPerSecond = 8f;

        /// <summary>只在白天工作。</summary>
        public bool dayOnly = true;

        /// <summary>只在夜间工作。</summary>
        public bool nightOnly = false;

        /// <summary>存活天数，到点枯萎；小于等于 0 表示不枯萎。</summary>
        public float witherDays = 15f;

        public CompProperties_ManaPassiveFlower()
        {
            compClass = typeof(CompManaPassiveFlower);

            // 基类配置：每秒结算、无缓冲、只喂魔力池、沿用旧提示文案
            radius = 6f;
            settleIntervalTicks = 60;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaPassiveFlower";
            inspectWorkingKey = "Botania.PassiveFlowerWorking";
            inspectNoPoolKey = "Botania.PassiveFlowerNoPool";
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

    public class CompManaPassiveFlower : CompManaProducer
    {
        /// <summary>一个游戏日的 tick 数。</summary>
        private const int TicksPerDay = 60000;

        /// <summary>天空光强高于这个值算白天。</summary>
        private const float DayGlowThreshold = 0.5f;

        /// <summary>已经活了多久；随存档保存（沿用旧键，老档的树龄不会清零）。</summary>
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

        /// <summary>每 tick 记账：年龄 + 枯萎判定。</summary>
        protected override void ExtraTick()
        {
            ageTicks++;
            if (Props.witherDays > 0f && ageTicks >= Props.witherDays * TicksPerDay)
            {
                Wither();
            }
        }

        /// <summary>当前时段是否该工作。</summary>
        protected override bool WantsProduce()
        {
            Map map = parent.Map;
            return map != null && IsActiveTime(map);
        }

        /// <summary>按秒产出折算到本次结算间隔。</summary>
        protected override float ProducePerSettle(int ticks)
        {
            float seconds = ticks / (float)GenTicks.TicksPerRealSecond;
            return Props.manaPerSecond * seconds;
        }

        /// <summary>当前时段是否该工作（供提示文案使用）。</summary>
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

        /// <summary>提示文案保持旧版措辞：无池子 / 等白天 / 等夜晚 / 工作中，再附剩余寿命。</summary>
        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }

            string state;
            if (!HasTarget())
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
        }
    }
}
