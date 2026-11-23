using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 产能（产魔）设备的公共配置。
    ///
    /// 框架思路与 <see cref="CompProperties_ManaDevice"/> 对称：
    /// 设备负责「花魔力干活」，产能设备负责「凭空/靠资源产生魔力并送进最近魔力池」。
    /// 以前每朵产魔花各自抄一遍：找池子、定时结算、缓冲、存档、提示文案。
    ///
    /// 两种用法：
    ///   · 无缓冲（bufferCapacity = 0）：每次结算直接把产物送进池子（太阳花 / 夜颠茄）；
    ///   · 有缓冲（bufferCapacity &gt; 0）：产物先进自己的缓存，结算时整块推送（电暴花）。
    /// </summary>
    public class CompProperties_ManaProducer : CompProperties
    {
        /// <summary>寻找魔力池的半径（格）。</summary>
        public float radius = 6f;

        /// <summary>结算间隔（tick）。60 = 每秒一次。</summary>
        public int settleIntervalTicks = 60;

        /// <summary>
        /// 缓冲上限。0 表示不缓冲（产物直接送池子）；
        /// 大于 0 时产物先攒在自己身上，再整块推送。
        /// </summary>
        public float bufferCapacity = 0f;

        /// <summary>「正在产能」提示文案的 Keyed 键；null 用通用键。</summary>
        public string inspectWorkingKey;

        /// <summary>「附近没有可接收的魔力池」提示文案的 Keyed 键；null 用通用键。</summary>
        public string inspectNoPoolKey;

        /// <summary>
        /// 存档键前缀。本引擎存档把组件字段平铺在 Thing 节点下，键必须够独特。
        /// </summary>
        public string saveKeyPrefix = "botaniaManaProducer";

        /// <summary>旧版存档里的缓冲字段名（迁移兼容，读得到就续上）。</summary>
        public string legacyBufferKey;

        /// <summary>是否也允许把魔力送进中继塔（默认 false：行为与旧版一致，只喂魔力池）。</summary>
        public bool feedRelays = false;

        public string BufferSaveKey => saveKeyPrefix + "Buffer";
        public string NextSettleSaveKey => saveKeyPrefix + "NextSettleTick";
        public string WorkingKey => inspectWorkingKey ?? "Botania.ProducerWorking";
        public string NoPoolKey => inspectNoPoolKey ?? "Botania.ProducerNoPool";

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (radius < 0f)
            {
                yield return "radius must not be negative.";
            }
            if (settleIntervalTicks <= 0)
            {
                yield return "settleIntervalTicks must be greater than 0.";
            }
            if (bufferCapacity < 0f)
            {
                yield return "bufferCapacity must not be negative.";
            }
            if (saveKeyPrefix.NullOrEmpty())
            {
                yield return "saveKeyPrefix must not be empty (save fields are flat per Thing).";
            }
        }
    }

    /// <summary>
    /// 产能设备基类。子类只需要回答：
    ///   · <see cref="WantsProduce"/> —— 现在能不能产（昼夜、燃料、是否枯萎…）
    ///   · <see cref="ProducePerSettle"/> —— 这一次结算产多少魔力
    ///   · <see cref="ExtraTick"/> —— 每 tick 的记账（年龄、枯萎）可选
    /// 找池子 / 缓冲 / 结算节流 / 存档 / 提示文案全部由基类负责。
    ///
    /// 铁律（见 AGENTS.md §5）：
    ///   · 只在 <see cref="CompTick"/> 里做一次整数比较，不叠加 IsHashIntervalTick；
    ///   · 不在 PostSpawnSetup 里结算；
    ///   · 存档键带前缀，并兼容旧键。
    /// </summary>
    public abstract class CompManaProducer : ThingComp
    {
        private float buffer;
        private int nextSettleTick;

        protected CompProperties_ManaProducer ProducerProps => (CompProperties_ManaProducer)props;

        /// <summary>当前缓存中的魔力（无缓冲模式恒为 0）。</summary>
        public float Buffer => buffer;

        /// <summary>缓存剩余空间（无缓冲模式恒为 0）。</summary>
        public float FreeBufferSpace => Math.Max(0f, ProducerProps.bufferCapacity - buffer);

        /// <summary>每 tick 的记账钩子（年龄、枯萎计时等）。默认什么都不做。</summary>
        protected virtual void ExtraTick()
        {
        }

        /// <summary>当前是否允许产能。</summary>
        protected virtual bool WantsProduce()
        {
            return true;
        }

        /// <summary>本次结算产出的魔力量。ticks 为结算间隔。</summary>
        protected virtual float ProducePerSettle(int ticks)
        {
            return 0f;
        }

        /// <summary>
        /// 开发调试：把下一次结算时刻清零（相当于「清除冷却」）。
        /// 基类<b>不再</b>显示通用调试按钮；需要时由子类在它自己的专用调试按钮里调用本方法。
        /// </summary>
        public void DebugSkipCooldown()
        {
            nextSettleTick = 0;
        }

        /// <summary>
        /// 开发调试：把缓存灌满（仅缓冲式设备有意义）。
        /// 基类<b>不再</b>显示通用调试按钮；需要时由子类在专用调试按钮里调用。
        /// 注意：它直接改缓存，不经过产能机制——子类若要走真实产能路径，不要用它。
        /// </summary>
        public void DebugFillBuffer()
        {
            if (ProducerProps.bufferCapacity > 0f)
            {
                buffer = ProducerProps.bufferCapacity;
            }
        }

        /// <summary>
        /// 开发调试：清空缓存。
        /// 基类<b>不再</b>显示通用调试按钮；需要时由子类在专用调试按钮里调用。
        /// </summary>
        public void DebugClearBuffer()
        {
            buffer = 0f;
        }

        /// <summary>允许外部把能量直接灌进缓存（电暴花吸收电网短路能量用）。</summary>
        public float AbsorbIntoBuffer(float amount)
        {
            if (amount <= 0f || ProducerProps.bufferCapacity <= 0f)
            {
                return 0f;
            }
            float used = Math.Min(amount, FreeBufferSpace);
            buffer += used;
            return used;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            ExtraTick();
            // 只做一次整数比较；节流只有一个来源
            if (Find.TickManager.TicksGame < nextSettleTick)
            {
                return;
            }
            nextSettleTick = Find.TickManager.TicksGame + Math.Max(1, ProducerProps.settleIntervalTicks);
            SettleNow();
        }

        /// <summary>立刻结算一次（tick 之外也可以调用，例如开发调试按钮）。</summary>
        protected void SettleNow()
        {
            bool buffered = ProducerProps.bufferCapacity > 0f;
            if (buffered)
            {
                if (WantsProduce())
                {
                    float produced = ProducePerSettle(ProducerProps.settleIntervalTicks);
                    if (produced > 0f)
                    {
                        buffer = Math.Min(ProducerProps.bufferCapacity, buffer + produced);
                    }
                }
                if (buffer > 0f)
                {
                    float accepted = PushToPool(buffer);
                    if (accepted > 0f)
                    {
                        buffer = Math.Max(0f, buffer - accepted);
                        OnManaAccepted(accepted);
                    }
                    else
                    {
                        OnNoTarget();
                    }
                }
                return;
            }

            if (!WantsProduce())
            {
                return;
            }
            float amount = ProducePerSettle(ProducerProps.settleIntervalTicks);
            if (amount > 0f)
            {
                float accepted = PushToPool(amount);
                if (accepted > 0f)
                {
                    OnManaAccepted(accepted);
                }
                else
                {
                    OnNoTarget();
                }
            }
        }

        /// <summary>产物被池子实际接收后回调（例如火红莲按接收量扣燃料）。</summary>
        protected virtual void OnManaAccepted(float acceptedMana)
        {
        }

        /// <summary>有产物、但附近没有可接收的池子时回调（例如允许空烧燃料的花）。</summary>
        protected virtual void OnNoTarget()
        {
        }

        /// <summary>把魔力推给最近的池子，返回实际被接收的量。</summary>
        private float PushToPool(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            IManaSource target = FindTarget();
            return target?.AddMana(amount) ?? 0f;
        }

        /// <summary>找最近的可接收魔力的目标（默认只找魔力池）。</summary>
        protected IManaSource FindTarget()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, ProducerProps.radius, true))
            {
                CompManaStorage pool = thing.TryGetComp<CompManaStorage>();
                if (pool != null && pool.AcceptsMana && !pool.IsFull)
                {
                    return pool;
                }
                if (ProducerProps.feedRelays)
                {
                    IManaSource source = ManaSourceUtil.AsSource(thing);
                    if (source != null && source.FreeSpace > 0.01f)
                    {
                        return source;
                    }
                }
            }
            return null;
        }

        /// <summary>附近有没有能接收魔力的目标（供提示文案使用）。</summary>
        public bool HasTarget()
        {
            return FindTarget() != null;
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            string state = HasTarget()
                ? ProducerProps.WorkingKey.Translate()
                : ProducerProps.NoPoolKey.Translate();
            string extra = ExtraInspectString();
            if (!extra.NullOrEmpty())
            {
                state += "\n" + extra;
            }
            return state;
        }

        // 开发模式下的调试按钮刻意不在基类提供：
        // 以前的「立刻结算 / 跳过冷却 / 灌满缓存 / 清空缓存」是绕过设备流程的通用按钮，
        // 对任何设备都一样、也测不出该设备独有的机制，用户明确要求去掉。
        // 调试按钮改由每个设备按自己真实的机制入口单独实现（见各子类的 CompGetGizmosExtra）。

        /// <summary>子类追加提示（昼夜、缓冲量、剩余寿命等）。</summary>
        protected virtual string ExtraInspectString()
        {
            if (ProducerProps.bufferCapacity > 0f)
            {
                return "Botania.ProducerStored".Translate(
                    buffer.ToString("0"),
                    ProducerProps.bufferCapacity.ToString("0"));
            }
            return null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            float savedBuffer = buffer;
            int next = nextSettleTick;
            Scribe_Values.Look(ref savedBuffer, ProducerProps.BufferSaveKey, 0f);
            Scribe_Values.Look(ref next, ProducerProps.NextSettleSaveKey, 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !ProducerProps.legacyBufferKey.NullOrEmpty())
            {
                Scribe_Values.Look(ref savedBuffer, ProducerProps.legacyBufferKey, savedBuffer);
            }
            buffer = savedBuffer;
            nextSettleTick = next;
        }
    }
}
