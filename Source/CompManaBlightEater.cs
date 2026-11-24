using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 噬草花：专吃枯萎病（Blight）作物。每过一段时间找一株范围内的病株吃掉，
    /// 把它身上的魔力榨出来送进附近的池子——顺带也算帮玩家清理病害。
    ///
    /// 框架迁移（0.8.4 S2）：找魔力池 / 结算节流 / 存档 / 通用提示交给 <see cref="CompManaProducer"/>，
    /// 本文件只保留「找病株 → 吃掉 → 产多少魔力」与「连吃 N 株后消化」两段专属逻辑。
    /// 只有一条节流：基类的结算间隔（= 旧 ticksBetweenMeals）。
    /// </summary>
    public class CompProperties_ManaBlightEater : CompProperties_ManaProducer
    {
        /// <summary>
        /// 找病株的半径（格）。这是旧版的 <c>radius</c>，与魔力池半径分开配置
        /// （旧版 XML 里叫 <c>poolRadius</c> 的那一项现在用基类的 <c>radius</c>）。
        /// </summary>
        public float plantRadius = 6f;

        /// <summary>吃掉一株产多少魔力。</summary>
        public float manaPerPlant = 1500f;

        /// <summary>连续吃这么多株之后要停下来消化一次。</summary>
        public int mealsPerBatch = 10;

        /// <summary>消化时间（1800 tick = 30 秒）。</summary>
        public int restTicks = 1800;

        public CompProperties_ManaBlightEater()
        {
            compClass = typeof(CompManaBlightEater);

            // 基类配置：
            //   radius = 6            —— 送魔力池的半径（旧版 poolRadius，旧值 6）
            //   settleIntervalTicks   —— 两顿之间隔 180 tick = 3 秒（旧版 ticksBetweenMeals）
            //   bufferCapacity = 0    —— 不缓冲：吃一株直接送池，送不进就损失（旧版行为）
            //   feedRelays = false    —— 只喂魔力池，不喂中继塔（旧版行为）
            radius = 6f;
            settleIntervalTicks = 180;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaBlightEater";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            // 旧版对 ticksBetweenMeals 的 >0 校验已由基类的 settleIntervalTicks 校验接管。
            if (manaPerPlant <= 0f)
            {
                yield return "manaPerPlant must be greater than 0.";
            }
        }
    }

    public class CompManaBlightEater : CompManaProducer
    {
        /// <summary>这一批已经吃了几株，吃满 mealsPerBatch 就要消化一次。</summary>
        private int mealsThisBatch;

        /// <summary>
        /// 还要消化多少 tick；大于 0 时不吃东西。
        /// 进食间隔本身由基类的结算节流负责，这里只表达「旧版 ticksToNextMeal = restTicks」这一段。
        /// </summary>
        private int restTicksLeft;

        /// <summary>累计吃掉多少株，供信息栏显示。</summary>
        public int Eaten;

        /// <summary>
        /// <see cref="WantsProduce"/> 找到的病株，交给 <see cref="ProducePerSettle"/> 直接吃掉，
        /// 避免同一次结算把半径扫两遍。
        /// </summary>
        private Plant pendingTarget;

        public CompProperties_ManaBlightEater Props => (CompProperties_ManaBlightEater)props;

        /// <summary>消化倒计时；进食节奏由基类结算间隔负责，这里不另起 tick 节流。</summary>
        protected override void ExtraTick()
        {
            if (restTicksLeft > 0)
            {
                restTicksLeft--;
            }
        }

        /// <summary>消化中、或半径内没有病株时不产魔（基类此时只推进到下一次结算，不做任何事）。</summary>
        protected override bool WantsProduce()
        {
            if (restTicksLeft > 0)
            {
                return false;
            }
            pendingTarget = FindBlightedPlant();
            return pendingTarget != null;
        }

        /// <summary>
        /// 吃掉一株病株，返回这一株榨出的魔力。
        /// 旧行为：不管有没有魔力池都先吃（吃满一批就进入消化），没有池子时魔力直接损失。
        /// </summary>
        protected override float ProducePerSettle(int ticks)
        {
            Plant target = pendingTarget;
            pendingTarget = null;
            if (target == null)
            {
                return 0f;
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
                restTicksLeft = Props.restTicks;
            }

            if (Prefs.DevMode)
            {
                MotesUtility.ThrowMote(map, position);
            }
            return Props.manaPerPlant;
        }

        /// <summary>半径内最近的病株。</summary>
        private Plant FindBlightedPlant()
        {
            Map map = parent.Map;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.plantRadius, true))
            {
                Plant plant = thing as Plant;
                if (plant != null && plant.Blighted && !plant.Destroyed)
                {
                    return plant;
                }
            }
            return null;
        }

        /// <summary>
        /// 提示文案沿用旧版：没有病株 → 「半径 {0} 格内没有枯萎作物」；
        /// 有病株 → 「半径内 {0} 株枯萎作物 · 已吃 {1} 株」。与有没有魔力池无关。
        /// </summary>
        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            int nearby = 0;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.plantRadius, true))
            {
                Plant plant = thing as Plant;
                if (plant != null && plant.Blighted)
                {
                    nearby++;
                }
            }
            if (nearby <= 0)
            {
                return "Botania.BlightEaterIdle".Translate(Props.plantRadius.ToString("0"));
            }
            return "Botania.BlightEaterWorking".Translate(nearby, Eaten);
        }

        public override void PostExposeData()
        {
            // 基类负责：botaniaBlightEaterNextSettleTick / botaniaBlightEaterBuffer
            base.PostExposeData();
            Scribe_Values.Look(ref mealsThisBatch, "botaniaBlightEaterMealsThisBatch", 0);
            Scribe_Values.Look(ref Eaten, "botaniaBlightEaterEaten", 0);
            Scribe_Values.Look(ref restTicksLeft, "botaniaBlightEaterRestTicksLeft", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 旧存档键（本引擎把组件字段平铺在 Thing 下，旧版没有前缀）：只读兼容，不回写。
                Scribe_Values.Look(ref mealsThisBatch, "mealsThisBatch", mealsThisBatch);
                Scribe_Values.Look(ref Eaten, "eaten", Eaten);

                // 旧版只有一个统一倒计时 ticksToNextMeal；新框架的 nextSettleTick 是基类私有字段，
                // 无法直接折算，这里把它当成「还要等多久才吃」的消化计时，避免读旧档后立刻吞一株。
                // 误差不超过一个结算间隔（180 tick），新存档起由 nextSettleTick + restTicksLeft 精确续上。
                int legacyTicksToNextMeal = 0;
                Scribe_Values.Look(ref legacyTicksToNextMeal, "ticksToNextMeal", 0);
                if (legacyTicksToNextMeal > 0 && restTicksLeft <= 0)
                {
                    restTicksLeft = legacyTicksToNextMeal;
                }
            }
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
