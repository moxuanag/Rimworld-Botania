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

    /// <summary>
    /// 白雏菊（转化台）的配置。
    ///
    /// 框架迁移（0.8.4 S3-b）：props 继承 <see cref="CompProperties_ManaRecipeBuilding"/>，
    /// comp 继承 <see cref="CompManaRecipeBuilding"/>。找输入 / 付款 / 节流 / 存档 / 产出流程交给框架，
    /// 本文件只保留白雏菊独有的两样东西：
    ///   1. 转化规则表 <see cref="rules"/> 与「物品 → 规则」匹配；
    ///   2. 断魔降速：附近没有可用魔力网络（没有池子 / 中继塔，或它们都空了）时
    ///      仍然转化，但间隔 × <see cref="noManaSlowdown"/>，且不消耗魔力。
    ///
    /// XML 字段改名（XML 由后续步骤统一改，本次不动 XML）：
    ///   ticksPerConvert  → workTicks       （基类）
    ///   manaPerConvert   → manaPerWork     （基类）
    ///   manaSearchRadius → searchRadius    （基类）
    ///   noManaSlowdown / rules 名字不变。
    /// </summary>
    public class CompProperties_ManaTransmuter : CompProperties_ManaRecipeBuilding
    {
        /// <summary>
        /// 附近没有可用魔力网络（没有池子/中继塔，或它们都空了）时，
        /// 转化间隔乘以这个倍数，并且不消耗魔力。
        /// </summary>
        public int noManaSlowdown = 5;

        /// <summary>转化规则表。</summary>
        public List<ManaConversionRule> rules = new List<ManaConversionRule>();

        /// <summary>慢速（断魔免费）转化的间隔：workTicks × noManaSlowdown。</summary>
        public int SlowIntervalTicks => Math.Max(1, workTicks * Math.Max(1, noManaSlowdown));

        public CompProperties_ManaTransmuter()
        {
            compClass = typeof(CompManaTransmuter);

            // 存档键前缀（本引擎组件字段平铺在 Thing 节点下，必须带前缀）
            saveKeyPrefix = "botaniaTransmuter";

            // 基类配置：数值与迁移前逐项一致
            workTicks = 300;          // 旧 ticksPerConvert
            manaPerWork = 20f;        // 旧 manaPerConvert
            searchRadius = 6f;        // 旧 manaSearchRadius
            inputSearchRadius = 1f;   // 旧实现写死的 OccupiedRect().ExpandedBy(1)
            // 「没付到钱」的那一轮 = 断魔降速免费转化，下一轮按慢速间隔；
            // 排程仍然只经基类 ManaDeviceCore.ScheduleNext（节流只一处）。
            retryIntervalTicks = SlowIntervalTicks;
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            // workTicks 可能被 XML 改过，慢速间隔要跟着重算
            retryIntervalTicks = SlowIntervalTicks;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (noManaSlowdown < 1)
            {
                yield return "noManaSlowdown must be at least 1.";
            }
            if (!consumeInput)
            {
                yield return "CompProperties_ManaTransmuter always consumes its input; consumeInput=false is not supported.";
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
    ///   · 附近有可用的魔力网络 → 按 workTicks 全速转化，每次扣 manaPerWork 魔力；
    ///   · 附近没有可用网络（没池子，或池子全空）→ 仍然转化，但间隔 × noManaSlowdown，
    ///     且不消耗魔力。这保证开局没有魔力池时也能起量，只是慢得多。
    ///
    /// 框架迁移（0.8.4 S3-b）：找输入（TryFindInput）/ 周期（SettleOnce）/ 付款 / 存档交给
    /// <see cref="CompManaRecipeBuilding"/>；本文件只保留规则匹配、「断魔也转化」的钩子、
    /// 慢速排程（<see cref="ScheduleNextCycle"/>）、检视文案与开关 gizmo。
    /// 只有一条节流：基类 <see cref="ManaDeviceCore"/> 的 nextWorkTick。
    /// </summary>
    public class CompManaTransmuter : CompManaRecipeBuilding
    {
        private bool enabled = true;

        /// <summary>上一轮是否找到了可转化的输入（只用于检视文案，等价旧版的 hasInput）。</summary>
        private bool hasInput;

        /// <summary>
        /// 上一轮是否是「断魔降速」的免费转化。
        /// 旧实现里 currentInterval 只在转化后更新，没有输入时沿用上一次的间隔；
        /// 这里用这个标记复现同一节奏（不参与节流，只决定下一次排程用哪个间隔）。
        /// </summary>
        private bool lastCycleWasSlow;

        /// <summary>本轮匹配到的规则（TryFindInput 找到输入时记下，DoConvert 用）。</summary>
        private ManaConversionRule pendingRule;

        public CompProperties_ManaTransmuter Props => (CompProperties_ManaTransmuter)props;

        public bool Enabled => enabled;

        /// <summary>关闭后停下：不找输入、不付款、不转化。</summary>
        protected override bool CanWork => enabled;

        /// <summary>
        /// 找相邻格（外扩 1 格，含斜角）里第一个可转化的物品：
        ///   · 必须是地上的物品（容器 / 角色身上的不动）；
        ///   · 玩家手动禁用的物品不动，避免偷走不该动的库存；
        ///   · 必须匹配规则表，且堆叠数够一次转化。
        /// </summary>
        protected override bool TryFindInput(out Thing input)
        {
            input = null;
            pendingRule = null;
            hasInput = false;

            Map map = parent.Map;
            if (map == null)
            {
                return false;
            }

            foreach (IntVec3 cell in InputSearchCells())
            {
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
                    pendingRule = rule;
                    hasInput = true;
                    input = thing;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按匹配到的规则数量处理（与旧版 TryConvert 的 rule.count 一致）。</summary>
        protected override int CountForInput(Thing input)
        {
            return pendingRule != null ? pendingRule.count : 1;
        }

        /// <summary>
        /// 白雏菊独有行为：没有可用魔力网络（或池子空）时，仍然转化一次（慢速、不耗魔）。
        /// </summary>
        protected override bool ConvertWhenNoMana(Thing input, int count)
        {
            return true;
        }

        /// <summary>
        /// 执行转化：复用 <see cref="ManaTransmutation.TryConvert(Thing, ManaConversionRule, int)"/>，
        /// 与旧版完全同一条代码路径（split → destroy → 在输入格生成产出）。
        /// </summary>
        protected override void DoConvert(Thing input, int count)
        {
            ManaConversionRule rule = pendingRule;
            if (rule == null)
            {
                return;
            }
            ManaTransmutation.TryConvert(input, rule, count);
        }

        /// <summary>
        /// 排程（仍然只经基类的 ManaDeviceCore.ScheduleNext，不自建 tick）：
        ///   · 有输入 + 付到钱 → 正常间隔 workTicks；
        ///   · 有输入 + 没付到钱 → 慢速间隔（基类 retryIntervalTicks = workTicks × noManaSlowdown）；
        ///   · 没有输入 → 沿用上一轮节奏（旧版 currentInterval 不变的行为）。
        /// </summary>
        protected override void ScheduleNextCycle(bool foundInput, bool paid)
        {
            if (foundInput)
            {
                lastCycleWasSlow = !paid;
                Core.ScheduleNext(paid);
                return;
            }
            Core.ScheduleNext(!lastCycleWasSlow);
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
            if (!Core.HasMana)
            {
                return "Botania.TransmuterSlow".Translate(Props.noManaSlowdown);
            }
            return "Botania.TransmuterWorking".Translate();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 基类已不再提供通用调试按钮；调试按钮见下方（仅开发者模式）
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

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Botania.Dev.TransmuterGive".Translate(),
                    defaultDesc = "Botania.Dev.TransmuterGiveDesc".Translate(),
                    action = delegate
                    {
                        Map map = parent.Map;
                        if (map == null || Props.rules == null)
                        {
                            return;
                        }
                        for (int i = 0; i < Props.rules.Count; i++)
                        {
                            ManaConversionRule rule = Props.rules[i];
                            if (rule == null || rule.fromDef == null)
                            {
                                continue;
                            }
                            Thing thing = ThingMaker.MakeThing(rule.fromDef);
                            if (thing == null)
                            {
                                continue;
                            }
                            thing.stackCount = Math.Max(1, rule.count);
                            GenPlace.TryPlaceThing(thing, parent.Position, map, ThingPlaceMode.Near);
                            Messages.Message("Botania.Dev.TransmuterGiven".Translate(rule.fromDef.LabelCap),
                                new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                            return;
                        }
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Botania.Dev.TransmuterConvert".Translate(),
                    defaultDesc = "Botania.Dev.TransmuterConvertDesc".Translate(),
                    action = DebugConvertAdjacent
                };
            }
        }

        /// <summary>
        /// 开发者工具：把相邻格的输入材料真实转化一次。
        ///
        /// 不直接生成产物，而是走两条真实入口：
        ///   1. <see cref="TryFindInput"/> —— 与每轮结算同一个扫描（同一套过滤 + 规则匹配），
        ///      顺带把匹配到的规则记进 pendingRule；
        ///   2. <see cref="CompManaRecipeBuilding.SettleOnce"/> —— 框架的真实周期：
        ///      有魔力就凑够才扣（<see cref="ManaDeviceCore.TryPay"/>），断魔则按白雏菊的
        ///      真实语义「慢速免费转化」；最终产出仍由 <see cref="DoConvert"/> 调用
        ///      <see cref="ManaTransmutation.TryConvert"/> 完成（split → destroy → 生成产出）。
        /// </summary>
        private void DebugConvertAdjacent()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            Thing probe;
            if (!enabled || !TryFindInput(out probe) || probe == null)
            {
                Messages.Message("Botania.Dev.TransmuterConvertNone".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            ManaConversionRule rule = pendingRule;
            bool converted = SettleOnce();
            if (converted && rule != null && rule.toDef != null)
            {
                Messages.Message("Botania.Dev.TransmuterConvertDone".Translate(
                        rule.toDef.LabelCap, Math.Max(1, rule.count)),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("Botania.Dev.TransmuterConvertNone".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        public override void PostExposeData()
        {
            // 基类（ManaDeviceCore）负责：botaniaTransmuterHasMana / botaniaTransmuterNextWorkTick
            base.PostExposeData();

            bool savedEnabled = enabled;
            bool savedSlow = lastCycleWasSlow;
            Scribe_Values.Look(ref savedEnabled, "botaniaTransmuterEnabled", true);
            Scribe_Values.Look(ref savedSlow, "botaniaTransmuterSlowCycle", false);
            enabled = savedEnabled;
            lastCycleWasSlow = savedSlow;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 旧存档键（迁移前没有前缀，直接平铺在 Thing 节点下）：只读兼容，不回写。
                Scribe_Values.Look(ref enabled, "enabled", enabled);

                // 旧版只存 currentInterval（当前转化间隔）；ticksAccum 的进度折算不进基类的绝对
                // nextWorkTick（该字段是 ManaDeviceCore 私有），读旧档后最多提前一个周期结算一次。
                int legacyInterval = 0;
                Scribe_Values.Look(ref legacyInterval, "currentInterval", 0);
                if (legacyInterval > 0)
                {
                    lastCycleWasSlow = legacyInterval > Math.Max(1, Props.workTicks);
                }
            }
        }
    }
}
