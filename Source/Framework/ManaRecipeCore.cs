using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「配方执行器」建筑的公共配置。
    ///
    /// 框架思路（见 AGENTS.md §2）：转化台（白雏菊）这类
    /// 「找一种输入 → 花魔力 → 产出另一种物品」的建筑共用同一套周期，差别只在
    /// 「去哪找输入、算几个、转化具体做什么」。迁移前转化台自己抄了一遍
    /// 找物品 / 找魔力源 / 付款 / 节流 / 存档，这里统一。
    /// 注意：炉心蕙是**产魔**设备（走 ManaProducerCore），不适用本框架；
    /// 以后有同类「吃材料换产物」的建筑（例如泰拉祭坛若收敛成配方语义）优先复用本类。
    ///
    /// 与 <see cref="CompProperties_ManaDevice"/> 的关系：
    ///   · 找魔力源 / 付款（凑够才扣）/ 结算节流 / 存档 / 通用提示 全部沿用基类；
    ///   · 本类只补配方相关的三个配置：workTicks / inputSearchRadius / consumeInput；
    ///   · manaPerWork 已在基类里，本类不重复声明（XML 直接写基类字段名）。
    ///
    /// XML 新字段（旧字段改名见各设备迁移说明）：
    ///   workTicks         —— 一轮配方的周期（tick），替代旧 ticksPerConvert
    ///   inputSearchRadius —— 找输入的半径（格）
    ///   consumeInput      —— 转化时是否消耗输入
    /// </summary>
    public class CompProperties_ManaRecipeBuilding : CompProperties_ManaDevice
    {
        /// <summary>
        /// 一轮配方工作（找输入 → 付款 → 转化）的周期，单位 tick。
        /// 付款成功后的下一次结算间隔就是它。
        /// <para>
        /// 这是**唯一**的周期来源：XML 加载完成后会同步进基类的
        /// <see cref="CompProperties_ManaDevice.workIntervalTicks"/>（见 <see cref="ResolveReferences"/>），
        /// 节流仍然只由 <see cref="ManaDeviceCore"/> 一处负责。XML 里不要再另写 workIntervalTicks。
        /// </para>
        /// </summary>
        public int workTicks = 300;

        /// <summary>
        /// 找输入的半径（格）。按「建筑占用格向外扩 N 格」的正方形计算：
        /// 1 = 紧贴的相邻 8 格（含斜角），0 = 只看建筑自己占的格子。
        /// </summary>
        public float inputSearchRadius = 1f;

        /// <summary>
        /// 转化时是否消耗输入。false 用于「输入只是催化剂」的配方。
        /// 本类**不自动**扣输入：子类在 DoConvert 里按这个开关处理
        /// （或直接调 <see cref="CompManaRecipeBuilding.ConsumeInput"/> 帮助方法）。
        /// </summary>
        public bool consumeInput = true;

        /// <summary>输入搜索半径换算成的「外扩格数」（向上取整，负值按 0）。</summary>
        public int InputSearchExpand
        {
            get
            {
                if (inputSearchRadius <= 0f)
                {
                    return 0;
                }
                return (int)Math.Ceiling(inputSearchRadius);
            }
        }

        public CompProperties_ManaRecipeBuilding()
        {
            compClass = typeof(CompManaRecipeBuilding);
            // 本引擎存档把组件字段平铺在 Thing 节点下，键必须够独特；
            // 具体建筑请在自己的 props 构造里改成更精确的前缀。
            saveKeyPrefix = "botaniaManaRecipe";
        }

        /// <summary>
        /// XML 加载完成后把 workTicks 同步进基类的 workIntervalTicks。
        /// 必须等 XML 反序列化之后做（构造期 XML 还没读进来）。
        /// </summary>
        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            if (workTicks > 0)
            {
                workIntervalTicks = workTicks;
            }
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (workTicks <= 0)
            {
                yield return "workTicks must be greater than 0.";
            }
            if (inputSearchRadius < 0f)
            {
                yield return "inputSearchRadius must not be negative.";
            }
        }
    }

    /// <summary>
    /// 配方执行器基类。子类只需要回答三件事：
    ///   · <see cref="TryFindInput"/> —— 这一轮处理哪个输入（顺带扫图，一次结算只扫一遍）
    ///   · <see cref="CountForInput"/> —— 这一次处理几个（默认 1）
    ///   · <see cref="DoConvert"/>     —— 真正转化 / 产出（输入是否消耗也由它决定）
    ///
    /// 找魔力源、付款（凑够才扣）、结算节流、存档、魔力状态通知全部由
    /// <see cref="CompManaDevice"/> / <see cref="ManaDeviceCore"/> 负责。
    ///
    /// 铁律（见 AGENTS.md §5）：
    ///   · 节流只有 <see cref="ManaDeviceCore"/> 一处（ShouldWorkNow / ScheduleNext），子类不得再自建 tick 计数；
    ///   · 不在 PostSpawnSetup 里结算；
    ///   · 存档键带前缀，并兼容旧键。
    /// </summary>
    public abstract class CompManaRecipeBuilding : CompManaDevice
    {
        /// <summary>魔力状态通知去重（等价基类 SettleOnce 里的 lastReportedHasMana）。</summary>
        private bool? lastReportedHasMana;

        protected CompProperties_ManaRecipeBuilding RecipeProps => (CompProperties_ManaRecipeBuilding)props;

        /// <summary>
        /// 当前是否允许工作（开关、条件）。返回 false 时本轮不找输入、不付款、也不排程。
        /// </summary>
        protected virtual bool CanWork => true;

        /// <summary>
        /// 找这一轮要处理的输入。找到返回 true 并给出 input；没有就返回 false。
        /// 只在这里扫地图——同一轮不要重复扫。
        /// </summary>
        protected abstract bool TryFindInput(out Thing input);

        /// <summary>这一轮处理多少个（堆叠物品按规则数量）。基类会再兜底成 &gt;= 1。</summary>
        protected virtual int CountForInput(Thing input)
        {
            return 1;
        }

        /// <summary>
        /// 真正执行转化并产出结果。调用前魔力已扣除（或已判定这次免费转化）。
        /// 输入是否消耗由子类自己决定（可参考 <see cref="CompProperties_ManaRecipeBuilding.consumeInput"/>）。
        /// </summary>
        protected abstract void DoConvert(Thing input, int count);

        /// <summary>
        /// 魔力不足时是否照样转化一次。默认 false：不转化，按 retryIntervalTicks 等魔力。
        /// 白雏菊用它实现「断魔降速、且不消耗魔力」的独有语义（由子类覆写）。
        /// </summary>
        protected virtual bool ConvertWhenNoMana(Thing input, int count)
        {
            return false;
        }

        /// <summary>
        /// 本轮结束后的排程。默认：付款成功按 workTicks，其它情况按 retryIntervalTicks。
        /// 需要自定义节奏（例如断魔降速）的子类覆写这里——注意仍要经
        /// <see cref="ManaDeviceCore.ScheduleNext"/> 排程，不要自己记 tick。
        /// </summary>
        protected virtual void ScheduleNextCycle(bool foundInput, bool paid)
        {
            Core.ScheduleNext(paid);
        }

        /// <summary>
        /// 配方周期：找输入 → 付款 → 转化 → 排程。
        ///
        /// 为什么覆写基类的 SettleOnce：基类语义是「没付到钱就不干活」，
        /// 容不下白雏菊「断了魔也照样转化（慢速免费）」这条独有规则，
        /// 所以周期骨架放在这里，用 <see cref="ConvertWhenNoMana"/> 留出钩子。
        /// 节流仍然只有 <see cref="ManaDeviceCore"/> 一处。
        /// </summary>
        protected override bool SettleOnce()
        {
            if (!CanWork)
            {
                // 与迁移前的白雏菊一致：关掉时连计时都不推进（不排程，重新打开后马上结算一次）。
                return false;
            }

            Thing input;
            if (!TryFindInput(out input) || input == null)
            {
                ScheduleNextCycle(false, false);
                NotifyManaStateChanged();
                OnSettled(false);
                return false;
            }

            int count = Math.Max(1, CountForInput(input));
            bool paid = Core.TryPay(ManaPerWork);
            bool converted;
            if (paid)
            {
                DoConvert(input, count);
                converted = true;
            }
            else
            {
                // 断魔：默认什么都不做；白雏菊这类设备会在这里免费转化一次（慢速）
                converted = ConvertWhenNoMana(input, count);
                if (converted)
                {
                    DoConvert(input, count);
                }
            }
            ScheduleNextCycle(true, paid);
            NotifyManaStateChanged();
            OnSettled(converted);
            return converted;
        }

        /// <summary>
        /// 与基类 SettleOnce 的语义保持一致：只在「有魔力 ↔ 没魔力」切换时通知一次
        /// （首次结算也会通知一次），供灯光 / 贴图 / 电网输出等子类刷新。
        /// </summary>
        private void NotifyManaStateChanged()
        {
            if (lastReportedHasMana != Core.HasMana)
            {
                lastReportedHasMana = Core.HasMana;
                OnManaStateChanged(Core.HasMana);
            }
        }

        // 基类要求的两个抽象钩子：整套周期已被上面的 SettleOnce 接管，这里只做保守默认。
        protected override bool WantsWork()
        {
            return false;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>
        /// 按 <see cref="CompProperties_ManaRecipeBuilding.consumeInput"/> 批量消耗输入。
        /// 子类若在 DoConvert 里自己消耗（例如复用 ManaTransmutation），不要重复调用。
        /// </summary>
        protected bool ConsumeInput(Thing input, int count)
        {
            if (!RecipeProps.consumeInput || input == null || input.Destroyed || count <= 0)
            {
                return false;
            }
            int amount = Math.Min(count, input.stackCount);
            if (amount <= 0)
            {
                return false;
            }
            input.SplitOff(amount).Destroy(DestroyMode.Vanish);
            return true;
        }

        /// <summary>
        /// 按配置的 inputSearchRadius 遍历输入候选格（建筑占用格外扩 N 格，含斜角）。
        /// 顺序与旧版转化台一致：先按格子、再按格内物品。子类在 TryFindInput 里用它。
        /// </summary>
        protected IEnumerable<IntVec3> InputSearchCells()
        {
            Map map = parent.Map;
            if (map == null)
            {
                yield break;
            }
            foreach (IntVec3 cell in parent.OccupiedRect().ExpandedBy(RecipeProps.InputSearchExpand))
            {
                if (cell.InBounds(map))
                {
                    yield return cell;
                }
            }
        }
    }
}
