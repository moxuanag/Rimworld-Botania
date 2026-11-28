using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 丰饶之花（Fertility Flower）的配置。
    ///
    /// 框架位置：它继承 <see cref="CompProperties_ManaDevice"/>，所以「找魔力源 / 凑够才扣 /
    /// 失败短间隔重试 / 平铺存档键 / inspect 底行」全部由 <see cref="ManaDeviceCore"/> 提供，
    /// 本类只描述这朵花自己的业务参数。
    ///
    /// 工作量模型是「按秒 × 固定工作量」：
    /// 每秒累积 <c>workPerPlant / SecondsPerPlant</c> 点，种下/收获一株固定扣 <c>workPerPlant</c> 点，
    /// 所以每株耗时恒为 15 / 8 / 1 秒（三档），与作物自身的 sowWork / harvestWork 无关。
    ///
    /// 花瓣储备不在这里：那是原版 <see cref="CompRefuelable"/> 的活（容量、加料清单、
    /// 加料小部件、存档都由它管），本组件只调 <c>HasFuel</c> / <c>ConsumeFuel</c>。
    ///
    /// 作用范围环也不在这里：Def 上挂 <see cref="CompProperties_RadiusRing"/> +
    /// <see cref="PlaceWorker_RadiusFromComp"/> 即可（放置预览环 + 选中常驻环）。
    /// 半径随研究档位变化，所以本类实现 <see cref="IDynamicPlaceRadius"/>（放置预览），
    /// 运行时那一侧由 <see cref="CompFertilityFlower"/> 实现 <see cref="IDynamicRingRadius"/>。
    /// </summary>
    public class CompProperties_FertilityFlower : CompProperties_ManaDevice, IDynamicPlaceRadius
    {
        /// <summary>基础档作用半径（格）：播种、收获、催熟、数树都按它算。</summary>
        public float radius = 5f;

        /// <summary>进阶档半径加成（格）。</summary>
        public float radiusAdvancedBonus = 1f;

        /// <summary>终极档半径加成（格）。</summary>
        public float radiusUltimateBonus = 1f;

        /// <summary>基础档：每株要照料多少秒（Def 里是 15）。</summary>
        public float secondsPerPlantBase = 15f;

        /// <summary>进阶档：每株要照料多少秒（Def 里是 8）。</summary>
        public float secondsPerPlantAdvanced = 8f;

        /// <summary>终极档：每株要照料多少秒（Def 里是 1）。</summary>
        public float secondsPerPlantUltimate = 1f;

        /// <summary>播种 / 收获一株的固定工作量（点）。</summary>
        public float workPerPlant = 200f;

        /// <summary>
        /// 旧参数：过去「铲掉格子上那株野生草」要额外付的工作量。
        ///
        /// 现在铲草**零成本**（清地本身不再计价，播种只扣 <see cref="workPerPlant"/>），
        /// 所以本字段**保留但不再参与任何计算** —— 留着只为 Def / 旧存档的字段名继续有效。
        /// </summary>
        public float wipeWork = 0f;

        /// <summary>每播下一株消耗的魔力。</summary>
        public float manaPerSow = 12f;

        /// <summary>每收获一株消耗的魔力。</summary>
        public float manaPerHarvest = 6f;

        /// <summary>每播种 N 次扣 1 片花瓣（花瓣由 CompRefuelable 管；Def 里是 7）。</summary>
        public int sowsPerPetal = 7;

        /// <summary>催熟一次固定消耗的魔力（与株数无关）。</summary>
        public float manaPerRipen = 1000f;

        /// <summary>催熟一次给每株加多少成长度（0.30 = 30%）。</summary>
        public float ripenGrowth = 0.30f;

        /// <summary>催熟冷却（tick）。</summary>
        public int ripenCooldownTicks = 180;

        /// <summary>每棵树每夜给几个原木。</summary>
        public int woodPerTree = 3;

        /// <summary>每夜最多算几棵树。</summary>
        public int woodMaxTrees = 30;

        /// <summary>几点触发取木（本地时间，0 = 午夜）。</summary>
        public int woodHour = 0;

        /// <summary>
        /// 进阶解锁研究。XML 不写时按 <see cref="DefaultAdvancedResearch"/> 兜底
        /// （Def 引用没法写在字段初始化器里，所以用 <see cref="AdvancedResearch"/> 惰性解析）。
        /// </summary>
        public ResearchProjectDef advancedResearch;

        /// <summary>终极解锁研究（同上，默认 <see cref="DefaultUltimateResearch"/>）。</summary>
        public ResearchProjectDef ultimateResearch;

        public const string DefaultAdvancedResearch = "Botania_FuncAdvanced";
        public const string DefaultUltimateResearch = "Botania_FuncUltimate";

        /// <summary>实际生效的进阶研究：XML 显式配置优先，否则按默认 defName 解析。</summary>
        public ResearchProjectDef AdvancedResearch =>
            advancedResearch ?? DefDatabase<ResearchProjectDef>.GetNamedSilentFail(DefaultAdvancedResearch);

        /// <summary>实际生效的终极研究：XML 显式配置优先，否则按默认 defName 解析。</summary>
        public ResearchProjectDef UltimateResearch =>
            ultimateResearch ?? DefDatabase<ResearchProjectDef>.GetNamedSilentFail(DefaultUltimateResearch);

        /// <summary>
        /// 放置预览环用的半径：按「此刻」的研究完成情况算（5 / 6 / 7 格）。
        /// 放置时还没有建筑实例，只能问 CompProperties，所以动态半径放在这一层。
        /// </summary>
        public float CurrentPlaceRadius
        {
            get
            {
                float value = radius;
                ResearchProjectDef ultimate = UltimateResearch;
                if (ultimate != null && ultimate.IsFinished)
                {
                    return value + radiusAdvancedBonus + radiusUltimateBonus;
                }
                ResearchProjectDef advanced = AdvancedResearch;
                if (advanced != null && advanced.IsFinished)
                {
                    return value + radiusAdvancedBonus;
                }
                return value;
            }
        }

        public CompProperties_FertilityFlower()
        {
            compClass = typeof(CompFertilityFlower);

            // 基类配置：每秒一个结算周期（播种/收获都按「每秒工作量 × 秒数」累积），
            // 失败按同样的短间隔重试，来魔后立刻恢复；存档键带自己的前缀，
            // 免得同一件东西上多个组件平铺字段时撞键。
            searchRadius = 6f;
            workIntervalTicks = 60;
            retryIntervalTicks = 60;
            manaTolerance = 0.01f;
            saveKeyPrefix = "botaniaFertilityFlower";
        }

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
            if (radiusAdvancedBonus < 0f || radiusUltimateBonus < 0f)
            {
                yield return "radiusAdvancedBonus / radiusUltimateBonus must not be negative.";
            }
            if (secondsPerPlantBase <= 0f || secondsPerPlantAdvanced <= 0f || secondsPerPlantUltimate <= 0f)
            {
                yield return "secondsPerPlant* must be greater than 0 (a plant per N seconds).";
            }
            if (workPerPlant <= 0f)
            {
                yield return "workPerPlant must be greater than 0.";
            }
            if (manaPerSow < 0f)
            {
                yield return "manaPerSow must not be negative.";
            }
            if (manaPerHarvest < 0f)
            {
                yield return "manaPerHarvest must not be negative.";
            }
            if (manaPerRipen < 0f)
            {
                yield return "manaPerRipen must not be negative.";
            }
            if (ripenGrowth < 0f)
            {
                yield return "ripenGrowth must not be negative.";
            }
            if (ripenCooldownTicks < 0)
            {
                yield return "ripenCooldownTicks must not be negative.";
            }
            if (sowsPerPetal < 1)
            {
                yield return "sowsPerPetal must be at least 1.";
            }
            if (woodPerTree < 0)
            {
                yield return "woodPerTree must not be negative.";
            }
            if (woodMaxTrees < 0)
            {
                yield return "woodMaxTrees must not be negative.";
            }
            if (woodHour < 0 || woodHour > 23)
            {
                yield return "woodHour must be within 0~23 (local hour).";
            }
        }
    }

    /// <summary>
    /// 丰饶之花：在半径内用魔力替小人干农活 —— 播种（含铲掉挡路的野草）、收获、催熟，
    /// 夜里还给原木。
    ///
    /// **扫描与种植严格按 <c>docs/丰饶花扫描与种植规则.md</c>（R1–R9）实现**，编号就是顺序：
    ///   · R1 编号：<see cref="RebuildGrid"/>（z 降序 → 同行 x 升序），触发点 = 建造/重装/读档
    ///     （<see cref="PostSpawnSetup"/>）+ 发现研究档位变化；
    ///   · R3 主扫描：<see cref="StepMainScan"/> 按 <see cref="grid"/> 逐格走 R2 短路判定，
    ///     通过 → 绿框 + 攒 <see cref="pendingWork"/> → 攒够再按 R2 重判一次 → 付魔力/扣花瓣 →
    ///     割除（<c>Destroy(Vanish)</c>）→ 同格 <c>SowAt</c> → 熄绿 → 下一格；
    ///   · R4 升级回扫：<see cref="BeginUpgradeRescan"/> + <see cref="StepRescan"/>（回扫
    ///     「旧标记格 + 新增格」→ 按新范围重新编号 → 回主扫描），两处触发共用同一份幂等逻辑；
    ///   · R5 弱扫描：每 7500 tick（3 游戏小时）一次，先比范围（不一致就重编号回主扫描），
    ///     然后逐格收（每结算最多 20 格）→ 补种 → 回扫标记格；
    ///   · R6 标记集合：所有扫描路径的判定失败格都进 <see cref="unplantable"/>，种下即移除，
    ///     只在 R4.2 / R5.5 两个时机重判。
    ///
    /// 为什么覆写 <see cref="CompManaDevice.SettleOnce"/> 而不是 <c>WantsWork/DoWork</c>：
    /// 基类那套是「每次结算付一笔固定魔力、做一件固定的事」；这朵花是**按株计价**
    /// （收 1 株 manaPerHarvest、种 1 株 manaPerSow），而且一次结算里可能连做十几株。
    /// 所以自己组织循环，但取魔、节流、失败重试、inspect 仍然只走
    /// <see cref="ManaDeviceCore"/>（<c>TryPay</c> 凑够才扣、<c>FindSource</c> 只查不扣、
    /// <c>ScheduleNext</c> 排下一次、<c>InspectString</c> 出底行），一行取魔逻辑都没有重写。
    ///
    /// 所有植物侧的通用动作（收获判定、割除后给产物、播种、催熟、数树）在
    /// <see cref="PlantMagicUtil"/> 里。
    /// </summary>
    public class CompFertilityFlower : CompManaDevice, IDynamicRingRadius
    {
        /// <summary>基础档产量系数（略低于小人手收，因为省了人力）。</summary>
        private const float YieldFactorBase = 0.50f;

        /// <summary>进阶档产量系数。</summary>
        private const float YieldFactorAdvanced = 1.00f;

        /// <summary>终极档产量系数。</summary>
        private const float YieldFactorUltimate = 1.50f;

        /// <summary>单次结算里每种动作最多做几次（防止一帧里刷爆地图；R5.3「每结算最多处理 20 格」）。</summary>
        private const int MaxActionsPerSettle = 20;

        /// <summary>收获候选的重算间隔（tick）：三态门控「范围内有没有可收作物」按它缓存。</summary>
        private const int HarvestCacheTicks = 30;

        /// <summary>弱扫描周期：3 游戏小时 = 7500 tick（R5.1）。</summary>
        private const int WeakScanIntervalTicks = 7500;

        /// <summary>绿灯时长给得很长：绿框要「亮着直到种下」，真正熄灭靠 SowAt 成功后的 Remove。</summary>
        private const int PendingHighlightTicks = 3600;

        /// <summary>琥珀灯时长：判定失败的格子约 45 tick（0.75 秒）后自己消失。</summary>
        private const int FailHighlightTicks = 45;

        /// <summary>暂停/继续按钮的图标（仓库里已有的花形开关图）。</summary>
        private const string ToggleIconPath = "UI/Commands/Botania_DaisyToggle";

        /// <summary>催熟按钮的图标（绽放的花，语义贴近「催熟」）。</summary>
        private const string RipenIconPath = "UI/Icons/Botania/Bloom";

        /// <summary>「生成种植区」按钮的图标：原版「新建种植区」（G1.1）。</summary>
        private const string ZoneCreateIconPath = "UI/Designators/ZoneCreate_Growing";

        private static Texture2D toggleIcon;
        private static Texture2D ripenIcon;
        private static Texture2D zoneCreateIcon;

        // ------------------------------------------------------------------
        // 状态机的阶段（R3 / R4 / R5）。存档保存的就是这个字段。
        // ------------------------------------------------------------------
        private enum Phase
        {
            /// <summary>R3 主扫描：按编号逐格判定 → 割除 → 同格种下。</summary>
            MainScan,

            /// <summary>R4 升级回扫：回扫「旧标记格 + 半径变大后的新增格」。</summary>
            UpgradeRescan,

            /// <summary>R5.3 弱扫描第一段：按编号逐格收，只收不补种。</summary>
            WeakHarvest,

            /// <summary>R5.4 弱扫描第二段：把空的且可种的格子补种上。</summary>
            WeakReplant,

            /// <summary>R5.5 弱扫描第三段：补种完回扫标记格。</summary>
            WeakRescan,

            /// <summary>空闲：等下一个 3 游戏小时（或等上一轮主扫描没走完的部分接着走）。</summary>
            Idle
        }

        /// <summary>种一株的三种结果：种下了 / 这格作废（继续下一格）/ 暂时干不了（等下次结算）。</summary>
        private enum SowResult
        {
            Sown,
            Abandoned,
            Blocked
        }

        // ---- 存档状态（键名见 PostExposeData）----
        private ThingDef cropDef;
        private bool enabled = true;
        private float workCarry;
        private int sowsSincePetal;
        private int lastWoodDay;
        private int lastRipenTick;

        /// <summary>
        /// 本花自己创建的那个种植区（规则见 <c>docs/丰饶花种植区生成规则.md</c>，G1–G10）。
        ///
        /// 只是**引用**：区域本身是玩家的资产，本花从不删格、从不删区、从不改别的区域（G10）。
        /// 存档键 <c>botaniaFertilityGrowZone</c>（G8），读档后按 G6 校验一次。
        /// </summary>
        private Zone_Growing growZone;

        // ---- R1 编号状态 ----
        /// <summary>当前编号：半径内每一格，z 降序（屏幕从上到下）→ 同行 x 升序（R1.2）。</summary>
        private List<IntVec3> grid = new List<IntVec3>();
        /// <summary>编号时用的半径（R1.4：弱扫描用它做「范围是否一致」的比较）。</summary>
        private float gridRadius;
        /// <summary>编号时用的中心（同上）。</summary>
        private IntVec3 gridCenter = IntVec3.Invalid;
        /// <summary>R6 标记集合：「上次判定为不可种」的格子坐标（随存档保存）。</summary>
        private List<IntVec3> unplantable = new List<IntVec3>();
        /// <summary>R3 主扫描推进到编号的第几格。</summary>
        private int scanIndex;

        // ---- R4 升级回扫状态 ----
        private Phase phase = Phase.MainScan;
        /// <summary>R4.2 回扫队列 = 旧标记格 + 新增格（R5.5 复用同一个队列字段）。</summary>
        private List<IntVec3> rescanQueue;
        private int rescanIndex;
        /// <summary>上次处理完升级时的档位；与 <see cref="Tier"/> 不等即「档位变了」。</summary>
        private int lastTier = -1;

        // ---- R5 弱扫描状态 ----
        private int nextWeakScanTick;
        private int weakScanIndex;

        // ---- 正在攒工作量的那一株 ----
        /// <summary>R3 判定通过、正在攒工作量准备种的那一格（**只存坐标，不长期持有 Plant 引用**）。</summary>
        private IntVec3 pendingCell = IntVec3.Invalid;
        /// <summary>这一株已经累积的工作点（攒够 <c>workPerPlant</c> 才动手）。</summary>
        private float pendingWork;

        // ---- 运行期缓存（不进存档）----
        private ResearchProjectDef advancedResearchCached;
        private bool advancedResearchResolved;
        private ResearchProjectDef ultimateResearchCached;
        private bool ultimateResearchResolved;

        // gizmo / inspect 每帧都会被调用，范围扫描按 tick 缓存一次即可。
        private int cachedRipenTick = -1;
        private int cachedRipenCount;
        private int cachedTreeTick = -1;
        private int cachedTreeCount;

        // 三态门控用的「范围内有没有可收作物」，同样按 tick 缓存。
        private List<Plant> cachedHarvestables;
        private int cachedHarvestTick = -9999;

        public CompProperties_FertilityFlower Props => (CompProperties_FertilityFlower)props;

        private Map Map => parent.Map;

        public bool Enabled => enabled;

        public ThingDef CropDef => cropDef;

        /// <summary>当前游戏 tick（TickManager 未就绪时给 0）。</summary>
        private static int Now
        {
            get
            {
                TickManager tickManager = Find.TickManager;
                return tickManager != null ? tickManager.TicksGame : 0;
            }
        }

        /// <summary>进阶研究（取木与进阶档的门槛）。</summary>
        private ResearchProjectDef AdvancedResearch
        {
            get
            {
                if (!advancedResearchResolved)
                {
                    advancedResearchCached = Props.AdvancedResearch;
                    advancedResearchResolved = true;
                }
                return advancedResearchCached;
            }
        }

        private ResearchProjectDef UltimateResearch
        {
            get
            {
                if (!ultimateResearchResolved)
                {
                    ultimateResearchCached = Props.UltimateResearch;
                    ultimateResearchResolved = true;
                }
                return ultimateResearchCached;
            }
        }

        /// <summary>研究档位：0 = 基础、1 = 进阶、2 = 终极。</summary>
        private int Tier
        {
            get
            {
                ResearchProjectDef ultimate = UltimateResearch;
                if (ultimate != null && ultimate.IsFinished)
                {
                    return 2;
                }
                ResearchProjectDef advanced = AdvancedResearch;
                if (advanced != null && advanced.IsFinished)
                {
                    return 1;
                }
                return 0;
            }
        }

        private bool HasAdvancedTier => Tier >= 1;

        /// <summary>本档位每照料一株要多少秒（15 / 8 / 1）。</summary>
        private float SecondsPerPlant
        {
            get
            {
                int tier = Tier;
                if (tier >= 2)
                {
                    return Props.secondsPerPlantUltimate;
                }
                return tier == 1 ? Props.secondsPerPlantAdvanced : Props.secondsPerPlantBase;
            }
        }

        /// <summary>
        /// 每秒累积多少工作点 = workPerPlant / SecondsPerPlant（200 点 ÷ 15/8/1 秒 ≈ 13.3/25/200）。
        /// 每株恰好扣 workPerPlant，所以每株耗时恒为 15 / 8 / 1 秒。
        /// </summary>
        private float WorkPerSecond => Props.workPerPlant / SecondsPerPlant;

        /// <summary>一次结算（<c>workIntervalTicks</c> 个 tick）累积的工作点。</summary>
        private float WorkPerSettle => WorkPerSecond * DeviceProps.workIntervalTicks / 60f;

        /// <summary>
        /// 给工作预算加一次结算的量，**并封顶在一株的量**。
        ///
        /// 为什么要封顶：预算本来就是为了保证「每株 15 / 8 / 1 秒」（R7.1）。
        /// 不封顶的话，断魔 / 断花瓣那段时间预算会一直涨，等条件恢复就在一次结算里连做十几株 ——
        /// 面板写着 15 秒一株、实际一秒一片，账对不上。
        /// </summary>
        private void AccrueWork(ref float budget)
        {
            budget += WorkPerSettle;
            if (budget > Props.workPerPlant)
            {
                budget = Props.workPerPlant;
            }
        }

        /// <summary>本档位的产量系数（0.50 / 1.00 / 1.50）。</summary>
        private float YieldFactor
        {
            get
            {
                int tier = Tier;
                if (tier >= 2)
                {
                    return YieldFactorUltimate;
                }
                return tier == 1 ? YieldFactorAdvanced : YieldFactorBase;
            }
        }

        /// <summary>有效半径：基础 + 进阶加成 + 终极加成（5 / 6 / 7 格）。</summary>
        private float EffectiveRadius
        {
            get
            {
                int tier = Tier;
                float value = Props.radius;
                if (tier >= 1)
                {
                    value += Props.radiusAdvancedBonus;
                }
                if (tier >= 2)
                {
                    value += Props.radiusUltimateBonus;
                }
                return value;
            }
        }

        /// <summary>给 <see cref="CompRadiusRing"/> 用：选中时画的环跟着实际半径走。</summary>
        public float CurrentRingRadius => EffectiveRadius;

        /// <summary>当前档位的中文/本地化名称（一档 / 二档 / 三档）。</summary>
        private string TierName
        {
            get
            {
                int tier = Tier;
                if (tier >= 2)
                {
                    return "Botania.Fertility.Tier3".Translate();
                }
                return tier == 1 ? "Botania.Fertility.Tier2".Translate() : "Botania.Fertility.Tier1".Translate();
            }
        }

        // ------------------------------------------------------------------
        // 生命周期（R1.1）
        // ------------------------------------------------------------------

        /// <summary>
        /// R1.1 触发重新编号：建造完成、**重新安装**（从可收纳物品放回地面）、读档。
        ///
        /// 这里只重算编号与「本次范围」，不结算、不碰别的组件（AGENTS §6 铁律）：
        /// 真正的动作留给下一次 <see cref="SettleOnce"/>。
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // G8 / G6：读档（以及重新安装）后校验一次区域引用 —— 失效就只清引用，
            // 区域本身留在原地交给玩家（G6.1 / G6.2 绝不删玩家的区域）。
            if (!ValidGrowZone())
            {
                growZone = null;
            }

            RebuildGrid();                                     // R1.2 立刻按当前半径编号
            lastTier = Tier;                                   // 读档/重装不算「档位变化」
            int now = Now;
            if (!respawningAfterLoad)
            {
                // 新造 / 重新安装：编号已按新位置重建，扫描进度一律从头开始。
                scanIndex = 0;
                phase = Phase.MainScan;
                pendingCell = IntVec3.Invalid;
                pendingWork = 0f;
                workCarry = 0f;
                rescanQueue = null;
                rescanIndex = 0;
                unplantable.Clear();
                weakScanIndex = 0;
                nextWeakScanTick = now + WeakScanIntervalTicks; // R5.1 从放下的那一刻起算 3 小时
                return;
            }
            if (scanIndex > grid.Count)
            {
                scanIndex = 0;
            }
            if (nextWeakScanTick < now)
            {
                nextWeakScanTick = now + WeakScanIntervalTicks;
            }
        }

        /// <summary>
        /// R1.2 重新编号：有效半径内每一格，按 **z 从大到小（屏幕从上到下）**，
        /// 同一行 **x 从小到大（从左到右）** 排成 <see cref="grid"/>，并记下这次用的中心与半径（R1.4）。
        /// </summary>
        private void RebuildGrid()
        {
            // G4.1：先把「上次编号时用的半径」存进局部变量 —— 下面第 3 行 gridRadius 就被覆盖了，
            // 旧值只在这一处比较用得上。
            float oldRadius = gridRadius;
            List<IntVec3> cells = new List<IntVec3>();
            gridCenter = parent.Position;
            gridRadius = EffectiveRadius;
            Map map = Map;
            if (map != null)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(gridCenter, gridRadius, true))
                {
                    if (cell.InBounds(map))
                    {
                        cells.Add(cell);
                    }
                }
            }
            cells.Sort(delegate (IntVec3 a, IntVec3 b)
            {
                int dz = b.z.CompareTo(a.z);              // z 大的在上（屏幕上从上到下）
                return dz != 0 ? dz : a.x.CompareTo(b.x); // 同一行从左到右
            });
            grid = cells;
            // 标记集合只保留仍在新范围里的格（半径只会变大，这里主要为「重装换了位置」清旧标记）。
            HashSet<IntVec3> inRange = new HashSet<IntVec3>(cells);
            unplantable.RemoveAll(cell => !inRange.Contains(cell));

            // G4：编号重建 = 建造 / 重装 / 读档 / 弱扫描重编号（R1.1）；**只有半径真的变大**
            // （档位升级 / 半径记录丢失后重算变大）才把新增一圈里符合 G2 的格静默补进本花区域
            // （G4.1 / G4.3：不弹提示）。半径没变大就绝不自动补格 —— 否则玩家手工从本花种植区
            // 删掉的格子会在读档 / 重装 / 弱扫描时被偷偷补回来。没有区域时什么也不做（G4.2）。
            bool radiusGrew = gridRadius > oldRadius + 0.001f;
            if (radiusGrew && ValidGrowZone())
            {
                ExtendGrowZone(silent: true);
            }
        }

        // ------------------------------------------------------------------
        // 生成 / 补齐种植区（docs/丰饶花种植区生成规则.md，G1–G10）
        //
        // 本花只维护「自己的那一个」区域：新建与补齐都只用 Zone.AddCell，
        // 从不 RemoveCell、从不 Deregister、从不碰别人的区域（G10）。
        // ------------------------------------------------------------------

        /// <summary>
        /// G6 有效性判定（三条全满足才算有效）：
        /// ① 非 null、未被删除、仍在本图 <c>zoneManager.AllZones</c> 里；
        /// ② 仍是 <see cref="Zone_Growing"/>；
        /// ③ 覆盖本花所在格（<see cref="Zone.ContainsCell"/>）。
        ///
        /// 关于「未被删除」：本引擎的 <see cref="Zone"/> 没有 <c>Deleted</c> 成员，
        /// 删除 = <see cref="ZoneManager.DeregisterZone"/> 把它从 <c>AllZones</c> 里摘掉
        /// （<see cref="Zone.RemoveCell"/> 清到 0 格也会摘），所以「仍在 AllZones」就是「没被删」。
        /// </summary>
        private bool ValidGrowZone()
        {
            if (growZone == null)
            {
                return false;
            }
            Map map = Map;
            if (map == null || map.zoneManager == null)
            {
                return false;
            }
            // ① 未被删除：仍在 AllZones 里（顺带挡掉「引用的是别的图上的区域」）。
            if (!map.zoneManager.AllZones.Contains(growZone))
            {
                return false;
            }
            // ② 仍是种植区。
            if (!(growZone is Zone_Growing))
            {
                return false;
            }
            // ③ 覆盖本花所在格：花被搬走后旧区域就不再算「本花的区域」（G6.1 / G9.2）。
            return growZone.ContainsCell(parent.Position);
        }

        /// <summary>
        /// G2 格筛选（三段短路）：越界 → 地形可种 → 格上没有 <c>CanOverlapZones == false</c> 的东西
        /// → 没被别的区域占用。被别的区域占用的格只计数（<paramref name="skipped"/>），绝不夺格（G2.4 / G10）。
        ///
        /// 本花自己有效区域里的格既不算候选、也不算「跳过」（G3.2 补齐只关心「尚未属于本区域」的格）。
        /// </summary>
        private List<IntVec3> CollectGrowZoneCells(out int skipped)
        {
            skipped = 0;
            List<IntVec3> candidates = new List<IntVec3>();
            Map map = Map;
            if (map == null || !parent.Spawned || cropDef == null || cropDef.plant == null)
            {
                return candidates;
            }
            bool ownZoneValid = ValidGrowZone();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, EffectiveRadius, true))
            {
                // G2.1 越界。
                if (!cell.InBounds(map))
                {
                    continue;
                }
                // G2.2 地形可种（肥力 / 地形标签 / 地形黑名单 / 污染，交给引擎判定；
                //      温度不算地形差异，关掉 checkMapTemperature）。
                if (!cropDef.CanEverPlantAt(cell, map, canWipePlantsExceptTree: true, checkMapTemperature: false))
                {
                    continue;
                }
                // G2.3 格上有 CanOverlapZones == false 的东西（墙、建筑、蓝图……）→ 区域本来就画不上去。
                if (CellBlocksZone(cell, map))
                {
                    continue;
                }
                // G2.4 已被任何区域占用 → 跳过并计数，绝不抢格。
                Zone occupying = map.zoneManager.ZoneAt(cell);
                if (occupying != null)
                {
                    if (ownZoneValid && occupying == growZone)
                    {
                        continue;   // 已经是本花区域的格：不重复加，也不计入「被其它区域占用」
                    }
                    skipped++;
                    continue;
                }
                candidates.Add(cell);
            }
            return candidates;
        }

        /// <summary>G2.3 单格判定：格上有任何 <c>CanOverlapZones == false</c> 的东西就放不进区域。</summary>
        private static bool CellBlocksZone(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].def.CanOverlapZones)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// G1 按钮入口 / G3 创建与幂等：
        /// 没有有效区域 → 新建一个 <see cref="Zone_Growing"/> 并铺满候选格；
        /// 已有有效区域 → 只补齐、不重复、不删格。
        /// </summary>
        private void CreateOrExtendGrowZone(bool showMessages)
        {
            Map map = Map;
            if (map == null || !parent.Spawned)
            {
                return;
            }
            // 区域总得有个作物（G3.4）；还没选作物就先让玩家选（按钮本身始终可见，G1.2）。
            if (cropDef == null)
            {
                if (showMessages)
                {
                    Messages.Message("Botania.Fertility.NoCrop".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                return;
            }
            if (ValidGrowZone())
            {
                ExtendGrowZone(silent: !showMessages);
                return;
            }

            int skipped;
            List<IntVec3> candidates = CollectGrowZoneCells(out skipped);
            if (candidates.Count == 0)
            {
                // G3.3 / G10：一格都没有就不创建（空区域会被引擎自动注销）。
                if (showMessages)
                {
                    Messages.Message("Botania.Fertility.ZoneNoCells".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
                return;
            }

            Zone_Growing zone = new Zone_Growing(map.zoneManager);
            zone.SetPlantDefToGrow(cropDef);        // G3.1 / G3.4：先设作物，绝不留「区域存在但作物为空」
            map.zoneManager.RegisterZone(zone);     // G3.1：先注册，AddCell 才写得进 zoneGrid
            for (int i = 0; i < candidates.Count; i++)
            {
                zone.AddCell(candidates[i]);
            }
            growZone = zone;
            if (showMessages)
            {
                Messages.Message("Botania.Fertility.ZoneCreated".Translate(candidates.Count),
                    MessageTypeDefOf.PositiveEvent, false);
                ReportSkippedCells(skipped);
            }
        }

        /// <summary>
        /// G3.2 / G4 补齐：把「半径内符合 G2、且尚未属于本区域」的格加进本花区域，
        /// 顺手把作物同步成当前选择（G5.1 / G5.3：只改作物，不动格集合）。
        /// <paramref name="silent"/> = true 时一个提示都不弹（G4.3）。
        /// </summary>
        private void ExtendGrowZone(bool silent)
        {
            if (!ValidGrowZone())
            {
                return;     // G4.2：本花还没有区域就什么都不做（不自动创建）
            }
            int skipped;
            List<IntVec3> candidates = CollectGrowZoneCells(out skipped);
            int added = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                IntVec3 cell = candidates[i];
                if (growZone.ContainsCell(cell))
                {
                    continue;   // 兜底：AddCell 对已有的格会 Log.Error，这里先挡掉
                }
                growZone.AddCell(cell);
                added++;
            }
            // G5.1：换过作物就同步（新增 0 格也要同步）。
            if (cropDef != null)
            {
                growZone.SetPlantDefToGrow(cropDef);
            }
            if (silent)
            {
                return;     // G4.3：自动扩展不打扰玩家
            }
            Messages.Message("Botania.Fertility.ZoneExtended".Translate(added),
                MessageTypeDefOf.PositiveEvent, false);
            ReportSkippedCells(skipped);
        }

        /// <summary>G7：确实有格被别的区域占掉才提示，一个都没有就不打扰。</summary>
        private static void ReportSkippedCells(int skipped)
        {
            if (skipped <= 0)
            {
                return;
            }
            Messages.Message("Botania.Fertility.ZoneSkipped".Translate(skipped),
                MessageTypeDefOf.NeutralEvent, false);
        }

        // ------------------------------------------------------------------
        // 结算
        // ------------------------------------------------------------------

        // 基类把「想不想干 / 干什么」声明成抽象方法，但它的固定付款流程（WantsWork → TryPay →
        // DoWork）对「按株计价」的花不适用，本类改为直接覆写 SettleOnce。
        // 这两个方法因此永远不会被调用，保留最小实现只是为了满足基类契约。
        protected override bool WantsWork()
        {
            return false;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>
        /// 一次结算：先判三态，再决定这一秒要不要累积工作量，最后推进状态机。
        ///
        /// 三态门控：
        ///   ① 无魔力（<c>Core.FindSource(0f) == null</c>）→ 完全不干活，**且不累积任何工作预算**；
        ///   ② 有魔力 + 无花瓣 → **只能收获**：只有在范围内**确有可收作物**时才累积收获预算；
        ///   ③ 有魔力 + 有花瓣 → 播种 + 收获，正常累积。
        ///
        /// 播种的工作点记在 <see cref="pendingWork"/>（只给已选中的那一格攒，攒够一株就种），
        /// 收获的工作点记在 <see cref="workCarry"/>（每收一株扣 <c>workPerPlant</c>）。
        /// 两者都封顶一株的量，见 <see cref="AccrueWork"/>。
        /// </summary>
        protected override bool SettleOnce()
        {
            Map map = Map;
            if (!enabled || cropDef == null || map == null || !parent.Spawned)
            {
                Core.ScheduleNext(false);
                return false;
            }

            int now = Now;

            // R1.1 ②③ / R4.1：研究完成 → 半径变大。每次结算开头都查一次，变了就立刻回扫
            // （幂等：已经在回扫中不会重置进度，见 BeginUpgradeRescan）。
            if (Tier != lastTier)
            {
                BeginUpgradeRescan(map);
            }

            // 三态门控 ①：连魔力源都没有（断网 / 没池子）→ 停工，不攒预算。
            if (Core.FindSource(0f) == null)
            {
                Core.SetHasMana(false);
                Core.ScheduleNext(false);
                return false;
            }

            // 每夜取木不耗魔力，只要求网络里有魔力，所以放在三态门控之前（与花瓣无关）。
            bool any = DoNightWood(map);

            bool canSow = CanSowNow();
            bool canHarvest = CachedHarvestables(map).Count > 0;

            if (canSow || canHarvest)
            {
                // 二 / 三态：能干活 → 按秒累积收获预算（速度 15 / 8 / 1 秒一株）。
                AccrueWork(ref workCarry);
            }
            else if (!IsWeakPhase(phase))
            {
                // 三态门控 ②：有魔力但没花瓣、范围内也没有可收作物 → 只许收、无活可干 → 不攒预算。
                Core.ScheduleNext(false);
                return false;
            }
            // 注意：弱扫描流程本身「就算没活也要推进」（否则会卡在收完那一格、永远到不了补种），
            // 这时不攒预算、只把阶段往前推。

            // R5.1 弱扫描到期：先让弱扫描跑；主扫描 / 升级回扫的进度都在字段里，可以续上。
            if (!IsWeakPhase(phase) && now >= nextWeakScanTick)
            {
                phase = Phase.Idle;
            }

            if (RunPhase(map, canSow))
            {
                any = true;
            }

            Core.ScheduleNext(any);
            return any;
        }

        /// <summary>按当前阶段推进一步。每个阶段一次结算最多做 <see cref="MaxActionsPerSettle"/> 件事。</summary>
        private bool RunPhase(Map map, bool canSow)
        {
            switch (phase)
            {
                case Phase.MainScan:
                    return StepMainScan(map, canSow);
                case Phase.UpgradeRescan:
                    return StepRescan(map, canSow, upgrade: true);
                case Phase.WeakHarvest:
                    return StepWeakHarvest(map);
                case Phase.WeakReplant:
                    return StepWeakReplant(map, canSow);
                case Phase.WeakRescan:
                    return StepRescan(map, canSow, upgrade: false);
                default:
                    return StepIdle(map);
            }
        }

        private static bool IsWeakPhase(Phase value)
        {
            return value == Phase.WeakHarvest || value == Phase.WeakReplant || value == Phase.WeakRescan;
        }

        // ------------------------------------------------------------------
        // R3 主扫描
        // ------------------------------------------------------------------

        /// <summary>
        /// R3 主扫描：按 <see cref="grid"/> 的编号逐格执行 R2。
        ///   · 判定失败（短路与任一条件不满足）→ 进标记集合、琥珀闪一下、编号 +1；
        ///   · 判定通过 → 设为 <see cref="pendingCell"/>、绿框长亮、开始攒 <see cref="pendingWork"/>；
        ///     攒够一株的工作量后**再按 R2 重判一次**（R6.5）才真正种。
        ///   · 走到第 N 格（<c>scanIndex == grid.Count</c>）且没有待种的一株 → 一轮走完，进 R4。
        /// </summary>
        private bool StepMainScan(Map map, bool canSow)
        {
            bool any = false;
            bool sowAccrued = false;
            int actions = 0;
            while (actions < MaxActionsPerSettle)
            {
                if (pendingCell.IsValid)
                {
                    // 三态门控 ②：没花瓣就只收不种 —— 播种预算一分不攒，绿框保持（亮着直到种下）。
                    if (!canSow)
                    {
                        return any;
                    }
                    // 同格 Add 是覆盖：每次结算续一次时长 = 「亮着直到种下」。
                    TransientCellHighlight.Add(map, pendingCell, TransientCellHighlight.Green, PendingHighlightTicks);
                    if (!sowAccrued)
                    {
                        AccrueWork(ref pendingWork);
                        sowAccrued = true;
                    }
                    if (pendingWork < Props.workPerPlant)
                    {
                        return any;
                    }
                    SowResult result = TrySowPendingCell(map);
                    if (result == SowResult.Blocked)
                    {
                        return any;
                    }
                    if (result == SowResult.Sown)
                    {
                        any = true;
                    }
                    actions++;
                    continue;
                }

                // R3.2 / R3.3：走到第 N 格 —— 此时 pendingCell 一定已经种完（上面的分支先处理）。
                if (scanIndex >= grid.Count)
                {
                    FinishMainRound(map);
                    return any;
                }

                // R3.1 取编号的下一格做 R2 判定（失败也照常推进编号）。
                IntVec3 cell = grid[scanIndex];
                scanIndex++;
                Plant ignored;
                if (!PassesR2(cell, map, out ignored))
                {
                    MarkUnplantable(cell);
                    FlashAmber(map, cell);
                    actions++;
                    continue;
                }
                pendingCell = cell;
                TransientCellHighlight.Add(map, cell, TransientCellHighlight.Green, PendingHighlightTicks);
            }
            return any;
        }

        /// <summary>
        /// R3.2 / R3.3 + R4：一轮走完。第 N 格若可种，上面的循环已经把它种完才会走到这里。
        ///
        /// R4.2 兜底：再检查一次档位（与结算开头是同一份 <see cref="BeginUpgradeRescan"/>，幂等）。
        /// R4.4 未升级：不做回扫，直接进入弱扫描等待。
        /// </summary>
        private void FinishMainRound(Map map)
        {
            if (Tier != lastTier)
            {
                BeginUpgradeRescan(map);
                return;
            }
            phase = Phase.Idle;
        }

        // ------------------------------------------------------------------
        // R4 升级回扫（R4.2 / R4.3）
        // ------------------------------------------------------------------

        /// <summary>
        /// R4.1 / R4.2：档位升级（研究完成）→ 立刻构造回扫队列：
        ///   · ① 之前被标记为「不可种」的格子（<see cref="unplantable"/> 全部）；
        ///   · ② 半径变大后新增的格子（新半径格集合 − 旧 <see cref="grid"/> 集合）。
        ///
        /// **幂等**：已经在 <see cref="Phase.UpgradeRescan"/> 里就原样返回，不重置进度 ——
        /// 因为整个回扫期间 <see cref="Tier"/> 与 <see cref="lastTier"/> 一直不等，结算开头会反复调到这里。
        /// 弱扫描流程进行中同样不打断它，最多等这一轮弱扫描走完。
        /// </summary>
        private void BeginUpgradeRescan(Map map)
        {
            if (phase == Phase.UpgradeRescan || IsWeakPhase(phase))
            {
                return;
            }
            List<IntVec3> queue = new List<IntVec3>();
            for (int i = 0; i < unplantable.Count; i++)
            {
                queue.Add(unplantable[i]);
            }
            // 新增格：新半径内的格 − 旧编号集合，仍按 R1.2 的顺序排好（回扫也照编号走，便于复现）。
            HashSet<IntVec3> oldGrid = new HashSet<IntVec3>(grid);
            List<IntVec3> added = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, EffectiveRadius, true))
            {
                if (!cell.InBounds(map) || oldGrid.Contains(cell))
                {
                    continue;
                }
                added.Add(cell);
            }
            added.Sort(delegate (IntVec3 a, IntVec3 b)
            {
                int dz = b.z.CompareTo(a.z);
                return dz != 0 ? dz : a.x.CompareTo(b.x);
            });
            queue.AddRange(added);
            rescanQueue = queue;
            rescanIndex = 0;
            phase = Phase.UpgradeRescan;
        }

        /// <summary>
        /// 回扫队列（R4.2 升级回扫 / R5.5 补种后回扫共用）：逐个按 R2 判定，
        /// 可种 → 走与主扫描完全相同的「攒工作量 → 重判 → 付款 → 割除 → 同格种下」；
        /// 不可种 → 保持/加入标记并跳下一个。
        /// </summary>
        private bool StepRescan(Map map, bool canSow, bool upgrade)
        {
            if (rescanQueue == null)
            {
                rescanQueue = new List<IntVec3>();
            }
            // 三态门控 ②：没有花瓣时回扫「可种则种」根本做不到，直接收尾 ——
            // 升级回扫把种植交给随后从第 1 格重走的主扫描（编号已按新半径重建），
            // 弱扫描回扫只保留旧标记。不然会停在这里，后面每 3 小时的弱扫描再也到不了期。
            if (!canSow)
            {
                if (upgrade)
                {
                    FinishUpgradeRescan();
                }
                else
                {
                    FinishWeakRescan();
                }
                return false;
            }
            bool any = false;
            bool sowAccrued = false;
            int actions = 0;
            while (actions < MaxActionsPerSettle)
            {
                if (pendingCell.IsValid)
                {
                    TransientCellHighlight.Add(map, pendingCell, TransientCellHighlight.Green, PendingHighlightTicks);
                    if (!sowAccrued)
                    {
                        AccrueWork(ref pendingWork);
                        sowAccrued = true;
                    }
                    if (pendingWork < Props.workPerPlant)
                    {
                        return any;
                    }
                    SowResult result = TrySowPendingCell(map);
                    if (result == SowResult.Blocked)
                    {
                        return any;
                    }
                    if (result == SowResult.Sown)
                    {
                        any = true;
                    }
                    actions++;
                    continue;
                }

                if (rescanIndex >= rescanQueue.Count)
                {
                    if (upgrade)
                    {
                        FinishUpgradeRescan();
                    }
                    else
                    {
                        FinishWeakRescan();
                    }
                    return any;
                }

                IntVec3 cell = rescanQueue[rescanIndex];
                rescanIndex++;
                Plant ignored;
                if (!PassesR2(cell, map, out ignored))
                {
                    MarkUnplantable(cell);
                    FlashAmber(map, cell);
                    actions++;
                    continue;
                }
                pendingCell = cell;
                TransientCellHighlight.Add(map, cell, TransientCellHighlight.Green, PendingHighlightTicks);
            }
            return any;
        }

        /// <summary>R4.3：回扫结束 → 按新范围重新编号 → 回主扫描（R3）。</summary>
        private void FinishUpgradeRescan()
        {
            RebuildGrid();
            scanIndex = 0;
            lastTier = Tier;
            rescanQueue = null;
            rescanIndex = 0;
            phase = Phase.MainScan;
        }

        // ------------------------------------------------------------------
        // R5 弱扫描
        // ------------------------------------------------------------------

        /// <summary>空闲阶段：到点就开弱扫描；没到点就把上一轮没走完的主扫描接着走。</summary>
        private bool StepIdle(Map map)
        {
            int now = Now;
            if (now < nextWeakScanTick)
            {
                // 弱扫描插队过 / 没花瓣暂停过 —— 主扫描还有没走完的部分就接着走。
                if (scanIndex < grid.Count || pendingCell.IsValid)
                {
                    phase = Phase.MainScan;
                }
                return false;
            }

            // R5.2 先比较范围：中心或半径变了（建筑被搬动 / 研究刚完成）→ 重新编号并回主扫描。
            if (gridCenter != parent.Position || !Mathf.Approximately(gridRadius, EffectiveRadius))
            {
                RebuildGrid();
                scanIndex = 0;
                phase = Phase.MainScan;
                nextWeakScanTick = now + WeakScanIntervalTicks;
                return false;
            }

            nextWeakScanTick = now + WeakScanIntervalTicks; // R5.1 下一个 3 小时
            phase = Phase.WeakHarvest;                      // R5.3 先逐格收
            weakScanIndex = 0;
            return false;
        }

        /// <summary>
        /// R5.3 弱扫描第一段：按编号 <c>1 → N</c> 检查该格植物是否**可被魔力收获**，可收则收，
        /// 否则看下一格。**这一遍只收，不补种**；每结算最多处理 <see cref="MaxActionsPerSettle"/> 格。
        ///
        /// 每株固定付 <c>manaPerHarvest</c>、扣一株的工作点（速度 15/8/1 秒），
        /// 产物落在花脚下，收尾与玩家收获一致（<c>PlantCollected(null, Chop)</c>）。
        /// 魔力/工作点不够就停在原地，下次结算从同一格继续（不会漏收）。
        /// </summary>
        private bool StepWeakHarvest(Map map)
        {
            bool any = false;
            int processed = 0;
            while (weakScanIndex < grid.Count && processed < MaxActionsPerSettle)
            {
                IntVec3 cell = grid[weakScanIndex];
                Plant plant = cell.GetPlant(map);
                if (PlantMagicUtil.CanMagicallyHarvest(plant))
                {
                    int amount = PlantMagicUtil.ApplySkillYield(plant.YieldNow(), YieldFactor);
                    if (amount > 0)
                    {
                        if (workCarry < Props.workPerPlant)
                        {
                            return any;      // 速度门控：这一株留到下次结算
                        }
                        if (!Core.TryPay(Props.manaPerHarvest))
                        {
                            return any;      // 魔力不足：这一株留到下次结算
                        }
                        ThingDef productDef = plant.def.plant.harvestedThingDef;
                        if (productDef != null)
                        {
                            PlantMagicUtil.DropThing(productDef, amount, parent.Position, map);
                        }
                        plant.PlantCollected(null, PlantDestructionMode.Chop);
                        workCarry -= Props.workPerPlant;
                        cachedHarvestables = null;
                        any = true;
                    }
                }
                weakScanIndex++;
                processed++;
            }
            if (weakScanIndex >= grid.Count)
            {
                phase = Phase.WeakReplant;   // R5.4 收完 → 回头补种
                weakScanIndex = 0;
            }
            return any;
        }

        /// <summary>
        /// R5.4 弱扫描第二段：按编号顺序把「这一遍被收空的格 + 当前空且可种的格」补种上。
        /// 判定、割除、付款、速度与主扫描完全同一套（R2 / R7），一株一株来。
        ///
        /// 三态门控 ②：没花瓣就一株也补不了 —— 直接跳过本段进入 R5.5，
        /// 否则这一轮会永远卡在补种、下一个 3 小时永远不到期。
        /// </summary>
        private bool StepWeakReplant(Map map, bool canSow)
        {
            if (!canSow)
            {
                BeginWeakRescanAfterReplant();
                return false;
            }

            bool any = false;
            bool sowAccrued = false;
            int actions = 0;
            while (actions < MaxActionsPerSettle)
            {
                if (pendingCell.IsValid)
                {
                    TransientCellHighlight.Add(map, pendingCell, TransientCellHighlight.Green, PendingHighlightTicks);
                    if (!sowAccrued)
                    {
                        AccrueWork(ref pendingWork);
                        sowAccrued = true;
                    }
                    if (pendingWork < Props.workPerPlant)
                    {
                        return any;
                    }
                    SowResult result = TrySowPendingCell(map);
                    if (result == SowResult.Blocked)
                    {
                        return any;
                    }
                    if (result == SowResult.Sown)
                    {
                        any = true;
                    }
                    actions++;
                    continue;
                }

                if (weakScanIndex >= grid.Count)
                {
                    BeginWeakRescanAfterReplant();   // R5.5 补种完 → 回扫标记格
                    return any;
                }

                IntVec3 cell = grid[weakScanIndex];
                weakScanIndex++;
                Plant ignored;
                if (!PassesR2(cell, map, out ignored))
                {
                    MarkUnplantable(cell);
                    FlashAmber(map, cell);
                    actions++;
                    continue;
                }
                pendingCell = cell;
                TransientCellHighlight.Add(map, cell, TransientCellHighlight.Green, PendingHighlightTicks);
            }
            return any;
        }

        /// <summary>R5.5：补种之后回扫一遍「之前被标记为不可种」的格子（R4.2 那类）。</summary>
        private void BeginWeakRescanAfterReplant()
        {
            rescanQueue = new List<IntVec3>(unplantable);
            rescanIndex = 0;
            phase = Phase.WeakRescan;
        }

        /// <summary>R5.6：这一轮弱扫描结束 → 回到空闲，等下一个 3 小时。</summary>
        private void FinishWeakRescan()
        {
            rescanQueue = null;
            rescanIndex = 0;
            phase = Phase.Idle;
            nextWeakScanTick = Now + WeakScanIntervalTicks;
        }

        // ------------------------------------------------------------------
        // R2 单格判定 + 种植动作
        // ------------------------------------------------------------------

        /// <summary>
        /// R2 单格判定（**短路与**，任一失败即「不可种」）。
        ///
        /// 顺序按规格：
        ///   · R2.1 地形可种 —— 肥力 / 地形标签 / 地形黑名单 / 污染，交给引擎
        ///     <c>CanNowPlantAt(canWipePlantsExceptTree: true)</c>（与玩家播种同一套判据）；
        ///   · R2.2 该格没有建筑 —— 同一个调用也覆盖建筑 / 蓝图 / 框架；
        ///   · R2.3 该格没有树 —— <c>canWipe: true</c> 时引擎把树当「可铲的植物」不拦，所以单独判；
        ///   · R2.4 该格没有人工种植的植物（<c>sown</c>，含玩家种的与本花自己种的）—— 同样单独判。
        ///
        /// 注意：R6.5 —— 标记只代表「上次判定的结论」，每次都必须重新调本方法，不许拿旧结论顶替。
        /// </summary>
        /// <param name="plantOnCell">该格上的植物（可能为 null；调用方不要长期持有）。</param>
        private bool PassesR2(IntVec3 cell, Map map, out Plant plantOnCell)
        {
            plantOnCell = null;
            if (cropDef == null || cropDef.plant == null || map == null || !cell.InBounds(map))
            {
                return false;
            }
            // R2.1 地形可种 + R2.2 无建筑（引擎判据；canWipe 让野生草/灌木不算阻挡，树仍会单独判）
            if (!cropDef.CanNowPlantAt(cell, map, canWipePlantsExceptTree: true))
            {
                return false;
            }
            plantOnCell = cell.GetPlant(map);
            if (plantOnCell != null && plantOnCell.Destroyed)
            {
                plantOnCell = null;
            }
            // R2.3 该格没有树
            if (plantOnCell != null && plantOnCell.def?.plant != null && plantOnCell.def.plant.IsTree)
            {
                return false;
            }
            // R2.4 该格没有人工种植的植物
            if (plantOnCell != null && plantOnCell.sown)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 把 <see cref="pendingCell"/> 这一株真正种下去。顺序严格按规格：
        ///   R6.5 种植前**重新判一次该格**（不许拿判定时的旧结论顶替）
        ///   → R7.5 查花瓣（<c>HasFuel</c> 只看不扣）
        ///   → R7.4 <c>TryPay(manaPerSow)</c>（凑够才扣）
        ///   → 扣花瓣（付款成功之后才扣，避免白扣）
        ///   → R2.5/R2.6 割除该格植物（有产出按档位系数给产物）
        ///   → R2.7 <c>Destroy(DestroyMode.Vanish)</c> 彻底清空（不走「收获后回退成长度」那套收尾）
        ///   → 仍被占 → 记不可种、跳过
        ///   → 否则同 tick 同格 <c>SowAt</c> → 熄绿 → 记一株花瓣计数 → 从标记集合移除（R6.2）
        /// </summary>
        private SowResult TrySowPendingCell(Map map)
        {
            IntVec3 cell = pendingCell;
            if (!cell.IsValid)
            {
                return SowResult.Abandoned;
            }

            // R6.5 种植前重判（这一格可能在攒工作量的几十秒里长出了东西 / 被玩家种上了）。
            Plant ignored;
            if (!PassesR2(cell, map, out ignored))
            {
                MarkUnplantable(cell);
                FlashAmber(map, cell);
                pendingCell = IntVec3.Invalid;
                return SowResult.Abandoned;
            }

            // R7.5 花瓣：先查后扣。
            bool needPetal = sowsSincePetal >= Props.sowsPerPetal;
            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            if (needPetal && (refuelable == null || !refuelable.HasFuel))
            {
                return SowResult.Blocked;
            }

            // R7.4 魔力：凑不够就保持 pendingCell 与绿框，下次结算接着种（花瓣一点没动）。
            if (!Core.TryPay(Props.manaPerSow))
            {
                return SowResult.Blocked;
            }
            if (needPetal)
            {
                refuelable.ConsumeFuel(1f);
                sowsSincePetal = 0;
            }

            // R2.5 / R2.6 / R2.7：割除这一格的植物并按档位系数给产物，然后彻底清空。
            WipeCellPlant(cell, map);
            if (cell.GetPlant(map) != null)
            {
                // R2.7 清完仍被植物占着（例如「收获后回退成长度」的灌木）→ 按不可种处理，跳过这格。
                MarkUnplantable(cell);
                FlashAmber(map, cell);
                pendingCell = IntVec3.Invalid;
                return SowResult.Abandoned;
            }

            // R2.5 同一 tick、同一格种下。
            if (PlantMagicUtil.SowAt(cropDef, cell, map) == null)
            {
                MarkUnplantable(cell);
                FlashAmber(map, cell);
                pendingCell = IntVec3.Invalid;
                return SowResult.Abandoned;
            }

            TransientCellHighlight.Remove(map, cell);  // 绿框立刻熄灭
            pendingCell = IntVec3.Invalid;
            pendingWork = 0f;
            sowsSincePetal++;
            UnmarkUnplantable(cell);                   // R6.2 种下即从标记集合移除
            cachedHarvestables = null;
            return SowResult.Sown;
        }

        /// <summary>
        /// R2.6 / R2.7：割除一株植物。
        /// 有产出（已成熟、有 <c>harvestedThingDef</c>）时按档位系数折算产量并掉在花脚下；
        /// 然后一律 <c>Destroy(DestroyMode.Vanish)</c> —— 不能走「收获后回退成长度」的收尾方式，
        /// 否则这一格还占着植物、同格再 <c>SowAt</c> 会两株叠在一起。
        /// </summary>
        private void WipeCellPlant(IntVec3 cell, Map map)
        {
            Plant plant = cell.GetPlant(map);
            if (plant == null || plant.Destroyed)
            {
                return;
            }
            if (plant.def?.plant != null)
            {
                // YieldNow() 在未成熟 / 不可收 / 枯萎时返回 0，草与无产出植物自然也拿不到东西。
                int amount = PlantMagicUtil.ApplySkillYield(plant.YieldNow(), YieldFactor);
                ThingDef productDef = plant.def.plant.harvestedThingDef;
                if (amount > 0 && productDef != null)
                {
                    PlantMagicUtil.DropThing(productDef, amount, parent.Position, map);
                }
            }
            plant.Destroy(DestroyMode.Vanish);
        }

        /// <summary>R6.1：任何扫描路径判定失败都要标记（去重）。</summary>
        private void MarkUnplantable(IntVec3 cell)
        {
            if (!cell.IsValid)
            {
                return;
            }
            if (!unplantable.Contains(cell))
            {
                unplantable.Add(cell);
            }
        }

        /// <summary>R6.2：判定成功并种下的格子，立刻从标记集合移除。</summary>
        private void UnmarkUnplantable(IntVec3 cell)
        {
            if (cell.IsValid)
            {
                unplantable.Remove(cell);
            }
        }

        /// <summary>判定失败的琥珀反馈（45 tick 自己消失）。</summary>
        private void FlashAmber(Map map, IntVec3 cell)
        {
            TransientCellHighlight.Add(map, cell, TransientCellHighlight.Amber, FailHighlightTicks);
        }

        /// <summary>
        /// 「能不能播种」：还没到该扣花瓣的株数（<c>sowsSincePetal &lt; sowsPerPetal</c>），
        /// 或者仓里还有花瓣（<see cref="CompRefuelable.HasFuel"/>）。
        /// 有魔力但没花瓣时这里返回 false —— 那一秒只收货、不播种（三态门控 ②）。
        /// </summary>
        private bool CanSowNow()
        {
            if (cropDef == null || cropDef.plant == null)
            {
                return false;
            }
            if (sowsSincePetal < Props.sowsPerPetal)
            {
                return true;
            }
            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            return refuelable != null && refuelable.HasFuel;
        }

        /// <summary>
        /// 三态门控用：「范围内有没有可收作物」。候选列表按 <see cref="HarvestCacheTicks"/> 缓存，
        /// 播种/收获之后置空强制重算（gizmo/inspect 那一侧另有自己的缓存）。
        /// </summary>
        private List<Plant> CachedHarvestables(Map map)
        {
            int now = Now;
            if (cachedHarvestables == null || now < cachedHarvestTick || now - cachedHarvestTick >= HarvestCacheTicks)
            {
                cachedHarvestables = PlantMagicUtil.FindHarvestablePlants(
                    parent.Position, map, EffectiveRadius, MaxActionsPerSettle);
                cachedHarvestTick = now;
            }
            return cachedHarvestables;
        }

        // ------------------------------------------------------------------
        // 每夜取木
        // ------------------------------------------------------------------

        /// <summary>
        /// 每夜取木：每天只在 woodHour 判定一次（一棵树都没认领成功也算判定过，避免整晚反复扫）。
        ///
        /// 取木**不扣魔力**，只要求「所在魔力网络里有魔力」（<c>Core.FindSource(0f)</c> 只查不扣）。
        /// 同一棵树一天只算一次：认领记录放在 <see cref="MapComponent_TreeClaims"/>（地图级），
        /// 所以两朵花的半径重叠也不会对同一棵树重复出木。
        /// </summary>
        private bool DoNightWood(Map map)
        {
            if (!HasAdvancedTier)
            {
                return false;
            }
            if (GenLocalDate.HourInteger(map) != Props.woodHour)
            {
                return false;
            }
            int day = GenTicks.TicksAbs / GenDate.TicksPerDay;
            if (day == lastWoodDay)
            {
                return false;
            }
            // 没有魔力就今晚不出木，也不写 lastWoodDay —— 同一个小时里来魔了还能补上。
            if (Core.FindSource(0f) == null)
            {
                return false;
            }
            // 判定过了就记下，哪怕一棵树都没认领到。
            lastWoodDay = day;

            if (Props.woodPerTree <= 0 || Props.woodMaxTrees <= 0)
            {
                return false;
            }
            List<Plant> trees = PlantMagicUtil.FindMatureTrees(
                parent.Position, map, EffectiveRadius, Props.woodMaxTrees);
            if (trees.Count == 0)
            {
                return false;
            }
            MapComponent_TreeClaims claims = map.GetComponent<MapComponent_TreeClaims>();
            bool any = false;
            for (int i = 0; i < trees.Count; i++)
            {
                Plant tree = trees[i];
                if (tree == null || tree.Destroyed)
                {
                    continue;
                }
                // 今天别的花（或这朵花上一轮）已经收过这棵 → 跳过，不发木。
                if (claims != null && !claims.TryClaim(tree.thingIDNumber))
                {
                    continue;
                }
                PlantMagicUtil.DropThing(ThingDefOf.WoodLog, Props.woodPerTree, parent.Position, map);
                any = true;
            }
            return any;
        }

        // ------------------------------------------------------------------
        // 存档
        // ------------------------------------------------------------------

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref cropDef, "botaniaFertilityCrop");
            // G8：本花自己创建的那个种植区，只存引用（区域本体由 ZoneManager 深度存档）。
            Scribe_References.Look(ref growZone, "botaniaFertilityGrowZone");
            Scribe_Values.Look(ref enabled, "botaniaFertilityEnabled", true);
            Scribe_Values.Look(ref workCarry, "botaniaFertilityWorkCarry", 0f);
            Scribe_Values.Look(ref sowsSincePetal, "botaniaFertilitySowsSincePetal", 0);
            Scribe_Values.Look(ref lastWoodDay, "botaniaFertilityLastWoodDay", 0);
            Scribe_Values.Look(ref lastRipenTick, "botaniaFertilityLastRipenTick", 0);

            // R1.4 编号（含每格坐标）+ R6.3 标记集合 + 状态机进度，全部随存档保存。
            Scribe_Collections.Look(ref grid, "botaniaFertilityGrid", LookMode.Value);
            Scribe_Values.Look(ref gridRadius, "botaniaFertilityGridRadius", 0f);
            Scribe_Values.Look(ref gridCenter, "botaniaFertilityGridCenter", IntVec3.Invalid);
            Scribe_Collections.Look(ref unplantable, "botaniaFertilityUnplantable", LookMode.Value);
            Scribe_Values.Look(ref scanIndex, "botaniaFertilityScanIndex", 0);
            Scribe_Values.Look(ref phase, "botaniaFertilityPhase", Phase.MainScan);
            Scribe_Collections.Look(ref rescanQueue, "botaniaFertilityRescanQueue", LookMode.Value);
            Scribe_Values.Look(ref rescanIndex, "botaniaFertilityRescanIndex", 0);
            Scribe_Values.Look(ref lastTier, "botaniaFertilityLastTier", -1);
            Scribe_Values.Look(ref nextWeakScanTick, "botaniaFertilityNextWeakScanTick", 0);
            Scribe_Values.Look(ref weakScanIndex, "botaniaFertilityWeakScanIndex", 0);
            Scribe_Values.Look(ref pendingCell, "botaniaFertilityPendingCell", IntVec3.Invalid);
            Scribe_Values.Look(ref pendingWork, "botaniaFertilityPendingWork", 0f);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (grid == null)
                {
                    grid = new List<IntVec3>();
                }
                if (unplantable == null)
                {
                    unplantable = new List<IntVec3>();
                }
            }
        }

        // ------------------------------------------------------------------
        // Gizmo
        // ------------------------------------------------------------------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!parent.Spawned)
            {
                yield break;
            }

            // 1) 选择作物：图标直接用该作物产物的 uiIcon，一眼能看出现在种的是什么。
            yield return new Command_Action
            {
                defaultLabel = "Botania.Fertility.SelectCrop".Translate(CurrentCropLabel()),
                defaultDesc = "Botania.Fertility.SelectCropDesc".Translate(),
                icon = CropIcon(cropDef),
                action = ShowCropMenu
            };

            // 2) 暂停 / 继续。
            yield return new Command_Toggle
            {
                defaultLabel = enabled
                    ? "Botania.Fertility.Pause".Translate()
                    : "Botania.Fertility.Resume".Translate(),
                defaultDesc = "Botania.Fertility.ToggleDesc".Translate(),
                icon = ToggleIcon,
                isActive = () => enabled,
                toggleAction = () =>
                {
                    enabled = !enabled;
                }
            };

            // 3) 生成种植区（G1）：始终可见（G1.2），按当前半径 + 当前作物建一个 Zone_Growing；
            //    已有有效区域时只补齐（G3.2）。
            yield return new Command_Action
            {
                defaultLabel = "Botania.Fertility.ZoneCreate".Translate(),
                defaultDesc = "Botania.Fertility.ZoneCreateDesc".Translate(),
                icon = ZoneCreateIcon,
                action = () => CreateOrExtendGrowZone(showMessages: true)
            };

            // 4) 催熟：只有终极研究完成后才出现。
            ResearchProjectDef ultimate = UltimateResearch;
            if (ultimate != null && ultimate.IsFinished)
            {
                yield return MakeRipenCommand();
            }
        }

        private Gizmo MakeRipenCommand()
        {
            Map map = Map;
            int count = map != null ? CachedRipenPlantCount(map) : 0;
            Command_Action command = new Command_Action
            {
                defaultLabel = "Botania.Fertility.Ripen".Translate(Props.manaPerRipen.ToString("F0")),
                defaultDesc = "Botania.Fertility.RipenDesc".Translate(),
                icon = RipenIcon,
                action = DoRipen
            };

            int remaining = RipenCooldownRemaining();
            if (remaining > 0)
            {
                command.Disabled = true;
                command.disabledReason = "Botania.Fertility.RipenCooldown".Translate(
                    (remaining / (float)GenTicks.TicksPerRealSecond).ToString("F0"));
            }
            else if (count <= 0)
            {
                command.Disabled = true;
                command.disabledReason = "Botania.Fertility.RipenNoPlants".Translate();
            }
            else if (Core.FindSource(Props.manaPerRipen) == null)
            {
                command.Disabled = true;
                command.disabledReason = "Botania.Fertility.RipenNoMana".Translate(
                    Props.manaPerRipen.ToString("F0"));
            }
            return command;
        }

        /// <summary>催熟冷却还剩多少 tick（0 = 可用）。</summary>
        private int RipenCooldownRemaining()
        {
            int elapsed = Now - lastRipenTick;
            int remaining = Props.ripenCooldownTicks - elapsed;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>
        /// 执行一次催熟：固定付 <see cref="CompProperties_FertilityFlower.manaPerRipen"/> 点魔力
        /// （与株数无关），然后范围里**所有非树植物**都过一遍：
        /// 已经熟透的直接按收获流程收走（产量 × 档位系数、产物落在自己脚下），
        /// 还没熟的 <c>Growth += ripenGrowth</c>（上限 1）。
        ///
        /// 顺序是「先检查、再扣款、最后执行」：范围里一株可下手的都没有就一分不扣。
        /// </summary>
        private void DoRipen()
        {
            Map map = Map;
            if (map == null || !parent.Spawned)
            {
                return;
            }
            if (RipenCooldownRemaining() > 0)
            {
                return;
            }
            // 按「真的能动」的株数判断，避免扣了 1000 魔力却一株没动。
            if (PlantMagicUtil.CountRipenablePlants(parent.Position, map, EffectiveRadius) <= 0)
            {
                Messages.Message("Botania.Fertility.RipenNoPlants".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!Core.TryPay(Props.manaPerRipen))
            {
                Messages.Message("Botania.Fertility.RipenNoMana".Translate(
                    Props.manaPerRipen.ToString("F0")), MessageTypeDefOf.RejectInput, false);
                return;
            }
            lastRipenTick = Now;
            PlantMagicUtil.RipenArea(
                parent.Position, map, EffectiveRadius, Props.ripenGrowth, YieldFactor);
            cachedHarvestables = null;
        }

        /// <summary>选择作物的浮动菜单。列表来自 <see cref="PlantMagicUtil.ValidSowableCrops"/>。</summary>
        private void ShowCropMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (ThingDef crop in PlantMagicUtil.ValidSowableCrops())
            {
                ThingDef captured = crop;
                ThingDef product = captured.plant?.harvestedThingDef;
                options.Add(new FloatMenuOption(
                    captured.LabelCap,
                    delegate
                    {
                        cropDef = captured;
                        // 换作物 = 旧的待种格 / 待种进度 / 扫描进度全部作废（绿灯顺手熄掉），
                        // 编号本身仍然有效（格集合没变），所以从主扫描第 1 格重新走。
                        ResetScanState();
                        // G5.1 / G5.3：本花区域的作物跟着换（只改作物，不动格集合；别的区域一概不碰）。
                        if (ValidGrowZone())
                        {
                            growZone.SetPlantDefToGrow(captured);
                        }
                    },
                    product,
                    CropIcon(captured)));
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    "Botania.Fertility.NoCrop".Translate(), null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>换作物时清掉运行进度（编号保留）。</summary>
        private void ResetScanState()
        {
            Map map = Map;
            if (map != null && pendingCell.IsValid)
            {
                TransientCellHighlight.Remove(map, pendingCell);
            }
            pendingCell = IntVec3.Invalid;
            pendingWork = 0f;
            workCarry = 0f;
            scanIndex = 0;
            rescanQueue = null;
            rescanIndex = 0;
            weakScanIndex = 0;
            phase = Phase.MainScan;
            cachedHarvestables = null;
        }

        private string CurrentCropLabel()
        {
            return cropDef != null ? cropDef.LabelCap : "Botania.Fertility.NoCrop".Translate();
        }

        /// <summary>作物图标：优先用产物的 uiIcon，没有产物（或图标缺失）就用原版「重新种植」图标兜底。</summary>
        private static Texture2D CropIcon(ThingDef crop)
        {
            ThingDef product = crop?.plant?.harvestedThingDef;
            if (product != null && product.uiIcon != null)
            {
                return product.uiIcon;
            }
            return TexCommand.Replant;
        }

        /// <summary>暂停/继续按钮的图标（惰性加载，避免静态构造早于内容加载）。</summary>
        private static Texture2D ToggleIcon
        {
            get
            {
                if (toggleIcon == null)
                {
                    toggleIcon = ContentFinder<Texture2D>.Get(ToggleIconPath, false) ?? BaseContent.BadTex;
                }
                return toggleIcon;
            }
        }

        private static Texture2D RipenIcon
        {
            get
            {
                if (ripenIcon == null)
                {
                    ripenIcon = ContentFinder<Texture2D>.Get(RipenIconPath, false) ?? TexCommand.Replant;
                }
                return ripenIcon;
            }
        }

        /// <summary>
        /// 「生成种植区」的图标（G1.1）：原版 <c>UI/Designators/ZoneCreate_Growing</c>。
        /// 本引擎没有 <c>TexCommand.GrowZone</c>，缺图就用「重新种植」图标兜底 —— 绝不留空白方块。
        /// </summary>
        private static Texture2D ZoneCreateIcon
        {
            get
            {
                if (zoneCreateIcon == null)
                {
                    zoneCreateIcon = ContentFinder<Texture2D>.Get(ZoneCreateIconPath, false) ?? TexCommand.Replant;
                }
                return zoneCreateIcon;
            }
        }

        // ------------------------------------------------------------------
        // inspect
        // ------------------------------------------------------------------

        public override string CompInspectStringExtra()
        {
            StringBuilder builder = new StringBuilder();
            // 底行沿用框架：有魔「正在消耗魔力运转」/ 缺魔「附近没有可用的魔力，已停工」。
            string core = base.CompInspectStringExtra();
            if (!core.NullOrEmpty())
            {
                builder.Append(core);
            }
            builder.AppendInNewLine("Botania.Fertility.InspectCrop".Translate(CurrentCropLabel()));

            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            if (refuelable != null)
            {
                builder.AppendInNewLine("Botania.Fertility.InspectPetals".Translate(
                    refuelable.Fuel.ToString("F0"),
                    refuelable.Props.fuelCapacity.ToString("F0")));
            }

            Map map = Map;
            if (map != null && HasAdvancedTier)
            {
                int trees = CachedMatureTreeCount(map);
                builder.AppendInNewLine("Botania.Fertility.InspectWood".Translate(trees * Props.woodPerTree));
            }

            // 等级一行：一档/二档/三档 · 每株 N 秒 · 产收 ×X.XX
            builder.AppendInNewLine("Botania.Fertility.InspectTier".Translate(
                TierName, SecondsPerPlant.ToString("F0"), YieldFactor.ToString("F2")));
            return builder.ToString();
        }

        // ------------------------------------------------------------------
        // 信息卡（右侧「信息」面板的附加条目）
        // ------------------------------------------------------------------

        /// <summary>
        /// 选中时信息卡里的三条：等级 / 每株耗时 / 收成系数。
        /// 明确写出「本档位现在是多少」，否则玩家只能看 inspect 那一行小字。
        /// </summary>
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            IEnumerable<StatDrawEntry> baseStats = base.SpecialDisplayStats();
            if (baseStats != null)
            {
                foreach (StatDrawEntry entry in baseStats)
                {
                    yield return entry;
                }
            }
            // 本引擎里 BasicsNonPawn 存在；万一某个语言包把它去掉了也不至于崩。
            StatCategoryDef category = StatCategoryDefOf.BasicsNonPawn ?? StatCategoryDefOf.Basics;
            yield return new StatDrawEntry(
                category,
                "Botania.Fertility.StatTier".Translate(),
                TierName,
                null,
                3000);
            yield return new StatDrawEntry(
                category,
                "Botania.Fertility.StatSeconds".Translate(),
                SecondsPerPlant.ToString("F0") + " 秒",
                null,
                2900);
            yield return new StatDrawEntry(
                category,
                "Botania.Fertility.StatYield".Translate(),
                "×" + YieldFactor.ToString("F2"),
                null,
                2800);
        }

        // ------------------------------------------------------------------
        // 每帧调用的扫描：按 tick 缓存，选中东西时不会每帧扫一遍范围
        // ------------------------------------------------------------------

        private int CachedRipenPlantCount(Map map)
        {
            int now = Now;
            if (now != cachedRipenTick)
            {
                cachedRipenTick = now;
                cachedRipenCount = PlantMagicUtil.CountNonTreePlants(
                    parent.Position, map, EffectiveRadius);
            }
            return cachedRipenCount;
        }

        private int CachedMatureTreeCount(Map map)
        {
            int now = Now;
            if (now != cachedTreeTick)
            {
                cachedTreeTick = now;
                cachedTreeCount = PlantMagicUtil.CountMatureTrees(
                    parent.Position, map, EffectiveRadius, Props.woodMaxTrees);
            }
            return cachedTreeCount;
        }
    }
}