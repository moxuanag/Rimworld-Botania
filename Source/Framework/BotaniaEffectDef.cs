using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 一条「魔力效果」的数据定义（S5 效果注册表）。
    ///
    /// 设计意图：效果本身是**数据**，代码只有一个小小的 worker。
    /// 换一个 worker 子类 + 改 XML 参数就能做出新效果，不必再为每栋设备写一遍
    /// 「找魔力源 / 凑够才扣 / 节流 / 存档」——那些全在
    /// <see cref="CompManaDevice"/> + <see cref="ManaDeviceCore"/> 里。
    ///
    /// 这个 Def 本身不挂在任何东西上，必须被下面二者之一引用后才会生效：
    ///   · <see cref="CompProperties_ManaEffectDevice.effects"/>（写在 ThingDef 的 comps 里）；
    ///   · <see cref="ModExt_ManaDeviceEffects.effects"/>（写在 ThingDef 的 modExtensions 里）。
    /// 二者同时配置时按 <see cref="BotaniaEffectDef"/> 去重合并（同一个效果只结算一次）。
    ///
    /// XML 里 Def 的类型名要写全名：<c>&lt;Botania.BotaniaEffectDef&gt;</c>（自定义 Def 放在
    /// 自己的命名空间里，引擎按完整类型名解析；只写短名会报 "is not a Def type"）。
    /// </summary>
    public class BotaniaEffectDef : Def
    {
        /// <summary>
        /// 实际执行效果的 worker 类型，必须是 <see cref="BotaniaEffectWorker"/> 的非抽象子类。
        /// XML 写法：<c>&lt;workerClass&gt;Botania.ManaEffect_GrowthBoost&lt;/workerClass&gt;</c>。
        /// </summary>
        public Type workerClass;

        /// <summary>通用浮点参数，语义由 worker 决定（例：每次结算的成长度增量）。</summary>
        public float amount;

        /// <summary>通用整数参数，语义由 worker 决定（例：每次结算最多处理几个目标）。</summary>
        public int intValue;

        /// <summary>通用 ThingDef 参数，语义由 worker 决定（留给需要「指定物品/植物」的效果）。</summary>
        public ThingDef thingDef;

        /// <summary>通用字符串参数，语义由 worker 决定（留给需要「按标签/键筛选」的效果）。</summary>
        public string stringValue;

        /// <summary>
        /// 作用半径（格）。作用于「以设备所在格为圆心、半径 N」的范围；
        /// 0 表示只算设备所在格。上限 60：<see cref="GenRadial"/> 的径向格表只预生成到约 80 格，
        /// 超过会刷 "Not enough squares to get to radius" 错误（见 <see cref="BotaniaEffectWorker.MaxRadius"/>）。
        /// </summary>
        public float radius = 3f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (workerClass == null)
            {
                yield return "workerClass is required (must be a concrete BotaniaEffectWorker subclass).";
            }
            else if (!typeof(BotaniaEffectWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from BotaniaEffectWorker, got " + workerClass.FullName + ".";
            }
            else if (workerClass.IsAbstract)
            {
                yield return "workerClass must not be abstract, got " + workerClass.FullName + ".";
            }
            if (radius < 0f)
            {
                yield return "radius must not be negative.";
            }
            else if (radius > BotaniaEffectWorker.MaxRadius)
            {
                yield return "radius must be at most " + BotaniaEffectWorker.MaxRadius
                    + " (GenRadial only pre-generates cells up to ~80).";
            }
        }
    }

    /// <summary>
    /// 效果执行器基类：无状态、可复用（<see cref="CompManaEffectDevice"/> 会按类型缓存实例）。
    /// 子类只实现 <see cref="Apply"/>；节流、付款、存档、范围环都不归它管。
    /// </summary>
    public abstract class BotaniaEffectWorker
    {
        /// <summary>
        /// 允许的最大作用半径（格）。<see cref="GenRadial"/> 只预生成到约 80 格，
        /// 这里留余量取 60，避免玩家/作者把半径配得过大时每 tick 刷错误日志。
        /// </summary>
        public const float MaxRadius = 60f;

        /// <summary>
        /// 安全的作用半径：负数按 0、超过 <see cref="MaxRadius"/> 按上限收。
        /// 各 worker 都要用它取半径，别直接读 def.radius（配置错误不该把 tick 刷成错误流）。
        /// </summary>
        protected static float RadiusOf(BotaniaEffectDef def)
        {
            if (def == null)
            {
                return 0f;
            }
            return Math.Max(0f, Math.Min(def.radius, MaxRadius));
        }

        /// <summary>
        /// 真正应用一次效果。调用前魔力已由基类扣掉，<paramref name="ticks"/> 为本次结算间隔。
        /// </summary>
        /// <param name="def">本条效果的数据（amount / intValue / radius…）。</param>
        /// <param name="parent">挂载效果的 Thing（设备本体），保证非 null 且已 Spawn。</param>
        /// <param name="map">parent 所在地图。</param>
        /// <param name="ticks">结算间隔（tick），一般只用于「按时间缩放」的效果。</param>
        public abstract void Apply(BotaniaEffectDef def, Thing parent, Map map, int ticks);
    }

    /// <summary>
    /// 挂在 ThingDef 上的效果扩展：让「同一套效果」可以被多栋设备按 Def 引用，
    /// 不必为每栋建筑在 comps 里重复抄一遍效果列表。
    /// 写法见 Defs/EffectDefs/EffectDefs_Botania.xml 与文档里的挂载片段。
    /// </summary>
    public class ModExt_ManaDeviceEffects : DefModExtension
    {
        /// <summary>效果列表。没配置时为 null，框架按「空列表」处理。</summary>
        public List<BotaniaEffectDef> effects;
    }

    /// <summary>
    /// 效果设备配置：在耗魔设备配置（<see cref="CompProperties_ManaDevice"/>）之上，
    /// 加一组要反复结算的效果。设备 Comp 必须是 <see cref="CompManaEffectDevice"/>。
    /// </summary>
    public class CompProperties_ManaEffectDevice : CompProperties_ManaDevice
    {
        /// <summary>本设备私有的一组效果（与 ThingDef 上 ModExt 里的效果合并去重）。</summary>
        public List<BotaniaEffectDef> effects = new List<BotaniaEffectDef>();

        public CompProperties_ManaEffectDevice()
        {
            // 基类构造把 compClass 指向 CompManaDevice；这里必须改回本框架的 Comp。
            compClass = typeof(CompManaEffectDevice);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            int usable = 0;
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] == null)
                    {
                        yield return "effects[" + i + "] is null (def not found or not a BotaniaEffectDef).";
                        continue;
                    }
                    usable++;
                }
            }
            // 物品/建筑 Def 上还可以再挂 ModExt_ManaDeviceEffects，这里一并校验，
            // 免得「props 只写组件、效果写在 modExtensions」时漏检。
            if (parentDef != null)
            {
                ModExt_ManaDeviceEffects extension = parentDef.GetModExtension<ModExt_ManaDeviceEffects>();
                if (extension != null && extension.effects != null)
                {
                    for (int i = 0; i < extension.effects.Count; i++)
                    {
                        if (extension.effects[i] == null)
                        {
                            yield return "modExtensions/ModExt_ManaDeviceEffects effects[" + i + "] is null.";
                            continue;
                        }
                        usable++;
                    }
                }
            }
            if (usable == 0)
            {
                yield return (parentDef != null ? parentDef.defName : "A ThingDef")
                    + " has CompProperties_ManaEffectDevice but no effects configured"
                    + " (props.effects and ModExt_ManaDeviceEffects.effects are both empty).";
            }
        }
    }

    /// <summary>
    /// 效果设备的 Comp：把「要花魔力反复做的效果」变成数据。
    ///
    /// 与 <see cref="CompManaDevice"/> 的关系（刻意不重复造轮子）：
    ///   · 节流：基类 <see cref="CompManaDevice.CompTick"/> → SettleOnce（本类不碰 nextWorkTick）；
    ///   · 付款：基类 TryPay「凑够才扣，不够一点不抽」；
    ///   · 存档：基类按 saveKeyPrefix 平铺读写（本类没有额外存档字段）；
    ///   · 提示文案：基类给「工作中 / 缺魔」，本类再追加一行效果列表。
    /// 本类只负责三件事：合并效果列表（去重）、付得起才要干活、逐个实例化 worker 并 Apply。
    /// </summary>
    public class CompManaEffectDevice : CompManaDevice
    {
        private List<BotaniaEffectDef> resolvedEffects;

        // worker 是无状态执行器：按类型缓存，别每 tick 反射 new 一个（GC 与反射开销都省掉）。
        // value 允许为 null：workerClass 配错的 def 只报一次错，之后直接跳过。
        private readonly Dictionary<Type, BotaniaEffectWorker> workers = new Dictionary<Type, BotaniaEffectWorker>();

        protected CompProperties_ManaEffectDevice EffectProps => (CompProperties_ManaEffectDevice)props;

        /// <summary>
        /// 合并后的效果列表（props.effects + ThingDef modExtensions.effects，按 Def 去重）。
        /// 首次访问时解析并缓存；Def 在运行期不变，所以不需要失效逻辑。
        /// </summary>
        public IReadOnlyList<BotaniaEffectDef> Effects
        {
            get
            {
                if (resolvedEffects == null)
                {
                    resolvedEffects = ResolveEffects();
                }
                return resolvedEffects;
            }
        }

        private List<BotaniaEffectDef> ResolveEffects()
        {
            List<BotaniaEffectDef> result = new List<BotaniaEffectDef>();
            HashSet<BotaniaEffectDef> seen = new HashSet<BotaniaEffectDef>();
            AppendEffects(EffectProps.effects, result, seen);
            ModExt_ManaDeviceEffects extension = parent?.def?.GetModExtension<ModExt_ManaDeviceEffects>();
            if (extension != null)
            {
                AppendEffects(extension.effects, result, seen);
            }
            return result;
        }

        /// <summary>把一组效果追加到合并列表，同一 Def 只保留第一次出现（props 优先于 ModExt）。</summary>
        private static void AppendEffects(
            List<BotaniaEffectDef> source,
            List<BotaniaEffectDef> result,
            HashSet<BotaniaEffectDef> seen)
        {
            if (source == null)
            {
                return;
            }
            for (int i = 0; i < source.Count; i++)
            {
                BotaniaEffectDef def = source[i];
                if (def != null && seen.Add(def))
                {
                    result.Add(def);
                }
            }
        }

        /// <summary>
        /// 有任意效果、且付得起这一次的魔力时才想干活。
        /// 返回 false 时基类不会扣魔力，只会安排一次短间隔重试（来魔后立刻恢复）。
        /// 没有任何效果时不干活，避免「空配置设备」白白花魔力。
        /// </summary>
        protected override bool WantsWork()
        {
            if (Effects.Count == 0)
            {
                return false;
            }
            if (!CanAffordWork())
            {
                // 基类只在 TryPay 被调用时更新 HasMana；这里提前判定「付不起」，
                // 要手动把状态同步给 Core，否则 inspect / OnManaStateChanged 会停在「有魔」的旧状态。
                Core.SetHasMana(false);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 是否付得起一次结算。用与基类 TryPay 完全相同的「凑够才扣」判定：
        /// 找源阈值就是 manaPerWork，所以这里返回 true 时随后的 TryPay 一定能找到同一个源付上。
        /// </summary>
        private bool CanAffordWork()
        {
            float cost = ManaPerWork;
            if (cost <= 0f)
            {
                return true;
            }
            return Core.FindSource(cost) != null;
        }

        /// <summary>逐个应用效果。调用前魔力已扣，节流由基类负责，这里不要自己 tick 计数。</summary>
        protected override void DoWork(int ticks)
        {
            Thing thing = parent;
            Map map = thing?.Map;
            if (thing == null || map == null)
            {
                return;
            }
            IReadOnlyList<BotaniaEffectDef> effects = Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                BotaniaEffectDef def = effects[i];
                BotaniaEffectWorker worker = GetWorker(def);
                if (worker == null)
                {
                    continue;
                }
                try
                {
                    worker.Apply(def, thing, map, ticks);
                }
                catch (Exception exception)
                {
                    // 一个 worker 抛异常不该拖垮整栋设备的下一次结算：报一次错，继续跑别的效果。
                    Log.ErrorOnce(
                        "Botania: effect worker " + def.workerClass.Name + " of " + def.defName
                            + " threw on " + thing.def.defName + ": " + exception,
                        def.defName.GetHashCode());
                }
            }
        }

        /// <summary>按 workerClass 取缓存实例；配错（null / 抽象 / 类型不对）只报一次错并缓存 null。</summary>
        private BotaniaEffectWorker GetWorker(BotaniaEffectDef def)
        {
            if (def?.workerClass == null)
            {
                return null;
            }
            Type workerClass = def.workerClass;
            BotaniaEffectWorker worker;
            if (workers.TryGetValue(workerClass, out worker))
            {
                return worker;
            }
            if (!typeof(BotaniaEffectWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
            {
                Log.ErrorOnce(
                    "Botania: " + def.defName + " has an invalid workerClass " + workerClass.FullName
                        + " (must be a concrete BotaniaEffectWorker subclass), effect skipped.",
                    def.defName.GetHashCode());
                workers[workerClass] = null;
                return null;
            }
            worker = (BotaniaEffectWorker)Activator.CreateInstance(workerClass);
            workers[workerClass] = worker;
            return worker;
        }

        /// <summary>基类的「工作中 / 缺魔」提示之后，追加一行「效果：…」，方便进游戏核实配置。</summary>
        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            string effectLine = DescribeEffects();
            if (effectLine.NullOrEmpty())
            {
                return text;
            }
            string line = "Botania.EffectDeviceEffects".Translate(effectLine);
            return text.NullOrEmpty() ? line : text + "\n" + line;
        }

        private string DescribeEffects()
        {
            IReadOnlyList<BotaniaEffectDef> effects = Effects;
            if (effects.Count == 0)
            {
                return null;
            }
            List<string> labels = new List<string>();
            for (int i = 0; i < effects.Count; i++)
            {
                BotaniaEffectDef def = effects[i];
                labels.Add(def.label.NullOrEmpty() ? def.defName : def.label);
            }
            return string.Join(", ", labels);
        }
    }
}
