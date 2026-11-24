using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 示例效果 1：成长涌动（growth surge）。
    ///
    /// 数据参数（Defs/EffectDefs/EffectDefs_Botania.xml）：
    ///   · <c>amount</c> 每次结算给范围内**每株**植物增加的成长度（0.05 = 5%）；
    ///   · <c>radius</c> 作用半径（格）。
    /// 已长到 100% 的植物跳过；每改一株都手动 MapMeshDirty：
    /// 这版引擎改 Growth 不会自动重刷网格，不 dirty 会一直停在幼苗贴图（见 AGENTS.md / 贴图提示）。
    /// </summary>
    public class ManaEffect_GrowthBoost : BotaniaEffectWorker
    {
        public override void Apply(BotaniaEffectDef def, Thing parent, Map map, int ticks)
        {
            if (parent == null || map == null || def == null)
            {
                return;
            }
            float radius = RadiusOf(def);
            if (radius <= 0f)
            {
                return;
            }
            float amount = def.amount;
            if (amount <= 0f)
            {
                return;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                parent.Position, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.Destroyed)
                {
                    continue;
                }
                if (plant.Growth >= 1f)
                {
                    continue;
                }
                plant.Growth = Math.Min(1f, plant.Growth + amount);
                // 成长度改了不会自动重画：成熟度 100% 还是幼苗贴图多半就是漏了这一步。
                map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            }
        }
    }

    /// <summary>
    /// 示例效果 2：净化涌动（purifying pulse）。
    ///
    /// 数据参数：
    ///   · <c>radius</c> 作用半径（格）；范围内所有 <see cref="Filth"/> 直接消失。
    /// 没有副作用：Filth 只是地图上的污秽 Thing，Destroy(Vanish) 不触发任何伤害/爆炸。
    /// 注意先把 Thing 列表物化再删：边遍历 GenRadial 边 Destroy 会改动当前格的 Thing 列表而漏删。
    /// </summary>
    public class ManaEffect_CleanFilth : BotaniaEffectWorker
    {
        public override void Apply(BotaniaEffectDef def, Thing parent, Map map, int ticks)
        {
            if (parent == null || map == null)
            {
                return;
            }
            float radius = RadiusOf(def);
            if (radius <= 0f)
            {
                return;
            }
            List<Thing> things = new List<Thing>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                parent.Position, map, radius, true))
            {
                things.Add(thing);
            }
            for (int i = 0; i < things.Count; i++)
            {
                Filth filth = things[i] as Filth;
                if (filth == null || filth.Destroyed)
                {
                    continue;
                }
                filth.Destroy(DestroyMode.Vanish);
            }
        }
    }

    /// <summary>
    /// 示例效果 3：采收涌动（harvesting pulse）。
    ///
    /// 数据参数：
    ///   · <c>radius</c> 作用半径（格）；
    ///   · <c>intValue</c> 每次结算最多收获几株（0 或负数 = 不限）。
    ///
    /// 判定与原版 <c>WorkGiver_GrowerHarvest</c> 对齐：LifeStage == Mature、HarvestableNow、
    /// CanYieldNow、autoHarvestable；另外尊重「禁止修剪」开关。
    /// **只处理作物，不处理树木**：原版收获/砍树流程对树要记录历史事件、生成树桩，
    /// 需要一个真实的 Pawn actor；传 null 会 NRE。作物走 <see cref="Plant.PlantCollected"/> 是安全的
    /// （收获模式用 Chop 与玩家收获一致：作物销毁 / 灌木回退生长度并自动刷网格）。
    /// 产量结算为 0 的那一株直接跳过、不摧毁，留给下一次结算再掷。
    /// </summary>
    public class ManaEffect_AutoHarvest : BotaniaEffectWorker
    {
        public override void Apply(BotaniaEffectDef def, Thing parent, Map map, int ticks)
        {
            if (parent == null || map == null || def == null)
            {
                return;
            }
            float radius = RadiusOf(def);
            if (radius <= 0f)
            {
                return;
            }
            int maxCount = def.intValue > 0 ? def.intValue : int.MaxValue;
            // 先物化：收获会销毁/回退植物，边遍历 GenRadial 边改 Thing 列表会漏处理。
            List<Plant> plants = new List<Plant>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                parent.Position, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant != null && !plant.Destroyed)
                {
                    plants.Add(plant);
                }
            }
            int harvested = 0;
            for (int i = 0; i < plants.Count && harvested < maxCount; i++)
            {
                Plant plant = plants[i];
                if (!CanMagicallyHarvest(plant))
                {
                    continue;
                }
                int harvestCount = plant.YieldNow();
                if (harvestCount <= 0)
                {
                    continue;
                }
                ThingDef productDef = plant.def.plant.harvestedThingDef;
                if (productDef != null)
                {
                    Thing product = ThingMaker.MakeThing(productDef);
                    product.stackCount = harvestCount;
                    GenPlace.TryPlaceThing(product, parent.Position, map, ThingPlaceMode.Near);
                }
                // 与玩家收获相同的收尾：作物销毁（harvestAfterGrowth>0 的灌木则回退生长度）。
                plant.PlantCollected(null, PlantDestructionMode.Chop);
                harvested++;
            }
        }

        /// <summary>照抄原版自动收获工作类型的前置判定；树木与「禁止修剪」的植物一律跳过。</summary>
        private static bool CanMagicallyHarvest(Plant plant)
        {
            if (plant == null || plant.Destroyed)
            {
                return false;
            }
            if (plant.def.plant.IsTree)
            {
                return false;
            }
            if (!plant.def.plant.autoHarvestable)
            {
                return false;
            }
            if (plant.LifeStage != PlantLifeStage.Mature)
            {
                return false;
            }
            if (!plant.HarvestableNow || !plant.CanYieldNow())
            {
                return false;
            }
            CompPlantPreventCutting preventCutting = plant.TryGetComp<CompPlantPreventCutting>();
            if (preventCutting != null && preventCutting.PreventCutting)
            {
                return false;
            }
            return true;
        }
    }
}
