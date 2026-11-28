using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 植物魔法的公共工具集（静态、无状态、可任意复用）。
    ///
    /// 为什么单独抽出来：收获判定 / 播种落位 / 催熟 / 数树 这几件事，
    /// 收获涌动（<see cref="ManaEffect_AutoHarvest"/>）、丰饶之花（<see cref="CompFertilityFlower"/>）
    /// 以及以后任何「让植物长起来 / 收下来 / 种下去」的花或效果都是同一套语义。
    /// 以前每个调用方各抄一遍，行为会慢慢分叉（例如成长度改了忘记 MapMeshDirty，
    /// 结果成熟度 100% 还顶着幼苗贴图），所以统一放这里。
    ///
    /// 本类**不碰魔力**：取魔 / 扣款 / 节流一律走
    /// <see cref="ManaDeviceCore"/>（凑够才扣 <c>TryPay</c>、只查不扣 <c>FindSource</c>）。
    /// 也不碰存档：需要持久化的状态由各调用方自己的 Comp 用 <c>Scribe_*</c> 读写。
    /// </summary>
    public static class PlantMagicUtil
    {
        /// <summary>
        /// 「地面播种」标签。原版作物靠 <c>plant.sowTags</c> 区分能不能种在地里 /
        /// 水培盆里；功能花只在露天地面播种，所以只认这一个标签。
        /// </summary>
        public const string GroundSowTag = "Ground";

        /// <summary>播种时落下的初始成长度（与原版播种一致：0.0001 而不是 0，否则会停在「播种中」贴图）。</summary>
        public const float SownGrowth = 0.0001f;

        /// <summary>
        /// 这株植物能不能被「魔力收获」。
        ///
        /// 判定与原版 <c>WorkGiver_GrowerHarvest</c> 对齐：LifeStage == Mature、HarvestableNow、
        /// CanYieldNow、autoHarvestable；另外尊重「禁止修剪」开关。
        /// **树木一律跳过**：原版砍树流程要记录历史事件、生成树桩，需要一个真实的 Pawn actor
        /// （<see cref="Plant.PlantCollected"/> 里会用到 <c>by</c>），传 null 会 NRE。
        ///
        /// 本方法从 <see cref="ManaEffect_AutoHarvest"/> 原样搬出，行为不变 —— 那里改为调用本方法。
        /// </summary>
        public static bool CanMagicallyHarvest(Plant plant)
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

        /// <summary>
        /// 按「植物技能档位」折算产量：原版产量 × 系数，再四舍五入。
        /// 折算后为 0 表示这一株这次收不出东西，调用方应当**不付款、不摧毁**它。
        /// </summary>
        /// <param name="baseYield">原版产量，通常是 <see cref="Plant.YieldNow"/> 的返回值。</param>
        /// <param name="factor">档位系数（&lt;1 减产、&gt;1 增产）。</param>
        public static int ApplySkillYield(int baseYield, float factor)
        {
            if (baseYield <= 0 || factor <= 0f)
            {
                return 0;
            }
            return GenMath.RoundRandom(baseYield * factor);
        }

        /// <summary>
        /// 玩家可以「种在地里」的作物列表（给选择作物的浮动菜单用）。
        ///
        /// 条件：category == Plant、sowTags 含 "Ground"、可收获、不是树、不是「只能野生」。
        /// 树木单独排除：它们靠 <see cref="CountMatureTrees"/> / 手动种植，不走这套播种。
        /// </summary>
        public static IEnumerable<ThingDef> ValidSowableCrops()
        {
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (def == null || def.category != ThingCategory.Plant)
                {
                    continue;
                }
                PlantProperties plant = def.plant;
                if (plant == null)
                {
                    continue;
                }
                if (!plant.Sowable || !plant.sowTags.Contains(GroundSowTag))
                {
                    continue;
                }
                if (!plant.Harvestable)
                {
                    continue;
                }
                if (plant.IsTree)
                {
                    continue;
                }
                if (plant.mustBeWildToSow)
                {
                    continue;
                }
                yield return def;
            }
        }

        /// <summary>
        /// 在半径内找一个能播种的格子（只查，不改地图），并顺带指出这格上挡路的野生植物。
        ///
        /// 调用方**每轮播种都要重新调一次**，不要缓存结果：播下一株后，
        /// 相邻播种限制（<see cref="PlantUtility.AdjacentSowBlocker"/>）就会变。
        /// 多个候选时随机取一个，避免总是从同一侧开始种、长出一条直线。
        ///
        /// 判定用 <c>CanNowPlantAt(..., canWipePlantsExceptTree: true)</c>：
        /// 格子上的野生草/灌木算「可以铲掉」，**树仍然阻挡**（不铲树）。
        /// 选中之后再用 <see cref="WipeablePlantAt"/> 判断这格上到底有没有该铲的东西。
        /// </summary>
        /// <param name="plantToWipe">需要先铲掉的野生植物；null 表示这格本来就空着。</param>
        public static bool TryFindSowCell(ThingDef crop, IntVec3 center, Map map, float radius,
                                          out IntVec3 cell, out Plant plantToWipe)
        {
            cell = IntVec3.Invalid;
            plantToWipe = null;
            if (crop == null || crop.plant == null || map == null || radius <= 0f)
            {
                return false;
            }
            int found = 0;
            IntVec3 picked = IntVec3.Invalid;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!candidate.InBounds(map))
                {
                    continue;
                }
                if (!crop.CanNowPlantAt(candidate, map, canWipePlantsExceptTree: true))
                {
                    continue;
                }
                if (PlantUtility.AdjacentSowBlocker(crop, candidate, map) != null)
                {
                    continue;
                }
                // 蓄水池抽样：一遍扫描、不建列表，也能均匀随机选一个。
                found++;
                if (Rand.Range(0, found) == 0)
                {
                    picked = candidate;
                }
            }
            if (found == 0)
            {
                return false;
            }
            cell = picked;
            plantToWipe = WipeablePlantAt(cell, map);
            return true;
        }

        /// <summary>
        /// 这格上有没有「可以顺手铲掉」的植物（只查，不销毁）。
        ///
        /// 可铲 = 三种都满足：不是树、不是人工种的（<c>sown</c>）、
        /// 没有 <see cref="CompPlantPreventCutting"/> 或者它没开「禁止修剪」。
        /// 树不铲：原版砍树要记录历史事件、生成树桩，还得有真实 Pawn 当 actor。
        /// </summary>
        public static Plant WipeablePlantAt(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
            {
                return null;
            }
            Plant plant = cell.GetPlant(map);
            if (plant == null || plant.Destroyed || plant.def?.plant == null)
            {
                return null;
            }
            if (plant.def.plant.IsTree)
            {
                return null;
            }
            if (plant.sown)
            {
                return null;
            }
            CompPlantPreventCutting preventCutting = plant.TryGetComp<CompPlantPreventCutting>();
            if (preventCutting != null && preventCutting.PreventCutting)
            {
                return null;
            }
            return plant;
        }

        /// <summary>
        /// 在指定格种下一株作物，返回新植株（失败返回 null）。
        ///
        /// 顺序照原版播种：先 <see cref="GenSpawn.Spawn(Thing, IntVec3, Map, Rot4, WipeMode)"/>，
        /// 再把成长度设成 <see cref="SownGrowth"/>、标 <c>sown = true</c>（这两步决定它算「人工种植」
        /// 且贴图不再是空地块），最后 MapMeshDirty —— 这版引擎改成长度**不会**自动重刷网格。
        /// </summary>
        public static Plant SowAt(ThingDef crop, IntVec3 cell, Map map)
        {
            if (crop == null || map == null || !cell.InBounds(map))
            {
                return null;
            }
            Plant plant = ThingMaker.MakeThing(crop) as Plant;
            if (plant == null)
            {
                Log.ErrorOnce("Botania: " + crop.defName + " is not a Plant thingClass, cannot sow magically.",
                    crop.defName.GetHashCode());
                return null;
            }
            GenSpawn.Spawn(plant, cell, map, Rot4.North);
            plant.Growth = SownGrowth;
            plant.sown = true;
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things);
            return plant;
        }

        /// <summary>
        /// 半径内成熟树的棵数（最多数 <paramref name="max"/> 棵）。
        /// 「成熟」= LifeStage == <see cref="PlantLifeStage.Mature"/>；树苗不算。
        /// </summary>
        public static int CountMatureTrees(IntVec3 center, Map map, float radius, int max)
        {
            return FindMatureTrees(center, map, radius, max).Count;
        }

        /// <summary>
        /// 半径内成熟树的列表（最多 <paramref name="max"/> 棵）。
        /// 「成熟」= LifeStage == <see cref="PlantLifeStage.Mature"/>；树苗不算。
        ///
        /// 取木要逐棵发原木、逐棵认领（<see cref="MapComponent_TreeClaims"/>），
        /// 所以除了数数量还得拿到实例。
        /// </summary>
        public static List<Plant> FindMatureTrees(IntVec3 center, Map map, float radius, int max)
        {
            List<Plant> result = new List<Plant>();
            if (map == null || max <= 0 || radius <= 0f)
            {
                return result;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.Destroyed || plant.def?.plant == null)
                {
                    continue;
                }
                if (!plant.def.plant.IsTree || plant.LifeStage != PlantLifeStage.Mature)
                {
                    continue;
                }
                result.Add(plant);
                if (result.Count >= max)
                {
                    break;
                }
            }
            return result;
        }

        /// <summary>这株植物还能不能被催熟：活着、且没长到 100%。</summary>
        public static bool CanRipen(Plant plant)
        {
            if (plant == null || plant.Destroyed || plant.def?.plant == null)
            {
                return false;
            }
            return plant.Growth < 1f;
        }

        /// <summary>这株植物吃一发催熟会不会有反应：熟透的按收获处理，没熟的长成长度。</summary>
        public static bool CanReactToRipen(Plant plant)
        {
            return CanMagicallyHarvest(plant) || CanRipen(plant);
        }

        /// <summary>
        /// 半径内**非树**植物的数量（催熟按钮的「范围里有没有东西」判定）。
        /// 树不算：催熟从不碰树。
        /// </summary>
        public static int CountNonTreePlants(IntVec3 center, Map map, float radius)
        {
            if (map == null || radius <= 0f)
            {
                return 0;
            }
            int count = 0;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.Destroyed || plant.def?.plant == null)
                {
                    continue;
                }
                if (plant.def.plant.IsTree)
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// 半径内「催熟能有反应」的株数：熟透可收的 + 还没熟的。
        ///
        /// 与 <see cref="RipenArea"/> 的判定一致，供调用方在**扣魔力之前**判断
        /// 「这一发催熟到底有没有活干」，避免扣了钱一件没做。
        /// </summary>
        public static int CountRipenablePlants(IntVec3 center, Map map, float radius)
        {
            if (map == null || radius <= 0f)
            {
                return 0;
            }
            int count = 0;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.Destroyed || plant.def?.plant == null || plant.def.plant.IsTree)
                {
                    continue;
                }
                if (!CanReactToRipen(plant))
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// 催熟一片地：半径内**所有非树植物**都过一遍，不设株数上限。
        ///
        /// 已经熟透的（<see cref="CanMagicallyHarvest"/>）直接按收获流程收走：
        /// 产量 × 档位系数、产物落在它自己脚下、<c>PlantCollected(null, Chop)</c> 收尾；
        /// 还没熟的 <c>Growth += growth</c>（上限 1）并 MapMeshDirty（这版引擎改成长度
        /// **不会**自动重刷网格，不 dirty 就顶着幼苗贴图）。
        ///
        /// **不扣魔力**：价格固定，由调用方先检查再走 <see cref="ManaDeviceCore.TryPay"/>。
        /// 先物化列表再处理：收获会销毁植株，边遍历 GenRadial 边改 Thing 列表会漏处理。
        /// </summary>
        public static int RipenArea(IntVec3 center, Map map, float radius, float growth, float yieldFactor)
        {
            if (map == null || radius <= 0f)
            {
                return 0;
            }
            // 先物化：收获会销毁植株，边遍历 GenRadial 边销毁会漏处理。
            List<Plant> plants = new List<Plant>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.Destroyed || plant.def?.plant == null || plant.def.plant.IsTree)
                {
                    continue;
                }
                plants.Add(plant);
            }
            int acted = 0;
            for (int i = 0; i < plants.Count; i++)
            {
                Plant plant = plants[i];
                if (plant == null || plant.Destroyed)
                {
                    continue;
                }
                if (CanMagicallyHarvest(plant))
                {
                    int amount = ApplySkillYield(plant.YieldNow(), yieldFactor);
                    ThingDef productDef = plant.def.plant.harvestedThingDef;
                    if (productDef != null && amount > 0)
                    {
                        DropThing(productDef, amount, plant.Position, map);
                    }
                    plant.PlantCollected(null, PlantDestructionMode.Chop);
                    acted++;
                    continue;
                }
                if (!CanRipen(plant))
                {
                    continue;
                }
                plant.Growth = plant.Growth + growth > 1f ? 1f : plant.Growth + growth;
                map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
                acted++;
            }
            return acted;
        }

        /// <summary>
        /// 半径内可被魔力收获的植株列表（最多 <paramref name="max"/> 株）。
        ///
        /// 先物化再处理：收获会销毁植株，边遍历 GenRadial 边改 Thing 列表会漏处理。
        /// </summary>
        public static List<Plant> FindHarvestablePlants(IntVec3 center, Map map, float radius, int max)
        {
            List<Plant> result = new List<Plant>();
            if (map == null || max <= 0 || radius <= 0f)
            {
                return result;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                Plant plant = thing as Plant;
                if (!CanMagicallyHarvest(plant))
                {
                    continue;
                }
                result.Add(plant);
                if (result.Count >= max)
                {
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// 把 <paramref name="count"/> 个 <paramref name="def"/> 扔到 <paramref name="cell"/> 附近
        /// （产物、原木、花瓣都走它）。放不下就销毁并返回 null，不留下未生成的野 Thing。
        /// </summary>
        public static Thing DropThing(ThingDef def, int count, IntVec3 cell, Map map)
        {
            if (def == null || map == null || count <= 0)
            {
                return null;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
            {
                thing.Destroy(DestroyMode.Vanish);
                return null;
            }
            return thing;
        }
    }
}
