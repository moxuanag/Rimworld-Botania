using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>精灵门的一条投入料：某种物品（或某类别）需要几个。</summary>
    public class ManaGateIngredient
    {
        /// <summary>具体物品。留空则用 fromCategory 匹配。</summary>
        public ThingDef fromDef;

        /// <summary>物品类别（如 Petals）。fromDef 为空时生效。</summary>
        public ThingCategoryDef fromCategory;

        /// <summary>需要几个。</summary>
        public int count = 1;
    }

    /// <summary>精灵门的一条转化配方：一组投入 + 魔力 + 时间 → 一个产出。</summary>
    public class ManaGateRule
    {
        public List<ManaGateIngredient> inputs = new List<ManaGateIngredient>();

        public ThingDef toDef;

        /// <summary>产出几个。</summary>
        public int outputCount = 1;

        /// <summary>这条转化需要的魔力（在完成那一刻一次性扣）。</summary>
        public float manaCost = 200f;

        /// <summary>门运转多少 tick 才出成品。600 = 10 秒。</summary>
        public int workTicks = 600;

        /// <summary>状态栏里显示的名字（中文直接写，允许为空则用产物的 label）。</summary>
        public string label;
    }

    public class CompProperties_ManaGate : CompProperties
    {
        public List<ManaGateRule> rules = new List<ManaGateRule>();

        /// <summary>找魔力源的半径。</summary>
        public float manaSearchRadius = 8f;

        /// <summary>门洞在建筑下方几格（上梁是 3×1，门洞 3×2 → 填 2）。</summary>
        public int openingDepth = 2;

        /// <summary>
        /// 立柱的 ThingDef 列表（左右柱可以是两个不同的 Def）。
        /// 非空时，上梁两端下方必须各有一列立柱，否则门不工作。
        /// </summary>
        public List<ThingDef> framePillarDefs = new List<ThingDef>();

        /// <summary>立柱高度（格），默认 3。</summary>
        public int framePillarHeight = 3;

        public CompProperties_ManaGate()
        {
            compClass = typeof(CompManaGate);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (openingDepth < 1)
            {
                yield return "ManaGate: openingDepth 必须 ≥ 1";
            }
            if (framePillarHeight < 1)
            {
                yield return "ManaGate: framePillarHeight 必须 ≥ 1";
            }
            if (rules == null || rules.Count == 0)
            {
                yield return "ManaGate: rules 不能为空";
                yield break;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                ManaGateRule rule = rules[i];
                if (rule == null)
                {
                    yield return $"ManaGate: rules[{i}] 为 null";
                    continue;
                }
                if (rule.toDef == null)
                {
                    yield return $"ManaGate: rules[{i}] 缺少 toDef";
                }
                if (rule.inputs == null || rule.inputs.Count == 0)
                {
                    yield return $"ManaGate: rules[{i}] 没有投入料";
                }
                else
                {
                    for (int j = 0; j < rule.inputs.Count; j++)
                    {
                        ManaGateIngredient ing = rule.inputs[j];
                        if (ing == null)
                        {
                            yield return $"ManaGate: rules[{i}].inputs[{j}] 为 null";
                            continue;
                        }
                        if (ing.fromDef == null && ing.fromCategory == null)
                        {
                            yield return $"ManaGate: rules[{i}].inputs[{j}] 需要 fromDef 或 fromCategory";
                        }
                        if (ing.count <= 0)
                        {
                            yield return $"ManaGate: rules[{i}].inputs[{j}] 的 count 必须 > 0";
                        }
                    }
                }
                if (rule.workTicks <= 0)
                {
                    yield return $"ManaGate: rules[{i}] 的 workTicks 必须 > 0";
                }
                if (rule.manaCost < 0f)
                {
                    yield return $"ManaGate: rules[{i}] 的 manaCost 不能为负";
                }
            }
        }
    }

    /// <summary>
    /// 精灵门的运转逻辑：把材料丢进门洞里，门自己找最近的魔力池抽魔力，
    /// 转够 workTicks 后吐出成品。
    ///
    /// 和 CompManaTransmuter（白雏菊）的三点区别，都是为这道门专门定的：
    ///   1. **硬性要求魔力**：没有池子或魔力不够就停在原地等，绝不「免魔慢速转化」——
    ///      精灵门是魔力设施，断魔就该停；
    ///   2. **支持多输入**：元素锭要 魔力钢×2 + 精灵之尘×1，规则表里可以列多条投入；
    ///   3. **有进度**：进度存在 Comp 里并写进检视栏，中途把材料搬走会退回去重来。
    ///
    /// 魔力在**完成那一刻**一次性扣：避免每 tick 扣一点点导致池子被别的东西抢走后
    /// 出现「转了 90% 却付不起尾款」的糊涂账。付不起就停在 100% 前等，不会丢材料。
    /// </summary>
    public class CompManaGate : ThingComp
    {
        private int ruleIndex = -1;
        private int progress;
        private bool enabled = true;

        private bool inputReady;
        private bool manaReady;
        private bool frameReady = true;

        public CompProperties_ManaGate Props => (CompProperties_ManaGate)props;

        private ManaGateRule ActiveRule
        {
            get
            {
                if (ruleIndex < 0 || Props.rules == null || ruleIndex >= Props.rules.Count)
                {
                    return null;
                }
                return Props.rules[ruleIndex];
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !enabled)
            {
                return;
            }
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            // 门框不完整（缺立柱）就完全不工作，避免只放一根上梁就能白嫖
            frameReady = FrameComplete(map);
            if (!frameReady)
            {
                ruleIndex = -1;
                progress = 0;
                return;
            }

            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                int found = FindRule(map);
                inputReady = found >= 0;
                manaReady = false;
                if (found < 0)
                {
                    return;
                }
                ruleIndex = found;
                progress = 0;
                rule = ActiveRule;
            }

            // 材料被搬走／被消耗掉 → 进度作废，别让玩家用「搬走再放回」刷进度
            if (!HasAllInputs(map, rule))
            {
                ruleIndex = -1;
                progress = 0;
                inputReady = false;
                return;
            }
            inputReady = true;

            progress++;
            if (progress < rule.workTicks)
            {
                manaReady = HasMana(rule);
                return;
            }

            // 转满了：付魔力 → 吃材料 → 出成品
            if (!ConsumeMana(rule))
            {
                manaReady = false;
                progress = rule.workTicks;   // 停在 100%，等魔力，不丢进度
                return;
            }
            manaReady = true;
            ConsumeInputs(map, rule);
            Produce(map, rule);
            ruleIndex = -1;
            progress = 0;
        }

        // ── 门洞范围 ────────────────────────────────────────────────
        /// <summary>
        /// 门框是否完整：上梁两端下方各要有一列立柱（默认 3 格高）。
        /// 只放一根上梁不该能当门用，所以这门检查是硬条件。
        /// </summary>
        private bool FrameComplete(Map map)
        {
            if (Props.framePillarDefs == null || Props.framePillarDefs.Count == 0)
            {
                return true;
            }
            CellRect rect = parent.OccupiedRect();
            int topZ = rect.minZ;
            for (int dz = 0; dz < Math.Max(1, Props.framePillarHeight); dz++)
            {
                if (!HasPillarAt(map, new IntVec3(rect.minX - 1, 0, topZ - dz)))
                {
                    return false;
                }
                if (!HasPillarAt(map, new IntVec3(rect.maxX + 1, 0, topZ - dz)))
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasPillarAt(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef def = things[i].def;
                for (int j = 0; j < Props.framePillarDefs.Count; j++)
                {
                    if (def == Props.framePillarDefs[j])
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        /// <summary>上梁下方 openingDepth 格、与建筑同宽的区域，就是「门洞」。</summary>
        private IEnumerable<IntVec3> OpeningCells()
        {
            CellRect rect = parent.OccupiedRect();
            for (int dz = 1; dz <= Props.openingDepth; dz++)
            {
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    yield return new IntVec3(x, 0, rect.minZ - dz);
                }
            }
        }

        private List<Thing> ItemsInOpening(Map map)
        {
            List<Thing> result = new List<Thing>();
            foreach (IntVec3 cell in OpeningCells())
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
                    // 只认躺在地上的，别把别人背着/容器里的算进来
                    if (!(thing.ParentHolder is Map))
                    {
                        continue;
                    }
                    if (thing.IsForbidden(Faction.OfPlayerSilentFail))
                    {
                        continue;
                    }
                    result.Add(thing);
                }
            }
            return result;
        }

        private static bool Matches(ManaGateIngredient ing, ThingDef def)
        {
            if (ing == null || def == null)
            {
                return false;
            }
            if (ing.fromDef != null)
            {
                return def == ing.fromDef;
            }
            return ing.fromCategory != null && def.IsWithinCategory(ing.fromCategory);
        }

        /// <summary>找第一条「门洞里现有的材料能满足」的配方，返回索引，找不到返回 -1。</summary>
        private int FindRule(Map map)
        {
            List<Thing> items = ItemsInOpening(map);
            if (items.Count == 0)
            {
                return -1;
            }
            for (int r = 0; r < Props.rules.Count; r++)
            {
                ManaGateRule rule = Props.rules[r];
                if (rule == null || rule.toDef == null || rule.inputs == null)
                {
                    continue;
                }
                if (SatisfiedBy(rule, items))
                {
                    return r;
                }
            }
            return -1;
        }

        /// <summary>门洞里现有的物品够不够满足这条配方（按各自需求累加比较）。</summary>
        private static bool SatisfiedBy(ManaGateRule rule, List<Thing> items)
        {
            for (int i = 0; i < rule.inputs.Count; i++)
            {
                ManaGateIngredient ing = rule.inputs[i];
                int have = 0;
                for (int j = 0; j < items.Count; j++)
                {
                    if (Matches(ing, items[j].def))
                    {
                        have += items[j].stackCount;
                    }
                }
                if (have < ing.count)
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasAllInputs(Map map, ManaGateRule rule)
        {
            return SatisfiedBy(rule, ItemsInOpening(map));
        }

        /// <summary>按配方把材料从门洞里扣掉（可能跨多个堆）。</summary>
        private void ConsumeInputs(Map map, ManaGateRule rule)
        {
            List<Thing> items = ItemsInOpening(map);
            for (int i = 0; i < rule.inputs.Count; i++)
            {
                ManaGateIngredient ing = rule.inputs[i];
                int need = ing.count;
                for (int j = 0; j < items.Count && need > 0; j++)
                {
                    Thing thing = items[j];
                    if (thing.Destroyed || !Matches(ing, thing.def))
                    {
                        continue;
                    }
                    int take = Math.Min(need, thing.stackCount);
                    if (take <= 0)
                    {
                        continue;
                    }
                    if (take >= thing.stackCount)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                    else
                    {
                        thing.SplitOff(take).Destroy(DestroyMode.Vanish);
                    }
                    need -= take;
                }
            }
        }

        private void Produce(Map map, ManaGateRule rule)
        {
            Thing output = ThingMaker.MakeThing(rule.toDef, null);
            output.stackCount = Math.Max(1, rule.outputCount);
            IntVec3 cell = parent.Position;
            foreach (IntVec3 c in OpeningCells())
            {
                if (c.InBounds(map))
                {
                    cell = c;
                    break;
                }
            }
            GenPlace.TryPlaceThing(output, cell, map, ThingPlaceMode.Near);
        }

        // ── 魔力 ────────────────────────────────────────────────────
        private IManaSource FindSource(ManaGateRule rule)
        {
            return ManaSourceUtil.FindNearest(
                parent.Position, parent.Map, Props.manaSearchRadius, rule.manaCost, parent);
        }

        private bool HasMana(ManaGateRule rule)
        {
            return FindSource(rule) != null;
        }

        private bool ConsumeMana(ManaGateRule rule)
        {
            if (rule.manaCost <= 0f)
            {
                return true;
            }
            IManaSource source = FindSource(rule);
            if (source == null)
            {
                return false;
            }
            float drained = source.DrainMana(rule.manaCost);
            if (drained < rule.manaCost - 0.01f)
            {
                // 抽不满就还回去，别让玩家白亏一半
                source.AddMana(drained);
                return false;
            }
            return true;
        }

        // ── 显示 ────────────────────────────────────────────────────
        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            if (!enabled)
            {
                return "Botania.GateDisabled".Translate();
            }
            if (Props.framePillarDefs != null && Props.framePillarDefs.Count > 0 && parent.Map != null && !FrameComplete(parent.Map))
            {
                return "Botania.GateFrameIncomplete".Translate();
            }
            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                return "Botania.GateNoInput".Translate();
            }
            string name = rule.label.NullOrEmpty() ? rule.toDef.LabelCap.ToString() : rule.label;
            float pct = rule.workTicks <= 0 ? 1f : Math.Min(1f, (float)progress / rule.workTicks);
            if (pct >= 1f && !manaReady)
            {
                return "Botania.GateNoMana".Translate(name, (int)Math.Round(rule.manaCost));
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("Botania.GateWorking".Translate(name, (int)Math.Round(pct * 100f)));
            sb.Append("（").Append((int)Math.Round(rule.manaCost)).Append(" 魔力/件）");
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            yield return new Command_Toggle
            {
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Botania_GateToggle"),
                defaultLabel = "Botania.GateToggle".Translate(),
                defaultDesc = "Botania.GateToggleDesc".Translate(),
                isActive = () => enabled,
                toggleAction = () => enabled = !enabled
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "botaniaGateEnabled", true);
            Scribe_Values.Look(ref ruleIndex, "botaniaGateRule", -1);
            Scribe_Values.Look(ref progress, "botaniaGateProgress", 0);
        }
    }
}
