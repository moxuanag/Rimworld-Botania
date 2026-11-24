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

    /// <summary>
    /// 精灵门的配置。框架迁移（0.8.4 S3-a）：改为继承 <see cref="CompProperties_ManaDevice"/>。
    ///
    /// XML 需要跟着改的字段（本类不自己改 Def，交给 XML）：
    ///   · 旧 <c>manaSearchRadius</c> → 基类字段 <c>searchRadius</c>。
    ///     不改名的话引擎会打红字日志「XML error: &lt;manaSearchRadius&gt; doesn't correspond to any
    ///     field in type CompProperties_ManaGate」并忽略该值（默认 8 格刚好和 XML 的 8 相同，暂时看不出问题，
    ///     但以后在 XML 里调半径就不生效了）。
    ///   · 不要写 <c>workIntervalTicks</c> / <c>retryIntervalTicks</c>：
    ///     本门是**逐 tick 推进进度**，两者在构造里固定为 1，XML 覆盖成别的值会拖慢/打乱进度。
    /// 其余字段（rules / openingDepth / framePillarDefs / framePillarHeight）名字与语义都不变。
    /// </summary>
    public class CompProperties_ManaGate : CompProperties_ManaDevice
    {
        public List<ManaGateRule> rules = new List<ManaGateRule>();

        /// <summary>门洞在建筑下方几格（上梁是 3×1，门洞 3×2 → 填 2）。</summary>
        public int openingDepth = 2;

        /// <summary>
        /// 立柱的 ThingDef 列表（左右柱可以是两个不同的 Def）。
        /// 非空时，上梁两端下方必须各有一列立柱，否则门不工作。
        /// </summary>
        public List<ThingDef> framePillarDefs = new List<ThingDef>();

        /// <summary>立柱高度（格），默认 3。</summary>
        public int framePillarHeight = 3;

        /// <summary>
        /// 专用存档键（带 <see cref="CompProperties_ManaDevice.saveKeyPrefix"/> 前缀）。
        /// 本引擎把组件字段平铺在 Thing 节点下，不带前缀会跟同物件其它组件撞键。
        /// 这三个键名与迁移前**逐字相同**（botaniaGateEnabled / botaniaGateRule / botaniaGateProgress），
        /// 所以老档直接读回，不需要 legacy 旧键。
        /// </summary>
        public string EnabledSaveKey => saveKeyPrefix + "Enabled";

        public string RuleSaveKey => saveKeyPrefix + "Rule";

        public string ProgressSaveKey => saveKeyPrefix + "Progress";

        public CompProperties_ManaGate()
        {
            compClass = typeof(CompManaGate);

            // 旧版 CompTick 每 tick 推进 1 点进度；框架里用「下一 tick 结算」表达同一节奏。
            // 成功/失败同间隔，保持「材料在就稳定推进」的旧行为（不要叠加 IsHashIntervalTick，
            // 节流只由 ManaDeviceCore 一处负责）。
            workIntervalTicks = 1;
            retryIntervalTicks = 1;

            searchRadius = 8f;       // 旧字段 manaSearchRadius 的默认值
            manaTolerance = 0.01f;   // 旧版「抽不满就还回去」的判定阈值
            saveKeyPrefix = "botaniaGate";
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
    ///
    /// 框架迁移（0.8.4 S3-a）：props 继承 <see cref="CompProperties_ManaDevice"/>，comp 继承
    /// <see cref="CompManaDevice"/>；计时调度（<see cref="ManaDeviceCore.ShouldWorkNow"/> /
    /// <see cref="ManaDeviceCore.ScheduleNext"/>）、找魔力源（<see cref="ManaDeviceCore.FindSource"/>）、
    /// 存档前缀交给基类。本文件只保留精灵门独有的账：
    /// 「门框是否完整 → 门洞里是什么材料 → 逐 tick 推进 → 完成时整笔付魔力 → 吞材料出货」。
    /// 旧版每 tick 跑一次 CompTick，现在等价于 workIntervalTicks = retryIntervalTicks = 1，
    /// 由基类每 tick 调一次 <see cref="SettleOnce"/>，推进节奏逐点不变。
    /// </summary>
    public class CompManaGate : CompManaDevice
    {
        private int ruleIndex = -1;
        private int progress;
        private bool enabled = true;

        // 旧状态机字段语义原样保留：inputReady / manaReady / frameReady 每 tick 由结算刷新，
        // 其中 manaReady 供检视栏判断「转满但没魔力」；inputReady / frameReady 目前只写不读。
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

        /// <summary>
        /// 覆写整套周期：精灵门是「逐 tick 推进进度、完成那一刻整笔硬性付款」，
        /// 与基类的「按固定间隔、固定额、凑够才扣」节奏不同，因此自己控制付款，
        /// 只复用基类的计时调度（<see cref="ManaDeviceCore.ScheduleNext"/>）、
        /// 找源（<see cref="ManaDeviceCore.FindSource"/>）与存档。
        /// </summary>
        protected override bool SettleOnce()
        {
            bool completed = TickGate();
            // 成功/失败间隔都是 1 tick（构造里设死），与旧版「每 tick 走一次」逐 tick 等价。
            Core.ScheduleNext(completed);
            return completed;
        }

        // 基类要求实现的两个抽象钩子：整套周期已被 SettleOnce 覆写，这里只做保守默认。
        protected override bool WantsWork()
        {
            return enabled;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>
        /// 一次「结算」= 旧版的一个 CompTick。返回值表示这一次是否真的产出了成品。
        /// 该方法只做状态推进，不重复判节流（节流只在基类 ShouldWorkNow 一处）。
        /// </summary>
        private bool TickGate()
        {
            if (parent == null || !parent.Spawned || !enabled)
            {
                return false;
            }
            Map map = parent.Map;
            if (map == null)
            {
                return false;
            }

            // 门框不完整（缺立柱）就完全不工作，避免只放一根上梁就能白嫖
            frameReady = FrameComplete(map);
            if (!frameReady)
            {
                ruleIndex = -1;
                progress = 0;
                return false;
            }

            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                int found = FindRule(map);
                inputReady = found >= 0;
                manaReady = false;
                Core.SetHasMana(false);
                if (found < 0)
                {
                    return false;
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
                return false;
            }
            inputReady = true;

            progress++;
            if (progress < rule.workTicks)
            {
                // 只用于检视栏（转满前先探一下附近有没有够付的源），不扣魔力
                manaReady = HasMana(rule);
                Core.SetHasMana(manaReady);
                return false;
            }

            // 转满了：付魔力 → 吃材料 → 出成品
            if (!ConsumeMana(rule))
            {
                manaReady = false;
                Core.SetHasMana(false);
                progress = rule.workTicks;   // 停在 100%，等魔力，不丢进度
                return false;
            }
            manaReady = true;
            Core.SetHasMana(true);
            ConsumeInputs(map, rule);
            Produce(map, rule);
            ruleIndex = -1;
            progress = 0;
            return true;
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
        /// <summary>附近有没有一个「现在就付得起这笔魔力」的源（不扣款）。</summary>
        private bool HasMana(ManaGateRule rule)
        {
            return Core.FindSource(rule.manaCost) != null;
        }

        /// <summary>
        /// 整笔付款：抽不到足额就退回，不留下「付了一半」的糊涂账。
        ///
        /// 这里没有用 <see cref="ManaDeviceCore.TryPay"/>：它的语义是「抽到多少扣多少」，
        /// 部分抽取时不退款；精灵门旧行为是「整笔硬性、抽不满就还回去」，所以沿用
        /// 修复器/明心莲那套显式账（FindSource → DrainMana → 不足退回），只把找源换成框架口。
        /// </summary>
        private bool ConsumeMana(ManaGateRule rule)
        {
            if (rule.manaCost <= 0f)
            {
                return true;
            }
            IManaSource source = Core.FindSource(rule.manaCost);
            if (source == null)
            {
                return false;
            }
            float drained = source.DrainMana(rule.manaCost);
            if (drained < rule.manaCost - Props.manaTolerance)
            {
                // 抽不满就还回去，别让玩家白亏一半
                source.AddMana(drained);
                return false;
            }
            return true;
        }

        /// <summary>状态栏 / 提示里用的规则名：优先 label，其次产物 label。</summary>
        private static string RuleName(ManaGateRule rule)
        {
            return rule.label.NullOrEmpty() ? rule.toDef.LabelCap.ToString() : rule.label;
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
            if (Props.framePillarDefs != null && Props.framePillarDefs.Count > 0 && !FrameComplete(parent.Map))
            {
                return "Botania.GateFrameIncomplete".Translate();
            }
            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                return "Botania.GateNoInput".Translate();
            }
            string name = RuleName(rule);
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

            // ---- 精灵门专用调试按钮（仅开发者模式）----
            // 基类 CompManaDevice 已不再提供「立刻结算一次 / 跳过冷却」这类通用按钮；
            // 这里只放精灵门自己的三个真实路径按钮。Preference 门控与其它迁移设备一致。
            if (!Prefs.DevMode)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.GateComplete".Translate(),
                defaultDesc = "Botania.Dev.GateCompleteDesc".Translate(),
                action = DebugCompleteTransmission
            };
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.GateGive".Translate(),
                defaultDesc = "Botania.Dev.GateGiveDesc".Translate(),
                action = DebugGiveInputs
            };
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.GateFillMana".Translate(),
                defaultDesc = "Botania.Dev.GateFillManaDesc".Translate(),
                action = DebugFillNearestManaSource
            };
        }

        // ── 调试工具实现 ────────────────────────────────────────────
        /// <summary>
        /// 开发者工具：把当前传送的进度直接推到「完成」边界。
        ///
        /// 安全性说明：这里**不**跳过付款/吃材料/产出，也不直接写 progress——
        /// 而是循环调用真实状态机入口 <see cref="SettleOnce"/>（= 正常每 tick 的那一次
        /// TickGate），由状态机自己一 tick 一 tick 把进度走满：门框复核、材料复核、
        /// 付款 <see cref="ConsumeMana"/>、扣料 <see cref="ConsumeInputs"/>、产出
        /// <see cref="Produce"/> 全部还是原状态机那条路径，所以不会出现「没付魔力白拿产物」
        /// 或「材料没扣却出成品」的脏状态。
        /// 魔力不足时状态机会自己停在 100% 等魔力，材料保留。
        /// </summary>
        private void DebugCompleteTransmission()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            if (!enabled || !FrameComplete(map))
            {
                Messages.Message("Botania.Dev.GateCompleteNoTarget".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                int found = FindRule(map);
                inputReady = found >= 0;
                if (found < 0)
                {
                    Messages.Message("Botania.Dev.GateCompleteNoTarget".Translate(),
                        new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                rule = Props.rules[found];
            }
            if (!HasAllInputs(map, rule))
            {
                Messages.Message("Botania.Dev.GateCompleteNoTarget".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            inputReady = true;

            // 真实状态机快进：每一轮 SettleOnce 只推进 1 点进度，完成那一刻才付款/扣料/产出。
            bool completed = false;
            for (int i = 0; i < rule.workTicks; i++)
            {
                if (SettleOnce())
                {
                    completed = true;
                    break;
                }
                // 状态机把进度重置了（材料被搬走 / 规则失效 / 门框变化）或已停在 100% 等魔力，
                // 别继续空转重试。
                if (ActiveRule != rule || progress >= rule.workTicks)
                {
                    break;
                }
            }
            if (completed)
            {
                Messages.Message("Botania.Dev.GateCompleteDone".Translate(RuleName(rule)),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("Botania.Dev.GateCompleteNoMana".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>开发者工具：按当前（没有则第一条）规则，往门洞里生成一份所需材料。</summary>
        private void DebugGiveInputs()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            ManaGateRule rule = ActiveRule;
            if (rule == null)
            {
                rule = FirstRule();
            }
            if (rule == null || rule.inputs == null || rule.inputs.Count == 0)
            {
                Messages.Message("Botania.Dev.GateGiveNone".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            // 优先丢进真正的门洞，保证下一个 tick 门能直接识别到材料。
            IntVec3 cell = parent.Position;
            foreach (IntVec3 c in OpeningCells())
            {
                if (c.InBounds(map))
                {
                    cell = c;
                    break;
                }
            }
            int placed = 0;
            for (int i = 0; i < rule.inputs.Count; i++)
            {
                ManaGateIngredient ing = rule.inputs[i];
                if (ing == null)
                {
                    continue;
                }
                ThingDef def = ing.fromDef;
                if (def == null)
                {
                    def = FirstItemDefInCategory(ing.fromCategory);
                }
                if (def == null)
                {
                    continue;
                }
                Thing thing = ThingMaker.MakeThing(def, null);
                if (thing == null)
                {
                    continue;
                }
                int count = Math.Max(1, ing.count);
                thing.stackCount = count;
                if (GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
                {
                    placed += count;
                }
            }
            if (placed > 0)
            {
                Messages.Message("Botania.Dev.GateGiveDone".Translate(RuleName(rule)),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("Botania.Dev.GateGiveNone".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private ManaGateRule FirstRule()
        {
            if (Props.rules == null)
            {
                return null;
            }
            for (int i = 0; i < Props.rules.Count; i++)
            {
                if (Props.rules[i] != null)
                {
                    return Props.rules[i];
                }
            }
            return null;
        }

        /// <summary>按类别取一个实际物品（取加载顺序里第一个可堆叠的物品 Def）。</summary>
        private static ThingDef FirstItemDefInCategory(ThingCategoryDef category)
        {
            if (category == null)
            {
                return null;
            }
            foreach (ThingDef def in category.DescendantThingDefs)
            {
                if (def != null && def.category == ThingCategory.Item && def.stackLimit > 0)
                {
                    return def;
                }
            }
            return null;
        }

        /// <summary>
        /// 开发者工具：把附近最近的一个魔力源注满。
        /// 优先「魔力池」，没有池子才退化为最近的中继塔；即使池子是空的也照样能选中
        /// （ManaSourceUtil.FindNearest 会排除空源，调试按钮需要反着来）。
        /// </summary>
        private void DebugFillNearestManaSource()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            IManaSource target = null;
            IManaSource fallback = null;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.searchRadius, true))
            {
                CompManaStorage pool = thing.TryGetComp<CompManaStorage>();
                if (pool != null)
                {
                    target = pool;
                    break;
                }
                if (fallback == null)
                {
                    fallback = ManaSourceUtil.AsSource(thing);
                }
            }
            if (target == null)
            {
                target = fallback;
            }
            if (target == null)
            {
                Messages.Message("Botania.Dev.GateFillManaNone".Translate(Props.searchRadius.ToString("F0")),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            float accepted = target.AddMana(target.FreeSpace);
            if (accepted <= 0.01f)
            {
                Messages.Message("Botania.Dev.GateFillManaFull".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("Botania.Dev.GateFillManaDone".Translate(accepted.ToString("F0")),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        public override void PostExposeData()
        {
            // 基类先写框架字段（botaniaGateHasMana / botaniaGateNextWorkTick）。
            base.PostExposeData();
            // 门自己的三个键：名字与迁移前逐字一致，老档可直接读回（无 legacy 键）。
            Scribe_Values.Look(ref enabled, Props.EnabledSaveKey, true);
            Scribe_Values.Look(ref ruleIndex, Props.RuleSaveKey, -1);
            Scribe_Values.Look(ref progress, Props.ProgressSaveKey, 0);
        }
    }
}
