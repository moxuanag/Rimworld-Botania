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
        /// <summary>两次转化之间的间隔（tick），有魔力网络时生效。</summary>
        public int ticksPerConvert = 300;

        /// <summary>每次转化消耗的魔力。</summary>
        public float manaPerConvert = 20f;

        /// <summary>搜索魔力源的半径。</summary>
        public float manaSearchRadius = 6f;

        /// <summary>
        /// 附近没有可用魔力网络（没有池子/中继塔，或它们都空了）时，
        /// 转化间隔乘以这个倍数，并且不消耗魔力。
        /// </summary>
        public int noManaSlowdown = 5;

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
            if (noManaSlowdown < 1)
            {
                yield return "noManaSlowdown must be at least 1.";
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
    /// 白雏菊的转化逻辑：把相邻格的木头变成活木、石块变成活石。
    ///
    /// 两种运行状态：
    ///   · 附近有可用的魔力网络 → 按 ticksPerConvert 全速转化，每次扣 manaPerConvert 魔力；
    ///   · 附近没有可用网络（没池子，或池子全空）→ 仍然转化，但间隔 × noManaSlowdown，
    ///     且不消耗魔力。这保证开局没有魔力池时也能起量，只是慢得多。
    /// </summary>
    public class CompManaTransmuter : ThingComp
    {
        private int ticksAccum;

        private bool enabled = true;

        private bool hasInput;
        private bool hasMana;

        /// <summary>当前生效的转化间隔，由上一次转化结果决定。</summary>
        private int currentInterval;

        public CompProperties_ManaTransmuter Props => (CompProperties_ManaTransmuter)props;

        public bool Enabled => enabled;

        private int SlowInterval
        {
            get
            {
                int factor = Props.noManaSlowdown < 1 ? 1 : Props.noManaSlowdown;
                return Props.ticksPerConvert * factor;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !enabled)
            {
                return;
            }

            if (currentInterval <= 0)
            {
                currentInterval = Props.ticksPerConvert;
            }

            ticksAccum++;
            if (ticksAccum < currentInterval)
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

            ManaConversionRule rule;
            Thing input = FindInput(map, out rule);
            if (input == null)
            {
                return;
            }
            hasInput = true;

            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, map, Props.manaSearchRadius, Props.manaPerConvert);

            // ── 情况一：没有可用魔力网络 → 慢速转化，不消耗魔力 ──
            if (source == null)
            {
                currentInterval = SlowInterval;
                ManaTransmutation.TryConvert(input, rule);
                return;
            }

            float drained = source.DrainMana(Props.manaPerConvert);
            // 魔力不够就还回去，不能让玩家白白损失一半
            if (drained < Props.manaPerConvert - 0.01f)
            {
                source.AddMana(drained);
                // 池子空了等同于「没有可用网络」：同样降速，但不消耗
                currentInterval = SlowInterval;
                ManaTransmutation.TryConvert(input, rule);
                return;
            }

            // ── 情况二：正常全速转化 ──
            hasMana = true;
            currentInterval = Props.ticksPerConvert;
            ManaTransmutation.TryConvert(input, rule);
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
                    ManaConversionRule rule = ManaTransmutation.FindRule(Props.rules, thing.def);
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
                return "Botania.TransmuterSlow".Translate(Props.noManaSlowdown);
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
            Scribe_Values.Look(ref currentInterval, "currentInterval", 0);
        }
    }
}
