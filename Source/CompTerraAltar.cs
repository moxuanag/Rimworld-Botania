using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉凝聚台（3×3 主体 + 四角基座）。
    ///
    /// 规矩：四个角的基座**缺一个就不工作**——这是文档第一部分 §12 定下的「结构感」，
    /// 平面上少一角，玩家一眼就能看出来自己没搭完。
    ///
    /// 材料从台面上取（玩家把魔力钢锭 / 精灵石英丢在台子上即可），
    /// 魔力从 8 格内最近的魔力源一次性扣 20,000。
    /// 先扣材料与魔力，再走 2,400 tick 的凝聚进度，完成时在台心生成一枚泰拉钢锭。
    ///
    /// 框架迁移（0.8.4 S3-b）：找魔力源 / 结算节流 / 存档键交给 <see cref="CompManaDevice"/>，
    /// 本文件只保留 pedestal 判定、材料需求（<see cref="TryGetNeed"/>）、仪式进度与 autoHaul。
    /// 祭坛是「开工时一次付清 + 长时间计时」的设备，所以覆写 <see cref="CompManaDevice.SettleOnce"/>：
    /// 付款与进度都由本处控制，不走基类「每次结算付一小笔」的模板。
    /// </summary>
    public class CompProperties_TerraAltar : CompProperties_ManaDevice
    {
        /// <summary>凝聚一枚锭需要的 tick 数。</summary>
        public int ticksPerIngot = 2400;

        public ThingDef ingredientA;
        public int countA = 1;
        public ThingDef ingredientB;
        public int countB = 1;
        public ThingDef product;
        public ThingDef pedestalDef;

        // ---- 本组件自己的存档键（都带基类的 saveKeyPrefix 前缀，避免同一件东西上多个组件平铺撞键） ----
        // 前缀 "botaniaTerraAltar" 与旧版键名完全一致，所以老档不需要任何 legacy 只读键就能读回。
        public string ProgressSaveKey => saveKeyPrefix + "Progress";
        public string RunningSaveKey => saveKeyPrefix + "Running";
        public string AutoHaulSaveKey => saveKeyPrefix + "AutoHaul";

        public CompProperties_TerraAltar()
        {
            compClass = typeof(CompTerraAltar);

            // 基类配置（全部沿用旧版数值）：
            //   searchRadius       = 8      取魔半径（旧 XML 同名字段，可直接沿用，无需改名）
            //   manaPerWork        = 20000  一条泰拉钢锭的魔力成本（旧字段 manaPerIngot，XML 需改名）
            //   workIntervalTicks  = 60     每 60 tick 推进一次进度（旧版 IsHashIntervalTick(60)）
            //   retryIntervalTicks = 60     缺料 / 缺魔力时同样每 60 tick 重试（旧版行为）
            //   manaTolerance      = 0.5    旧版找源门槛是 manaPerIngot - 0.5f
            searchRadius = 8f;
            manaPerWork = 20000f;
            workIntervalTicks = 60;
            retryIntervalTicks = 60;
            manaTolerance = 0.5f;
            saveKeyPrefix = "botaniaTerraAltar";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string err in base.ConfigErrors(parentDef))
            {
                yield return err;
            }
            if (product == null)
            {
                yield return "product 未设置";
            }
            if (pedestalDef == null)
            {
                yield return "pedestalDef 未设置";
            }
            if (ingredientA == null || ingredientB == null)
            {
                yield return "ingredientA / ingredientB 未设置";
            }
        }
    }

    public class CompTerraAltar : CompManaDevice
    {
        private int progress;
        private bool running;

        /// <summary>
        /// 是否允许搬运工给这座台子送料（玩家用 Gizmo 开关）。
        /// 真正的派活由 Hauling 工作里的 WorkGiver_TerraAltarDeliver 负责，这里只是许可位。
        /// </summary>
        private bool autoHaul = true;

        public bool AutoHaul => autoHaul;

        private Map Map => parent.Map;

        public CompProperties_TerraAltar Props => (CompProperties_TerraAltar)props;

        // ---- 基类要求的两个抽象钩子 ----
        // 整套周期已经被 SettleOnce 覆写，这里保留「开工条件 / 开工动作」的语义，
        // 由 TryStart 复用，避免同一套判定写两遍。

        /// <summary>能否开工：基座齐全 + 材料齐全（付款能力在 TryStart 里判断）。</summary>
        protected override bool WantsWork()
        {
            if (running)
            {
                return false;
            }
            if (MissingPedestals > 0)
            {
                return false;
            }
            return HasAllInputs();
        }

        /// <summary>开工动作：扣掉台面上的材料、进入 2400 tick 计时。付款已在 TryStart 里完成。</summary>
        protected override void DoWork(int ticks)
        {
            ConsumeFromPlatform(Props.ingredientA, Props.countA);
            ConsumeFromPlatform(Props.ingredientB, Props.countB);
            running = true;
            progress = 0;
        }

        /// <summary>
        /// 覆写整套周期：祭坛是「开工时一次性付款 + 长计时」的设备，
        /// 与基类「每次结算付一小笔」的模板不同，因此自己控制付款与进度，
        /// 只复用基类的计时调度（<see cref="ManaDeviceCore.ScheduleNext"/>）、
        /// 找源（<see cref="ManaDeviceCore.FindSource"/>）与存档。
        /// </summary>
        protected override bool SettleOnce()
        {
            bool worked;
            if (running)
            {
                // 旧版：每 60 tick progress += 60。
                progress += DeviceProps.workIntervalTicks;
                worked = true;
                if (progress >= Props.ticksPerIngot)
                {
                    FinishIngot();
                }
            }
            else
            {
                worked = TryStart();
            }
            Core.ScheduleNext(worked);
            return worked;
        }

        /// <summary>
        /// 开工：pedestal 齐 → 料齐（WantsWork，与旧版检查顺序一致）→ 找源付款 → 扣料 → 计时。
        /// 旧版付款细节：找源门槛是 cost − 0.5，找到后直接 DrainMana(cost)、抽到多少算多少（不校验实际抽到的量）。
        /// </summary>
        private bool TryStart()
        {
            if (!WantsWork())
            {
                return false;
            }
            IManaSource source = Core.FindSource(DeviceProps.manaPerWork - DeviceProps.manaTolerance);
            if (source == null)
            {
                return false;
            }
            source.DrainMana(DeviceProps.manaPerWork);
            DoWork(DeviceProps.workIntervalTicks);
            return true;
        }

        private bool HasAllInputs()
        {
            if (CountOnPlatform(Props.ingredientA) < Props.countA)
            {
                return false;
            }
            return CountOnPlatform(Props.ingredientB) >= Props.countB;
        }

        private void AppendHaulLine(StringBuilder sb)
        {
            if (!autoHaul)
            {
                return;
            }
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.Append("Botania.TerraAltarHaulOn".Translate());
        }

        /// <summary>四个对角基座所在的格子（几何见 TerraAltarCorners，放置预览用的是同一份）。</summary>
        public List<IntVec3> PedestalCells()
        {
            return TerraAltarCorners.Of(parent.OccupiedRect());
        }

        /// <summary>这一格上有没有基座。</summary>
        public bool HasPedestalAt(IntVec3 cell)
        {
            Map map = Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def == Props.pedestalDef && !things[i].Destroyed)
                {
                    return true;
                }
            }
            return false;
        }

        public int MissingPedestals
        {
            get
            {
                Map map = Map;
                if (map == null)
                {
                    return 4;
                }
                int missing = 0;
                List<IntVec3> cells = PedestalCells();
                for (int i = 0; i < cells.Count; i++)
                {
                    IntVec3 cell = cells[i];
                    if (!cell.InBounds(map))
                    {
                        missing++;
                        continue;
                    }
                    if (!HasPedestalAt(cell))
                    {
                        missing++;
                    }
                }
                return missing;
            }
        }

        /// <summary>台面（3×3 自身格）上某材料的总数（给搬运逻辑用）。</summary>
        public int CountOnPlatformPublic(ThingDef def)
        {
            return CountOnPlatform(def);
        }

        /// <summary>
        /// 台面还缺什么料：缺就返回 true，并给出材料 def 与缺口数量。
        /// 搬运 WorkGiver 与生产判定共用这一份口径。
        /// </summary>
        public bool TryGetNeed(out ThingDef def, out int count)
        {
            def = null;
            count = 0;
            if (Props.ingredientA != null)
            {
                int haveA = CountOnPlatform(Props.ingredientA);
                if (haveA < Props.countA)
                {
                    def = Props.ingredientA;
                    count = Props.countA - haveA;
                    return true;
                }
            }
            if (Props.ingredientB != null)
            {
                int haveB = CountOnPlatform(Props.ingredientB);
                if (haveB < Props.countB)
                {
                    def = Props.ingredientB;
                    count = Props.countB - haveB;
                    return true;
                }
            }
            return false;
        }

        /// <summary>台面（3×3 自身格）上某材料的总数。</summary>
        private int CountOnPlatform(ThingDef def)
        {
            if (def == null)
            {
                return 0;
            }
            Map map = Map;
            if (map == null)
            {
                return 0;
            }
            int total = 0;
            foreach (IntVec3 cell in parent.OccupiedRect())
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def == def && !things[i].Destroyed)
                    {
                        total += things[i].stackCount;
                    }
                }
            }
            return total;
        }

        private void ConsumeFromPlatform(ThingDef def, int count)
        {
            Map map = Map;
            if (map == null || def == null || count <= 0)
            {
                return;
            }
            int left = count;
            foreach (IntVec3 cell in parent.OccupiedRect())
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0 && left > 0; i--)
                {
                    Thing t = things[i];
                    if (t.def != def || t.Destroyed)
                    {
                        continue;
                    }
                    int take = left < t.stackCount ? left : t.stackCount;
                    left -= take;
                    if (take >= t.stackCount)
                    {
                        t.Destroy();
                    }
                    else
                    {
                        t.stackCount -= take;
                    }
                }
                if (left <= 0)
                {
                    break;
                }
            }
        }

        private void FinishIngot()
        {
            running = false;
            progress = 0;
            Map map = Map;
            if (map == null || Props.product == null)
            {
                return;
            }
            IntVec3 center = parent.OccupiedRect().CenterCell;
            if (!center.InBounds(map))
            {
                center = parent.Position;
            }
            Thing made = ThingMaker.MakeThing(Props.product);
            GenPlace.TryPlaceThing(made, center, map, ThingPlaceMode.Near);
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            if (running)
            {
                float pct = Props.ticksPerIngot <= 0 ? 1f
                    : progress / (float)Props.ticksPerIngot;
                sb.Append("Botania.TerraAltarWorking".Translate(pct.ToStringPercent()));
                return sb.ToString();
            }
            int missing = MissingPedestals;
            if (missing > 0)
            {
                sb.Append("Botania.TerraAltarNoPedestals".Translate(missing));
                AppendHaulLine(sb);
                return sb.ToString();
            }
            if (CountOnPlatform(Props.ingredientA) < Props.countA ||
                CountOnPlatform(Props.ingredientB) < Props.countB)
            {
                sb.Append("Botania.TerraAltarNeedInputs".Translate(
                    Props.ingredientA.label, Props.countA,
                    Props.ingredientB.label, Props.countB));
                AppendHaulLine(sb);
                return sb.ToString();
            }
            IManaSource source = Core.FindSource(DeviceProps.manaPerWork - DeviceProps.manaTolerance);
            if (source == null)
            {
                sb.Append("Botania.TerraAltarNoMana".Translate(
                    DeviceProps.manaPerWork.ToString("F0"), DeviceProps.searchRadius.ToString("F0")));
                AppendHaulLine(sb);
                return sb.ToString();
            }
            sb.Append("Botania.TerraAltarReady".Translate());
            AppendHaulLine(sb);
            return sb.ToString();
        }

        /// <summary>这一格上有没有「基座的蓝图」（还没盖起来也算占位）。</summary>
        private bool HasPedestalBlueprintAt(IntVec3 cell)
        {
            Map map = Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint bp && bp.def.entityDefToBuild == Props.pedestalDef)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 还「没安排上」几个角：既没有实心基座、也没有基座蓝图。
        /// 和 MissingPedestals 的区别：蓝图不算「能干活」（生产判定必须实心），
        /// 但按钮该不该变灰看的是这一条——否则放完蓝图按钮还亮着，点了什么也不发生。
        /// </summary>
        private int MissingOrUnplanned()
        {
            int n = 0;
            List<IntVec3> cells = PedestalCells();
            for (int i = 0; i < cells.Count; i++)
            {
                if (!HasPedestalAt(cells[i]) && !HasPedestalBlueprintAt(cells[i]))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// 选中凝聚台时的「补齐四角基座」按钮：直接在对角格放蓝图，
        /// 和原版「点建筑就给你把附属件摆好」的体验一致。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 旧版对非玩家阵营的祭坛不显示任何按钮；这里保持同样的可见性。
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            // 基类通用调试按钮（仅开发者模式）：立刻结算一次 / 跳过冷却。
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Botania.Dev.AltarGive".Translate(),
                    defaultDesc = "Botania.Dev.AltarGiveDesc".Translate(),
                    action = delegate
                    {
                        Map map = parent.Map;
                        if (map == null)
                        {
                            return;
                        }
                        if (!TryGetNeed(out ThingDef need, out int needCount) || need == null)
                        {
                            Messages.Message("Botania.Dev.AltarGiveNone".Translate(),
                                new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                            return;
                        }
                        Thing thing = ThingMaker.MakeThing(need);
                        if (thing == null)
                        {
                            return;
                        }
                        thing.stackCount = needCount;
                        GenPlace.TryPlaceThing(thing, parent.Position, map, ThingPlaceMode.Near);
                        Messages.Message("Botania.Dev.AltarGiveDone".Translate(need.LabelCap, needCount),
                            new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                    }
                };
            }
            Command_Action place = new Command_Action
            {
                defaultLabel = "Botania.TerraAltarPlacePedestals".Translate(),
                defaultDesc = "Botania.TerraAltarPlacePedestalsDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Botania_PlacePedestals"),
                action = PlaceMissingPedestals
            };
            if (MissingOrUnplanned() == 0)
            {
                place.Disable("Botania.TerraAltarPedestalsComplete".Translate());
            }
            yield return place;
            yield return MakeAutoHaulGizmo();
        }

        /// <summary>自动搬运开关（工作归搬运工，只在闲着的搬运工身上派活）。</summary>
        private Gizmo MakeAutoHaulGizmo()
        {
            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = "Botania.TerraAltarAutoHaul".Translate(),
                defaultDesc = "Botania.TerraAltarAutoHaulDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Botania_HaulToAltar"),
                isActive = () => autoHaul,
                toggleAction = delegate { autoHaul = !autoHaul; }
            };
            return toggle;
        }

        private void PlaceMissingPedestals()
        {
            Map map = Map;
            if (map == null || Props.pedestalDef == null)
            {
                return;
            }
            int placed = 0;
            int blocked = 0;
            List<IntVec3> cells = PedestalCells();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(map))
                {
                    blocked++;
                    continue;
                }
                if (HasPedestalAt(cell) || HasPedestalBlueprintAt(cell))
                {
                    continue;
                }
                AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                    Props.pedestalDef, cell, Rot4.North, map, false, null, null);
                if (!report.Accepted)
                {
                    blocked++;
                    continue;
                }
                GenConstruct.PlaceBlueprintForBuild(
                    Props.pedestalDef, cell, map, Rot4.North, Faction.OfPlayer, null);
                placed++;
            }
            if (placed > 0)
            {
                Messages.Message("Botania.TerraAltarPlacedPedestals".Translate(placed),
                    parent, MessageTypeDefOf.TaskCompletion, false);
            }
            if (blocked > 0)
            {
                Messages.Message("Botania.TerraAltarPedestalsBlocked".Translate(blocked),
                    parent, MessageTypeDefOf.RejectInput, false);
            }
        }

        public override void PostExposeData()
        {
            // 基类负责：botaniaTerraAltarHasMana / botaniaTerraAltarNextWorkTick
            base.PostExposeData();
            // 旧版键名就是 botaniaTerraAltar + Progress/Running/AutoHaul，
            // 这里用 saveKeyPrefix 拼出来完全一致，老档直接读回，无需 legacy 只读键。
            Scribe_Values.Look(ref progress, Props.ProgressSaveKey, 0);
            Scribe_Values.Look(ref running, Props.RunningSaveKey, false);
            Scribe_Values.Look(ref autoHaul, Props.AutoHaulSaveKey, true);
        }
    }
}
