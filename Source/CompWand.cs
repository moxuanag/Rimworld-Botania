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
    /// 森林法杖：装备在腰带层，给穿戴者两个按钮。
    /// 它不是武器，而是"把魔力网的手伸到远处"——消耗附近魔力池/中继塔的魔力，
    /// 在玩家指定的位置做两件原本只能由建筑完成的事：催长植物、转化材料。
    /// </summary>
    public class CompWand : ThingComp
    {
        public CompProperties_Wand Props => (CompProperties_Wand)props;

        private Pawn Wearer
        {
            get
            {
                Pawn_ApparelTracker tracker = parent.ParentHolder as Pawn_ApparelTracker;
                return tracker?.pawn;
            }
        }

        /// <summary>法杖要么被穿着（跟着小人走），要么躺在地上被选中。</summary>
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

        private IntVec3 AnchorPos
        {
            get
            {
                Pawn wearer = Wearer;
                return wearer != null ? wearer.Position : parent.Position;
            }
        }

        private Map AnchorMap
        {
            get
            {
                Pawn wearer = Wearer;
                return wearer != null ? wearer.Map : parent.Map;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (!HasAnchor)
            {
                yield break;
            }
            Texture2D icon = parent.def.uiIcon;

            yield return new Command_Action
            {
                defaultLabel = "Botania.WandBloomLabel".Translate(),
                defaultDesc = "Botania.WandBloomDesc".Translate(Props.bloomRadius.ToString("F0"),
                    Props.manaPerPlant.ToString("F0")),
                icon = icon,
                action = Bloom
            };

            yield return new Command_Action
            {
                defaultLabel = "Botania.WandConvertLabel".Translate(),
                defaultDesc = "Botania.WandConvertDesc".Translate(Props.manaPerConvert.ToString("F0")),
                icon = icon,
                action = Convert
            };
        }

        private void Bloom()
        {
            Map map = AnchorMap;
            if (map == null)
            {
                return;
            }
            IntVec3 center = AnchorPos;

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
                return;
            }

            if (!TryPayMana(center, map, targets.Count * Props.manaPerPlant))
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Plant plant = targets[i];
                if (plant == null || plant.Destroyed)
                {
                    continue;
                }
                plant.Growth = Mathf.Min(1f, plant.Growth + Props.bloomGrowth);
            }
            Messages.Message("Botania.WandBloomed".Translate(targets.Count.ToString()), MessageTypeDefOf.PositiveEvent, false);
        }

        private void Convert()
        {
            Map map = AnchorMap;
            if (map == null)
            {
                return;
            }
            IntVec3 center = AnchorPos;

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
                return;
            }

            if (!TryPayMana(center, map, targets.Count * Props.manaPerConvert))
            {
                return;
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
        }

        /// <summary>先确认凑得齐，再一次性扣；扣不满就退款，不做半吊子。</summary>
        private bool TryPayMana(IntVec3 center, Map map, float cost)
        {
            if (cost <= 0f)
            {
                return true;
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
