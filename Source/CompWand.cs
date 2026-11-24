using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 森林法杖（森之法杖）的两个法术配置。
    ///
    /// 找魔力源 / 凑够才扣 / 冷却节流 / 存档键已并入物品付款框架
    /// <see cref="CompProperties_ManaItem"/> + <see cref="ManaItemCore"/>；
    /// 本类只保留两个法术自己的参数（绽花半径 / 转化规则 / 每株每点费用……）。
    ///
    /// 旧字段 <c>sourceRadius</c>（XML 写的是 8）已并入基类 <c>searchRadius</c>，
    /// 数值不变（8）。XML 需要把
    /// <c>&lt;sourceRadius&gt;8&lt;/sourceRadius&gt;</c> 改名为
    /// <c>&lt;searchRadius&gt;8&lt;/searchRadius&gt;</c>，其余字段不用动。
    /// </summary>
    public class CompProperties_Wand : CompProperties_ManaItem
    {
        /// <summary>「绽花」影响半径。</summary>
        public float bloomRadius = 4f;

        /// <summary>每次绽花给植物增加多少成长度（0~1）。</summary>
        public float bloomGrowth = 0.25f;

        /// <summary>每株植物消耗的魔力。</summary>
        public float manaPerPlant = 20f;

        /// <summary>「就地转化」的搜索半径。</summary>
        public float convertRadius = 1.5f;

        /// <summary>
        /// 每一堆输入一次转化多少个（受堆叠数限制）。
        /// 单独放在这里而不是共用 rule.count，是因为白雏菊一次只转 1 个，
        /// 两者不应该互相牵制。
        /// </summary>
        public int convertBatchSize = 10;

        /// <summary>每转化出一个物品消耗的魔力。</summary>
        public float manaPerConvert = 20f;

        /// <summary>一次最多转化几堆。</summary>
        public int maxConvertsPerUse = 9;

        // 旧字段 sourceRadius 已并入基类 CompProperties_ManaItem.searchRadius
        // （见类注释里的 XML 改名说明）。

        /// <summary>施法距离（与两个技能的 verb range 保持一致，用于画范围环）。</summary>
        public float castRange = 6f;

        /// <summary>与白雏菊共用的转化规则表。</summary>
        public List<ManaConversionRule> rules = new List<ManaConversionRule>();

        public CompProperties_Wand()
        {
            compClass = typeof(CompWand);
            // 本引擎存档把组件字段平铺在 Thing 节点下，键必须够独特。
            // 法杖以前没有任何存档字段，所以 legacy* 旧键留空。
            saveKeyPrefix = "botaniaForestWand";
            // 两个法术按「株」「个」动态计费，不用固定费用（基类的 manaPerUse 不适用）。
            manaPerUse = 0f;
            // 冷却仍然由 AbilityDef 的 cooldownTicksRange=60 管理；
            // 这里保持 0，避免「技能冷却 + 物品冷却」两层节流叠加（AGENTS.md §5）。
            cooldownTicks = 0;
            // 沿用旧字段 sourceRadius 在 XML 里的值 8（基类默认也是 8）。
            searchRadius = 8f;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (bloomRadius <= 0f)
            {
                yield return "bloomRadius must be greater than 0.";
            }
            if (convertRadius <= 0f)
            {
                yield return "convertRadius must be greater than 0.";
            }
            if (manaPerPlant < 0f || manaPerConvert < 0f)
            {
                yield return "mana costs must not be negative.";
            }
        }
    }

    /// <summary>
    /// 森林法杖的两个法术：绽花、就地转化。
    /// 真正的按钮由装备上的两个技能（AbilityDef）提供，
    /// 本 Comp 只保留逻辑，另外在法杖躺在**地上**被选中时也提供一组带图标的按钮。
    ///
    /// 魔力一律从附近魔力网扣（魔力池 / 中继塔，半径 = props.searchRadius），
    /// 由 <see cref="ManaItemCore"/> 负责「找源 + 凑够才扣 + 节流 + 存档」；
    /// 法杖自带的 500 储能（CompManaWeapon）只服务开火，不再被法术消耗。
    /// </summary>
    public class CompWand : ThingComp
    {
        private ManaItemCore core;

        public CompProperties_Wand Props => (CompProperties_Wand)props;

        /// <summary>物品付款框架：找魔力源 / 凑够才扣 / 冷却 / 存档。</summary>
        private ManaItemCore Core => core ??= new ManaItemCore(this, Props);

        private Pawn Wearer => EquipUtil.GetHolder(parent);

        /// <summary>法杖要么被拿着（武器），要么被穿着（衣服），要么躺在地上被选中。</summary>
        private bool HasAnchor
        {
            get
            {
                Pawn wearer = Wearer;
                if (wearer != null)
                {
                    // 商队/远行队里没有地图，按钮点了也没意义
                    return wearer.Map != null;
                }
                return parent.Spawned;
            }
        }

        private IntVec3 AnchorPos => EquipUtil.GetPosition(parent);

        private Map AnchorMap => EquipUtil.GetMap(parent);

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 装备在身上时由技能（AbilityDef）提供按钮，这里只负责"法杖丢在地上被选中"的情况，
            // 免得同一个技能出现两遍
            if (!parent.Spawned || parent.Map == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "Botania.WandBloomLabel".Translate(),
                defaultDesc = "Botania.WandBloomDesc".Translate(Props.bloomRadius.ToString("F0"),
                    Props.manaPerPlant.ToString("F0")),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/Botania/Bloom"),
                action = () => TryBloomAt(AnchorPos, AnchorMap)
            };

            yield return new Command_Action
            {
                defaultLabel = "Botania.WandConvertLabel".Translate(),
                defaultDesc = "Botania.WandConvertDesc".Translate(Props.manaPerConvert.ToString("F0"),
                    Props.convertRadius.ToString("F0"),
                    Props.convertBatchSize.ToString()),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/Botania/Transfer"),
                action = () => TryConvertAt(AnchorPos, AnchorMap)
            };
        }

        /// <summary>在 center 周围催长植物。返回是否真的做成了一次。</summary>
        public bool TryBloomAt(IntVec3 center, Map map)
        {
            if (map == null)
            {
                return false;
            }
            // 节流只有 Core 一处；法杖 cooldownTicks = 0，冷却仍由 AbilityDef 管，
            // 这里不要再叠第二个 tick 计数（AGENTS.md §5）。
            if (!Core.Ready)
            {
                return false;
            }

            List<Plant> targets = new List<Plant>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, Props.bloomRadius, true))
            {
                Plant plant = thing as Plant;
                if (plant == null || plant.def.plant == null)
                {
                    continue;
                }
                if (plant.Growth >= 1f || plant.Destroyed)
                {
                    continue;
                }
                targets.Add(plant);
            }

            if (targets.Count == 0)
            {
                Messages.Message("Botania.WandNoPlants".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!TryPayMana(targets.Count * Props.manaPerPlant))
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Plant plant = targets[i];
                if (plant == null || plant.Destroyed)
                {
                    continue;
                }
                plant.Growth = Mathf.Min(1f, plant.Growth + Props.bloomGrowth);
                RefreshPlantGraphic(plant, map);
            }
            Core.NotifyUsed();
            Messages.Message("Botania.WandBloomed".Translate(targets.Count.ToString()), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        /// <summary>
        /// 植物贴图按成长阶段取自不同的 Graphic（幼苗期用 immatureGraphic）。
        /// 这一版引擎里改 Growth 不会自动重刷地图网格，于是出现
        /// 「成熟度已经 100% 了，画面还是刚种下去的样子」。这里手动标脏该格，
        /// 并顺手清掉 Graphic 缓存，让重画时按新成熟度取到正确贴图。
        /// </summary>
        private static void RefreshPlantGraphic(Plant plant, Map map)
        {
            if (plant == null || plant.Destroyed || map == null)
            {
                return;
            }
            plant.DirtyMapMesh(map);
            if (plant.Spawned)
            {
                map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            }
        }

        /// <summary>把 center 周围的木料/石砖转化掉。返回是否真的做成了一次。</summary>
        public bool TryConvertAt(IntVec3 center, Map map)
        {
            if (map == null)
            {
                return false;
            }
            // 节流只有 Core 一处（同 TryBloomAt 的说明）。
            if (!Core.Ready)
            {
                return false;
            }

            // 第一遍：只挑目标并算出每一堆实际要转几个，先攒出总产量。
            // 这样魔力能按"真正产出多少"来扣，不会出现扣了钱却少给货。
            List<Thing> targets = new List<Thing>();
            List<int> amounts = new List<int>();
            int totalOut = 0;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.convertRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing.def.category != ThingCategory.Item)
                    {
                        continue;
                    }
                    if (!(thing.ParentHolder is Map))
                    {
                        continue;
                    }
                    if (thing.IsForbidden(Faction.OfPlayerSilentFail))
                    {
                        continue;
                    }
                    ManaConversionRule rule = ManaTransmutation.FindRule(Props.rules, thing.def);
                    if (rule == null || thing.stackCount < 1)
                    {
                        continue;
                    }
                    int take = Mathf.Min(Props.convertBatchSize, thing.stackCount);
                    if (take <= 0)
                    {
                        continue;
                    }
                    targets.Add(thing);
                    amounts.Add(take);
                    totalOut += take;
                    if (targets.Count >= Props.maxConvertsPerUse)
                    {
                        break;
                    }
                }
                if (targets.Count >= Props.maxConvertsPerUse)
                {
                    break;
                }
            }

            if (targets.Count == 0 || totalOut <= 0)
            {
                Messages.Message("Botania.WandNoInput".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // 魔力按产量结算：一个活木/活石 = manaPerConvert 点魔力
            if (!TryPayMana(totalOut * Props.manaPerConvert))
            {
                return false;
            }

            int produced = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Thing thing = targets[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                ManaConversionRule rule = ManaTransmutation.FindRule(Props.rules, thing.def);
                if (ManaTransmutation.TryConvert(thing, rule, amounts[i]))
                {
                    produced += amounts[i];
                }
            }
            bool succeeded = produced > 0;
            if (succeeded)
            {
                // 只有真正生效才算一次使用（失败不占冷却，和技能那边的语义一致）。
                Core.NotifyUsed();
            }
            Messages.Message("Botania.WandConverted".Translate(produced.ToString()), MessageTypeDefOf.PositiveEvent, false);
            return succeeded;
        }

        /// <summary>
        /// 付款交给物品框架：就近找魔力池/中继塔（半径 = props.searchRadius），
        /// **凑够才扣**，抽不满会退回，不做半吊子。
        /// 法术付的是「附近魔力网」的魔力，杖内储能（CompManaWeapon）只用于开火；
        /// 所以提示里的「可用」也取附近实际可用的余量（没有源就是 0）。
        /// </summary>
        private bool TryPayMana(float cost)
        {
            if (cost <= 0f)
            {
                return true;
            }
            if (Core.TryPay(cost))
            {
                return true;
            }
            Messages.Message("Botania.WandNoMana".Translate(cost.ToString("F0"),
                Core.NearbyMana().ToString("F0")), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Core.ExposeData();
        }
    }
}
