using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力设备的公共配置。
    ///
    /// 框架思路：所有「要花魔力才能干活」的设备（灯、修复器、通量场、功能花……）
    /// 共用同一套语义——按固定间隔结算、凑够才扣、断魔就停工、来魔自动恢复。
    /// 以前这 10 个文件各自抄了一遍找源/付款/节流/存档，这里统一。
    ///
    /// 用法：让具体设备的 CompProperties 继承本类（XML 里的 compClass 不用改），
    /// 设备 Comp 要么继承 <see cref="CompManaDevice"/>，要么持有一个 <see cref="ManaDeviceCore"/>。
    /// 后者用于父类必须是原版 Comp（例如 CompPowerTrader）的设备。
    /// </summary>
    /// <summary>
    /// 设备付款行为的配置接口。
    ///
    /// 为什么需要接口：有些设备的 CompProperties 必须继承原版类型
    /// （例如通量场要继承 <c>CompProperties_Power</c> 才能接电网），
    /// 无法同时继承 <see cref="CompProperties_ManaDevice"/>。
    /// 让这类 props 实现本接口，<see cref="ManaDeviceCore"/> 就能照常复用。
    /// </summary>
    public interface IManaDeviceConfig
    {
        float SearchRadius { get; }
        float ManaPerWork { get; }
        int WorkIntervalTicks { get; }
        int RetryIntervalTicks { get; }
        float ManaTolerance { get; }
        string WorkingKey { get; }
        string NoManaKey { get; }
        string HasManaSaveKey { get; }
        string NextWorkSaveKey { get; }
        string LegacyHasManaKey { get; }
        string LegacyNextWorkKey { get; }
    }

    public class CompProperties_ManaDevice : CompProperties, IManaDeviceConfig
    {
        /// <summary>寻找魔力源（魔力池 / 中继塔）的半径。</summary>
        public float searchRadius = 8f;

        /// <summary>每次结算需要支付的魔力量。</summary>
        public float manaPerWork = 1f;

        /// <summary>正常结算间隔（tick）。</summary>
        public int workIntervalTicks = 3600;

        /// <summary>魔力不足时的重试间隔（tick），比正常间隔短，来魔后能很快恢复。</summary>
        public int retryIntervalTicks = 300;

        /// <summary>允许的魔力误差（浮点抖动），低于此值视为「刚好够」。</summary>
        public float manaTolerance = 0.01f;

        /// <summary>「工作中」提示文案的 Keyed 键；null 用通用键。</summary>
        public string inspectWorkingKey;

        /// <summary>「缺魔力」提示文案的 Keyed 键；null 用通用键。</summary>
        public string inspectNoManaKey;

        /// <summary>
        /// 存档键前缀。本引擎的存档把组件字段**平铺在 Thing 节点下**（不是按类名分组），
        /// 所以键必须够独特，避免同一件东西上多个组件撞键。
        /// </summary>
        public string saveKeyPrefix = "botaniaManaDevice";

        /// <summary>
        /// 旧版存档键（兼容用）：老组件用过的字段名，迁移后仍能读回旧值。
        /// 为空则只读写 <see cref="saveKeyPrefix"/> + 后缀。
        /// </summary>
        public string legacyHasManaKey;
        public string legacyNextWorkKey;

        public CompProperties_ManaDevice()
        {
            compClass = typeof(CompManaDevice);
        }

        public float SearchRadius => searchRadius;
        public float ManaPerWork => manaPerWork;
        public int WorkIntervalTicks => workIntervalTicks;
        public int RetryIntervalTicks => retryIntervalTicks;
        public float ManaTolerance => manaTolerance;
        public string LegacyHasManaKey => legacyHasManaKey;
        public string LegacyNextWorkKey => legacyNextWorkKey;
        public string HasManaSaveKey => saveKeyPrefix + "HasMana";
        public string NextWorkSaveKey => saveKeyPrefix + "NextWorkTick";
        public string WorkingKey => inspectWorkingKey ?? "Botania.DeviceWorking";
        public string NoManaKey => inspectNoManaKey ?? "Botania.DeviceNoMana";

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerWork < 0f)
            {
                yield return "manaPerWork must not be negative.";
            }
            if (workIntervalTicks <= 0)
            {
                yield return "workIntervalTicks must be greater than 0.";
            }
            if (retryIntervalTicks <= 0)
            {
                yield return "retryIntervalTicks must be greater than 0.";
            }
            if (searchRadius < 0f)
            {
                yield return "searchRadius must not be negative.";
            }
            if (saveKeyPrefix.NullOrEmpty())
            {
                yield return "saveKeyPrefix must not be empty (save fields are flat per Thing).";
            }
        }
    }

    /// <summary>
    /// 魔力设备的核心状态机（刻意不是 ThingComp，方便组合进任何父类）。
    /// 负责：计时节流 / 找最近魔力源 / 凑够才扣 / 存档 / 提示文案。
    /// </summary>
    public class ManaDeviceCore
    {
        private readonly ThingComp owner;
        private readonly IManaDeviceConfig props;
        private int nextWorkTick;

        public ManaDeviceCore(ThingComp owner, IManaDeviceConfig props)
        {
            this.owner = owner;
            this.props = props;
        }

        public IManaDeviceConfig Props => props;

        /// <summary>上一次结算是否付得起魔力。断魔时为 false。</summary>
        public bool HasMana { get; private set; } = true;

        /// <summary>
        /// 开发调试：把下一次结算时刻清零（相当于「清除冷却」）。
        /// 基类<b>不再</b>显示通用调试按钮；需要时由子类在它自己的专用调试按钮里调用本方法。
        /// </summary>
        public void DebugSkipCooldown()
        {
            nextWorkTick = 0;
        }

        /// <summary>由外部（组合式设备）直接设定当前是否有魔力，例如玩家手动关灯。</summary>
        public void SetHasMana(bool value)
        {
            HasMana = value;
        }

        private Thing Parent => owner?.parent;

        /// <summary>
        /// 每 tick 调用，返回 true 表示「到了该结算的时刻」。
        /// 只做一次整数比较，开销可忽略——**不要**在这里再叠 IsHashIntervalTick，
        /// 那会和调用方的计数器锁成不同相位，导致组件永远等不到结算时刻
        /// （0.8.4 精灵灯踩过：放好了池子却一直不亮）。
        /// </summary>
        public bool ShouldWorkNow()
        {
            Thing parent = Parent;
            if (parent == null || !parent.Spawned)
            {
                return false;
            }
            return Find.TickManager.TicksGame >= nextWorkTick;
        }

        /// <summary>
        /// 尝试支付 amount 点魔力。**凑够才扣**——魔力不足时一点不抽，
        /// 避免把池子抽干却干不成活（旧版 Fluxfield 踩过这个坑）。
        /// </summary>
        public bool TryPay(float amount)
        {
            Thing parent = Parent;
            if (parent == null || parent.Map == null)
            {
                HasMana = false;
                return false;
            }
            if (amount <= 0f)
            {
                HasMana = true;
                return true;
            }
            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, parent.Map, props.SearchRadius, amount, parent);
            bool paid = false;
            if (source != null && source.ManaAmount + props.ManaTolerance >= amount)
            {
                float drained = source.DrainMana(amount);
                paid = drained >= amount - props.ManaTolerance;
            }
            HasMana = paid;
            return paid;
        }

        /// <summary>
        /// 取「设备所在格所属的魔力网络」作为魔力源（不扣款）。
        ///
        /// 网络重构（2026-11-24）后，设备**不再有各自的搜索半径**：
        /// 只要设备所在格落在某个已连接节点的覆盖圆内，就能从该网络取魔；
        /// 同一网络内的所有魔力池视为一个共享大池（见 ManaNetworkGroup）。
        /// </summary>
        public IManaSource FindSource(float needAtLeast = 0f)
        {
            Thing parent = Parent;
            if (parent == null || parent.Map == null)
            {
                return null;
            }
            IManaSource source = parent.Map.GetComponent<MapComponent_ManaNetwork>()?.SourceAt(parent.Position);
            if (source == null)
            {
                return null;
            }
            if (needAtLeast > 0f && source.ManaAmount + props.ManaTolerance < needAtLeast)
            {
                return null;
            }
            return source;
        }

        /// <summary>
        /// 按「有多少抽多少」支付：最多抽 amount，返回实际抽到的量。
        /// 修复器这类「按修理量计费」的设备用它；与 <see cref="TryPay"/> 不同，允许部分支付。
        /// </summary>
        public float TryPayUpTo(float amount)
        {
            Thing parent = Parent;
            if (parent == null || parent.Map == null)
            {
                HasMana = false;
                return 0f;
            }
            if (amount <= 0f)
            {
                HasMana = true;
                return 0f;
            }
            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, parent.Map, props.SearchRadius, 0f, parent);
            if (source == null)
            {
                HasMana = false;
                return 0f;
            }
            float take = Math.Min(amount, source.ManaAmount);
            if (take <= 0f)
            {
                HasMana = false;
                return 0f;
            }
            float drained = source.DrainMana(take);
            HasMana = drained > 0f;
            return drained;
        }

        /// <summary>
        /// 结算成功或失败后安排下一次结算时刻：
        /// 成功按正常间隔，失败按较短的 retry 间隔（来魔后能立刻恢复）。
        /// </summary>
        public void ScheduleNext(bool worked)
        {
            nextWorkTick = Find.TickManager.TicksGame
                + (worked ? props.WorkIntervalTicks : props.RetryIntervalTicks);
        }

        /// <summary>通用提示文案。</summary>
        public string InspectString()
        {
            if (Parent?.Map == null)
            {
                return null;
            }
            return HasMana ? props.WorkingKey.Translate() : props.NoManaKey.Translate();
        }

        public void ExposeData()
        {
            bool hasMana = HasMana;
            int next = nextWorkTick;
            Scribe_Values.Look(ref hasMana, props.HasManaSaveKey, true);
            Scribe_Values.Look(ref next, props.NextWorkSaveKey, 0);
            // 旧版存档兼容：老键只读不写（回写会和新键重复占坑）
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (!props.LegacyHasManaKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref hasMana, props.LegacyHasManaKey, hasMana);
                }
                if (!props.LegacyNextWorkKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref next, props.LegacyNextWorkKey, next);
                }
            }
            HasMana = hasMana;
            nextWorkTick = next;
        }
    }

    /// <summary>
    /// 便捷基类：父类不是特殊原版 Comp 时直接继承它，只写 WantsWork / DoWork 两个方法。
    /// 父类必须另有讲究（如 CompPowerTrader）时，改用组合 ManaDeviceCore。
    /// </summary>
    public abstract class CompManaDevice : ThingComp
    {
        private ManaDeviceCore core;
        private bool? lastReportedHasMana;

        protected ManaDeviceCore Core => core ??= new ManaDeviceCore(this, DeviceProps);

        protected CompProperties_ManaDevice DeviceProps => (CompProperties_ManaDevice)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            _ = Core;
            // 刻意**不**在这里结算：PostSpawnSetup 期间别的组件可能还没初始化完，
            // 此时去改别人的状态（例如拨灯光开关）会被覆盖或与引擎内部状态脱钩。
            // nextWorkTick 默认 0，下一个 tick 自然会结算一次。
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Core.ShouldWorkNow())
            {
                return;
            }
            SettleOnce();
        }

        /// <summary>立刻结算一次（由 tick 或子类显式调用）。</summary>
        protected virtual bool SettleOnce()
        {
            bool wants = WantsWork();
            bool paid = wants && Core.TryPay(ManaPerWork);
            if (paid)
            {
                DoWork(DeviceProps.workIntervalTicks);
            }
            Core.ScheduleNext(paid);
            // 魔力状态变化时通知子类（灯光、贴图、电网输出等在这里刷新）
            if (lastReportedHasMana != Core.HasMana)
            {
                lastReportedHasMana = Core.HasMana;
                OnManaStateChanged(Core.HasMana);
            }
            OnSettled(paid);
            return paid;
        }

        /// <summary>魔力状态发生变化（有→无 或 无→有）时调用。首次结算也会调用一次。</summary>
        protected virtual void OnManaStateChanged(bool hasMana)
        {
        }

        /// <summary>是否想干活（关着 / 无目标时返回 false，此时不扣魔力）。</summary>
        protected abstract bool WantsWork();

        /// <summary>真正干活。调用前魔力已扣除。</summary>
        protected abstract void DoWork(int ticks);

        /// <summary>结算后的回调（例如把魔力状态翻译成灯光开关）。</summary>
        protected virtual void OnSettled(bool worked)
        {
        }

        /// <summary>每次结算支付多少魔力。默认读配置，特殊设备可覆写。</summary>
        protected virtual float ManaPerWork => DeviceProps.manaPerWork;

        // 开发模式下的调试按钮刻意不在基类提供：
        // 以前基类会给每个设备挂「立刻结算一次 / 跳过冷却」这类通用按钮，
        // 它们绕过设备流程、对任何设备都一样、也测不出该设备独有的机制，用户明确要求去掉。
        // 调试按钮改由每个设备按自己真实的机制入口单独实现（见各子类的 CompGetGizmosExtra）。

        public override string CompInspectStringExtra()
        {
            return Core.InspectString();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Core.ExposeData();
        }
    }
}
