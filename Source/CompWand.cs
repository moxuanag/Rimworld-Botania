using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    public class CompProperties_Wand : CompProperties
    {
        /// <summary>「绽花」影响半径。</summary>
        public float bloomRadius = 4f;

        /// <summary>每次绽花给植物增加多少成长度（0~1）。</summary>
        public float bloomGrowth = 0.25f;

        /// <summary>每株植物消耗的魔力。</summary>
        public float manaPerPlant = 20f;

        /// <summary>「就地转化」的搜索半径。</summary>
        public float convertRadius = 1.5f;

        /// <summary>每转化一个物品消耗的魔力。</summary>
        public float manaPerConvert = 20f;

        /// <summary>一次最多转化几堆。</summary>
        public int maxConvertsPerUse = 9;

        /// <summary>向魔力网抽取的半径。</summary>
        public float sourceRadius = 8f;

        /// <summary>施法距离（与两个技能的 verb range 保持一致，用于画范围环）。</summary>
        public float castRange = 6f;

        /// <summary>与白雏菊共用的转化规则表。</summary>
        public List<ManaConversionRule> rules = new List<ManaConversionRule>();

        public CompProperties_Wand()
        {
            compClass = typeof(CompWand);
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
    /// 真正的按钮由装备上的两个 verb 提供（Verb_WandBloom / Verb_WandConvert），
    /// 因为这一版引擎不会把装备 Comp 的 gizmo 显示到小人身上；
    /// 本 Comp 只保留逻辑，另外在法杖躺在**地上**被选中时也提供一组带图标的按钮。
    /// </summary>
    public class CompWand : ThingComp
    {
        public CompProperties_Wand Props => (CompProperties_Wand)props;

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
            // 装备在身上时由 verb 提供按钮，这里只负责"法杖丢在地上被选中"的情况，
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
                    Props.convertRadius.ToString("F0")),
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

            if (!TryPayMana(center, map, targets.Count * Props.manaPerPlant))
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
                // 这一版引擎里改 Growth 不会自动重刷贴图，成熟度涨了但画面还是幼苗，
                // 所以手动把所在格的网格标脏，让它按新成熟度重画。
                if (plant.Spawned)
                {
                    map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
                }
            }
            Messages.Message("Botania.WandBloomed".Translate(targets.Count.ToString()), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        /// <summary>把 center 周围的木料/石砖转化掉。返回是否真的做成了一次。</summary>
        public bool TryConvertAt(IntVec3 center, Map map)
        {
            if (map == null)
            {
                return false;
            }

            List<Thing> targets = new List<Thing>();
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
                    if (rule == null || thing.stackCount < rule.count)
                    {
                        continue;
                    }
                    targets.Add(thing);
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

            if (targets.Count == 0)
            {
                Messages.Message("Botania.WandNoInput".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!TryPayMana(center, map, targets.Count * Props.manaPerConvert))
            {
                return false;
            }

            int done = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Thing thing = targets[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                ManaConversionRule rule = ManaTransmutation.FindRule(Props.rules, thing.def);
                if (ManaTransmutation.TryConvert(thing, rule))
                {
                    done++;
                }
            }
            Messages.Message("Botania.WandConverted".Translate(done.ToString()), MessageTypeDefOf.PositiveEvent, false);
            return done > 0;
        }

        /// <summary>先确认凑得齐，再一次性扣；扣不满就退款，不做半吊子。</summary>
        private bool TryPayMana(IntVec3 center, Map map, float cost)
        {
            if (cost <= 0f)
            {
                return true;
            }

            // 法杖有一套内置储能（500），优先从杖内扣；不够时它会自己去附近魔力网借
            CompManaWeapon storage = parent.GetComp<CompManaWeapon>();
            if (storage != null)
            {
                if (storage.TrySpend(cost))
                {
                    return true;
                }
                Messages.Message("Botania.WandNoMana".Translate(cost.ToString("F0"),
                    storage.Mana.ToString("F0")), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            IManaSource source = ManaSourceUtil.FindNearest(center, map, Props.sourceRadius, cost);
            if (source == null)
            {
                Messages.Message("Botania.WandNoMana".Translate(cost.ToString("F0"), "0"),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            float drained = source.DrainMana(cost);
            if (drained < cost - 0.01f)
            {
                source.AddMana(drained);
                Messages.Message("Botania.WandNoMana".Translate(cost.ToString("F0"),
                    (drained).ToString("F0")), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return true;
        }
    }
}
