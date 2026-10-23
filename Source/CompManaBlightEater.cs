using System;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 噬草花：专吃枯萎病（Blight）作物。每过一段时间找一株范围内的病株吃掉，
    /// 把它身上的魔力榨出来送进附近的池子——顺带也算帮玩家清理病害。
    /// </summary>
    public class CompProperties_ManaBlightEater : CompProperties
    {
        /// <summary>找病株的半径。</summary>
        public float radius = 6f;

        /// <summary>吃掉一株产多少魔力。</summary>
        public float manaPerPlant = 1500f;

        /// <summary>两顿之间隔多少 tick（180 = 3 秒）。</summary>
        public int ticksBetweenMeals = 180;

        /// <summary>连续吃这么多株之后要停下来消化一次。</summary>
        public int mealsPerBatch = 10;

        /// <summary>消化时间（1800 tick = 30 秒）。</summary>
        public int restTicks = 1800;

        /// <summary>向魔力池输送的半径。</summary>
        public float poolRadius = 6f;

        public CompProperties_ManaBlightEater()
        {
            compClass = typeof(CompManaBlightEater);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerPlant <= 0f)
            {
                yield return "manaPerPlant must be greater than 0.";
            }
            if (ticksBetweenMeals <= 0)
            {
                yield return "ticksBetweenMeals must be greater than 0.";
            }
        }
    }

    public class CompManaBlightEater : ThingComp
    {
        private int ticksToNextMeal;

        /// <summary>这一批已经吃了几株，吃满 mealsPerBatch 就要消化一次。</summary>
        private int mealsThisBatch;

        /// <summary>累计吃掉多少株，供信息栏显示。</summary>
        public int Eaten;

        public CompProperties_ManaBlightEater Props => (CompProperties_ManaBlightEater)props;

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            ticksToNextMeal--;
            if (ticksToNextMeal > 0)
            {
                return;
            }
            ticksToNextMeal = Props.ticksBetweenMeals;
            EatOne();
        }

        private void EatOne()
        {
            Plant target = FindBlightedPlant();
            if (target == null)
            {
                return;
            }

            Map map = parent.Map;
            IntVec3 position = target.Position;
            target.Destroy(DestroyMode.Vanish);
            Eaten++;

            // 连吃 mealsPerBatch 株就要停下来消化一次
            mealsThisBatch++;
            if (Props.mealsPerBatch > 0 && mealsThisBatch >= Props.mealsPerBatch)
            {
                mealsThisBatch = 0;
                ticksToNextMeal = Props.restTicks;
            }

            CompManaStorage pool = FindPool();
            if (pool != null)
            {
                pool.AddMana(Props.manaPerPlant);
            }
            if (Prefs.DevMode)
            {
                MotesUtility.ThrowMote(map, position);
            }
        }

        /// <summary>半径内最近的病株。</summary>
        private Plant FindBlightedPlant()
        {
            Map map = parent.Map;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
            {
                Plant plant = thing as Plant;
                if (plant != null && plant.Blighted && !plant.Destroyed)
                {
                    return plant;
                }
            }
            return null;
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
            int nearby = 0;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.radius, true))
            {
                Plant plant = thing as Plant;
                if (plant != null && plant.Blighted)
                {
                    nearby++;
                }
            }
            if (nearby <= 0)
            {
                return "Botania.BlightEaterIdle".Translate(Props.radius.ToString("0"));
            }
            return "Botania.BlightEaterWorking".Translate(nearby, Eaten);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksToNextMeal, "ticksToNextMeal", 0);
            Scribe_Values.Look(ref Eaten, "eaten", 0);
        }
    }

    /// <summary>吃病株时的一点视觉反馈，失败也不影响逻辑。</summary>
    public static class MotesUtility
    {
        public static void ThrowMote(Map map, IntVec3 cell)
        {
            try
            {
                FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 0.6f);
            }
            catch (Exception)
            {
                // 纯演出，失败就算了
            }
        }
    }
}
