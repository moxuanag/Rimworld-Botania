using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 功能花（火红莲）：把燃料缓慢转化为魔力，并推送到附近的魔力池。
    /// 燃料本身由原版 CompRefuelable 管理（容量、加料清单、加料小部件都复用它）。
    /// </summary>
    public class CompProperties_ManaFlower : CompProperties
    {
        /// <summary>每消耗 1 单位燃料产出的魔力。</summary>
        public float manaPerFuel = 3000f;

        /// <summary>工作时每秒消耗的燃料量。</summary>
        public float fuelPerSecond = 0.02f;

        /// <summary>搜索魔力池的半径（格）。</summary>
        public float radius = 6f;

        /// <summary>附近没有魔力池时是否仍然烧燃料。默认 false（空转不烧料）。</summary>
        public bool burnFuelWithoutPool = false;

        public CompProperties_ManaFlower()
        {
            compClass = typeof(CompManaFlower);
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
            if (radius < 0f)
            {
                yield return "radius must not be negative.";
            }
            if (parentDef != null && !typeof(ThingWithComps).IsAssignableFrom(parentDef.thingClass))
            {
                yield return "parentDef.thingClass must derive from ThingWithComps.";
            }
        }
    }

    public class CompManaFlower : ThingComp
    {
        /// <summary>每满这么多 tick 结算一次（60 tick = 1 秒）。</summary>
        private const int TicksPerSettle = 60;

        private int ticksAccum;

        public CompProperties_ManaFlower Props => (CompProperties_ManaFlower)props;

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
            Settle(TicksPerSettle);
        }

        /// <summary>结算 seconds 秒的魔力产出。</summary>
        private void Settle(int ticks)
        {
            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            if (refuelable == null)
            {
                return;
            }
            if (!refuelable.HasFuel)
            {
                return;
            }
            float seconds = ticks / (float)GenTicks.TicksPerRealSecond;
            float fuelBudget = Props.fuelPerSecond * seconds;
            if (fuelBudget <= 0f)
            {
                return;
            }
            float available = Math.Min(fuelBudget, refuelable.Fuel);
            if (available <= 0f)
            {
                return;
            }

            CompManaStorage pool = FindPool();
            if (pool == null)
            {
                // 没有可以接收魔力的魔力池。
                // 默认空转不烧料；若显式允许空烧，则燃料照烧、魔力直接散失。
                if (Props.burnFuelWithoutPool)
                {
                    refuelable.ConsumeFuel(available);
                }
                return;
            }

            float manaProduced = available * Props.manaPerFuel;
            float accepted = pool.AddMana(manaProduced);
            if (accepted <= 0f)
            {
                return;
            }
            refuelable.ConsumeFuel(accepted / Props.manaPerFuel);
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

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            if (refuelable != null && !refuelable.HasFuel)
            {
                return "Botania.FlowerNoFuel".Translate();
            }
            CompManaStorage pool = FindPool();
            if (pool == null)
            {
                return Props.burnFuelWithoutPool
                    ? "Botania.FlowerWorkingNoPool".Translate()
                    : "Botania.FlowerNoPool".Translate();
            }
            return "Botania.FlowerWorking".Translate();
        }
    }
}
