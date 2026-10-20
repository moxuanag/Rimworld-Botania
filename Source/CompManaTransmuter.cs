using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>一条转化规则：把某种物品变成另一种物品。</summary>
    public class ManaConversionRule
    {
        /// <summary>输入物品。留空则用 fromCategory 匹配。</summary>
        public ThingDef fromDef;

        /// <summary>输入物品类别（如 StoneBlocks）。fromDef 为空时生效。</summary>
        public ThingCategoryDef fromCategory;

        /// <summary>输出物品。</summary>
        public ThingDef toDef;

        /// <summary>每次消耗/产出几个。</summary>
        public int count = 1;
    }

    public class CompProperties_ManaTransmuter : CompProperties
    {
        /// <summary>两次转化之间的间隔（tick）。</summary>
        public int ticksPerConvert = 300;

        /// <summary>每次转化消耗的魔力。</summary>
        public float manaPerConvert = 20f;

        /// <summary>搜索魔力源的半径。</summary>
        public float manaSearchRadius = 6f;

        /// <summary>转化规则表。</summary>
        public List<ManaConversionRule> rules = new List<ManaConversionRule>();

        public CompProperties_ManaTransmuter()
        {
            compClass = typeof(CompManaTransmuter);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (ticksPerConvert <= 0)
            {
                yield return "ticksPerConvert must be greater than 0.";
            }
            if (manaPerConvert < 0f)
            {
                yield return "manaPerConvert must not be negative.";
            }
            if (rules == null || rules.Count == 0)
            {
                yield return "rules must contain at least one entry.";
                yield break;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                ManaConversionRule rule = rules[i];
                if (rule == null)
                {
                    yield return $"rules[{i}] is null.";
                    continue;
                }
                if (rule.fromDef == null && rule.fromCategory == null)
                {
                    yield return $"rules[{i}] must define either fromDef or fromCategory.";
                }
                if (rule.toDef == null)
                {
                    yield return $"rules[{i}] is missing toDef.";
                }
                if (rule.count <= 0)
                {
                    yield return $"rules[{i}].count must be greater than 0.";
                }
            }
        }
    }

    /// <summary>
    /// 白雏菊的转化逻辑：把相邻格的木头变成活木、石块变成活石，每次消耗一点魔力。
    ///
    /// 与"配方"不同，它不需要小人，玩家把材料堆在花旁边就会自动持续工作；
    /// 代价是必须挨着魔力源，且速度有限。这就是本模组的自动化入口。
    /// </summary>
    public class CompManaTransmuter : ThingComp
    {
        private int ticksAccum;

        private bool enabled = true;

        private bool hasInput;
        private bool hasMana;

        public CompProperties_ManaTransmuter Props => (CompProperties_ManaTransmuter)props;

        public bool Enabled => enabled;

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !enabled)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < Props.ticksPerConvert)
            {
                return;
            }
            ticksAccum = 0;
            TryConvert();
        }

        private void TryConvert()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            hasInput = false;
            hasMana = false;

            Thing input = FindInput(map, out ManaConversionRule rule);
            if (input == null)
            {
                return;
            }
            hasInput = true;

            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, map, Props.manaSearchRadius, Props.manaPerConvert);
            if (source == null)
            {
                return;
            }

            float drained = source.DrainMana(Props.manaPerConvert);
            // 魔力不够就还回去，不能让玩家白白损失一半
            if (drained < Props.manaPerConvert - 0.01f)
            {
                source.AddMana(drained);
                return;
            }
            hasMana = true;

            IntVec3 cell = input.PositionHeld;
            int count = Math.Min(rule.count, input.stackCount);
            input.SplitOff(count).Destroy(DestroyMode.Vanish);

            Thing output = ThingMaker.MakeThing(rule.toDef, null);
            output.stackCount = count;
            GenPlace.TryPlaceThing(output, cell, map, ThingPlaceMode.Near);
        }

        private Thing FindInput(Map map, out ManaConversionRule matchedRule)
        {
            matchedRule = null;
            foreach (IntVec3 cell in parent.OccupiedRect().ExpandedBy(1))
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
                    // 只处理直接躺在地上的物品：容器/角色身上的不动
                    if (!(thing.ParentHolder is Map))
                    {
                        continue;
                    }
                    // 玩家手动禁用的物品不动，避免偷走不该动的库存
                    if (thing.IsForbidden(Faction.OfPlayerSilentFail))
                    {
                        continue;
                    }
                    ManaConversionRule rule = FindRule(thing.def);
                    if (rule == null || thing.stackCount < rule.count)
                    {
                        continue;
                    }
                    matchedRule = rule;
                    return thing;
                }
            }
            return null;
        }

        private ManaConversionRule FindRule(ThingDef def)
        {
            if (Props.rules == null)
            {
                return null;
            }
            for (int i = 0; i < Props.rules.Count; i++)
            {
                ManaConversionRule rule = Props.rules[i];
                if (rule == null)
                {
                    continue;
                }
                if (rule.fromDef != null && def == rule.fromDef)
                {
                    return rule;
                }
                if (rule.fromDef == null && rule.fromCategory != null && def.IsWithinCategory(rule.fromCategory))
                {
                    return rule;
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
            if (!enabled)
            {
                return "Botania.TransmuterDisabled".Translate();
            }
            if (!hasInput)
            {
                return "Botania.TransmuterNoInput".Translate();
            }
            if (!hasMana)
            {
                return "Botania.TransmuterNoMana".Translate();
            }
            return "Botania.TransmuterWorking".Translate();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "转化开关",
                defaultDesc = "关闭后白雏菊会停下，不再消耗魔力与材料。",
                isActive = () => enabled,
                toggleAction = () => enabled = !enabled
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref ticksAccum, "ticksAccum", 0);
        }
    }
}
