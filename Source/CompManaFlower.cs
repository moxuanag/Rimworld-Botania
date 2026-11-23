using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 功能花（火红莲）：把燃料缓慢转化为魔力，并推送到附近的魔力池。
    /// 燃料本身由原版 CompRefuelable 管理（容量、加料清单、加料小部件都复用它）。
    ///
    /// 框架迁移（0.8.4 S1.5）：找池子 / 结算节流 / 提示文案交给 <see cref="CompManaProducer"/>；
    /// 本文件只保留「烧燃料 → 产魔力」的换算，并且**按池子实际接收量扣燃料**
    /// （旧行为：池子只吃得下多少就只扣多少燃料）。
    /// </summary>
    public class CompProperties_ManaFlower : CompProperties_ManaProducer
    {
        /// <summary>每消耗 1 单位燃料产出的魔力。</summary>
        public float manaPerFuel = 3000f;

        /// <summary>工作时每秒消耗的燃料量。</summary>
        public float fuelPerSecond = 0.02f;

        /// <summary>附近没有魔力池时是否仍然烧燃料。默认 false（空转不烧料）。</summary>
        public bool burnFuelWithoutPool = false;

        public CompProperties_ManaFlower()
        {
            compClass = typeof(CompManaFlower);

            // 基类配置：每秒结算、无缓冲、只喂魔力池
            radius = 6f;
            settleIntervalTicks = 60;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaManaFlower";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerFuel <= 0f)
            {
                yield return "manaPerFuel must be greater than 0.";
            }
            if (fuelPerSecond < 0f)
            {
                yield return "fuelPerSecond must not be negative.";
            }
            if (parentDef != null && !typeof(ThingWithComps).IsAssignableFrom(parentDef.thingClass))
            {
                yield return "parentDef.thingClass must derive from ThingWithComps.";
            }
        }
    }

    public class CompManaFlower : CompManaProducer
    {
        public CompProperties_ManaFlower Props => (CompProperties_ManaFlower)props;

        private CompRefuelable Refuelable => parent.GetComp<CompRefuelable>();

        /// <summary>没有燃料就停工。</summary>
        protected override bool WantsProduce()
        {
            CompRefuelable refuelable = Refuelable;
            return refuelable != null && refuelable.HasFuel;
        }

        /// <summary>本次结算「愿意」产出的魔力（按可烧燃料折算）。</summary>
        protected override float ProducePerSettle(int ticks)
        {
            CompRefuelable refuelable = Refuelable;
            if (refuelable == null)
            {
                return 0f;
            }
            float seconds = ticks / (float)GenTicks.TicksPerRealSecond;
            float fuelBudget = Props.fuelPerSecond * seconds;
            if (fuelBudget <= 0f)
            {
                return 0f;
            }
            float availableFuel = Math.Min(fuelBudget, refuelable.Fuel);
            if (availableFuel <= 0f)
            {
                return 0f;
            }
            return availableFuel * Props.manaPerFuel;
        }

        /// <summary>池子只接收了一部分时，只扣对应的燃料（旧行为）。</summary>
        protected override void OnManaAccepted(float acceptedMana)
        {
            CompRefuelable refuelable = Refuelable;
            if (refuelable == null || Props.manaPerFuel <= 0f)
            {
                return;
            }
            refuelable.ConsumeFuel(acceptedMana / Props.manaPerFuel);
        }

        /// <summary>没有池子时：允许空烧就照烧燃料，否则一点不烧。</summary>
        protected override void OnNoTarget()
        {
            if (!Props.burnFuelWithoutPool)
            {
                return;
            }
            CompRefuelable refuelable = Refuelable;
            if (refuelable == null || !refuelable.HasFuel)
            {
                return;
            }
            float seconds = ProducerProps.settleIntervalTicks / (float)GenTicks.TicksPerRealSecond;
            refuelable.ConsumeFuel(Math.Min(Props.fuelPerSecond * seconds, refuelable.Fuel));
        }

        /// <summary>提示文案沿用旧版：无燃料 / 无池子（分空烧与否）/ 工作中。</summary>
        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            CompRefuelable refuelable = Refuelable;
            if (refuelable != null && !refuelable.HasFuel)
            {
                return "Botania.FlowerNoFuel".Translate();
            }
            if (!HasTarget())
            {
                return Props.burnFuelWithoutPool
                    ? "Botania.FlowerWorkingNoPool".Translate()
                    : "Botania.FlowerNoPool".Translate();
            }
            return "Botania.FlowerWorking".Translate();
        }
    }
}
