using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 苦丽花：把半径内小人的「劳动」换成魔力。
    ///
    /// 取信号的方式是<b>直接监听技能经验</b>（<c>Pawn_SkillTracker.Learn</c>）：
    /// 任何真正算得上劳动的行为都会涨经验，盖屋顶这种零成本小活只涨几点、
    /// 缝一件衣服涨几十点，于是收益天然按「干了多少活」分层，不需要给工作类型列白名单，
    /// 也不依赖 job / 账单在哪一刻结束。
    ///
    /// 框架迁移（0.8.4 S2）：找魔力池（<see cref="CompManaProducer.FindTarget"/>）、
    /// 结算节流（基类的 <c>settleIntervalTicks</c>，旧版是自建的 60 tick 累加器）、
    /// 存档键前缀交给 <see cref="CompManaProducer"/>。本文件只保留苦丽花独有的账：
    /// 「经验 → 魔力」计价、零碎小活冷却、劳累 hediff / 心情念头，
    /// 以及<b>按小人逐笔送池</b>的旧行为（每个小人各找一次最近的池子，不合并成一笔）。
    /// </summary>
    public class CompProperties_ManaBitterBloom : CompProperties_ManaProducer
    {
        /// <summary>
        /// 影响半径（格）：这个半径内的小人涨技能经验，花就能吃到。
        /// 旧版 XML 里这项叫 <c>radius</c>；现在基类的 <c>radius</c> 表示「送魔力池的半径」
        /// （旧版 <c>poolRadius</c>），所以这里改名为 <c>workRadius</c>。
        /// </summary>
        public float workRadius = 8f;

        /// <summary>每 1 点经验折多少魔力。</summary>
        public float manaPerXp = 10f;

        /// <summary>一次结算最多产多少魔力。</summary>
        public float maxManaPerWork = 400f;

        /// <summary>低于这个经验值就算「零碎小活」，要吃冷却。</summary>
        public float trivialXpThreshold = 5f;

        /// <summary>零碎小活的冷却（3600 tick = 60 秒）。</summary>
        public int trivialCooldownTicks = 3600;

        /// <summary>每次结算给小人加多少 hediff 严重度。</summary>
        public float fatigueSeverity = 0.25f;

        /// <summary>劳累 hediff。</summary>
        public HediffDef hediffDef;

        /// <summary>配套的心情念头。</summary>
        public ThoughtDef thoughtDef;

        // ---- 本组件自己的存档键（都带基类的 saveKeyPrefix 前缀，避免同一件东西上多个组件平铺撞键） ----

        public string FedCountSaveKey => saveKeyPrefix + "FedCount";
        public string LastPayoutSaveKey => saveKeyPrefix + "LastPayout";
        public string TrivialPawnsSaveKey => saveKeyPrefix + "TrivialPawns";
        public string TrivialTicksSaveKey => saveKeyPrefix + "TrivialTicks";

        // ---- 旧版存档键（老组件没有前缀，直接平铺在 Thing 节点下）；只读不写 ----

        public string legacyFedCountKey = "fedCount";
        public string legacyLastPayoutKey = "lastPayout";
        public string legacyTrivialPawnsKey = "trivialPawns";
        public string legacyTrivialTicksKey = "trivialTicks";

        public CompProperties_ManaBitterBloom()
        {
            compClass = typeof(CompManaBitterBloom);

            // 基类配置：
            //   radius = 6            —— 送魔力池的半径（旧版 poolRadius，旧值 6）
            //   settleIntervalTicks   —— 60 tick 结算一次（旧版自建累加器的 TicksPerSettle）
            //   bufferCapacity = 0    —— 不缓冲：结算到的魔力直接送池（旧版行为）
            //   feedRelays = false    —— 只喂魔力池，不喂中继塔（旧版行为）
            radius = 6f;
            settleIntervalTicks = 60;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaBitterBloom";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (hediffDef == null)
            {
                yield return "hediffDef must be set (work fatigue).";
            }
            if (thoughtDef == null)
            {
                yield return "thoughtDef must be set (mood penalty).";
            }
            if (manaPerXp <= 0f)
            {
                yield return "manaPerXp must be greater than 0.";
            }
            if (maxManaPerWork <= 0f)
            {
                yield return "maxManaPerWork must be greater than 0.";
            }
        }
    }

    public class CompManaBitterBloom : CompManaProducer
    {
        /// <summary>场上所有的苦丽花，经验钩子直接查这张表。</summary>
        public static readonly List<CompManaBitterBloom> All = new List<CompManaBitterBloom>();

        /// <summary>这朵花攒下的、还没结算的经验。</summary>
        private readonly Dictionary<Pawn, float> pending = new Dictionary<Pawn, float>();

        private readonly Dictionary<Pawn, int> lastTrivialTick = new Dictionary<Pawn, int>();

        /// <summary>读档用的临时工作列表：必须在字段上跨阶段存活，局部变量会在引用解析前被丢掉。</summary>
        private List<Pawn> savedPawns;

        private List<int> savedTicks;

        /// <summary>旧版存档键读出来的临时工作列表（只读兼容，读完即丢）。</summary>
        private List<Pawn> legacySavedPawns;

        private List<int> legacySavedTicks;

        private bool cooldownsNeedRebuild;

        /// <summary>累计结算了多少次，供信息栏显示。</summary>
        public int FedCount;

        /// <summary>上一次结算产出多少魔力，供信息栏显示。</summary>
        public float LastPayout;

        public CompProperties_ManaBitterBloom Props => (CompProperties_ManaBitterBloom)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            All.Remove(this);
        }

        /// <summary>有人涨了经验：把这份经验记到覆盖他的每朵花头上。</summary>
        public static void RecordXp(Pawn pawn, float xp)
        {
            if (pawn == null || pawn.Map == null || xp <= 0f || All.Count == 0)
            {
                return;
            }
            for (int i = 0; i < All.Count; i++)
            {
                CompManaBitterBloom bloom = All[i];
                if (bloom == null || bloom.parent == null || !bloom.parent.Spawned || bloom.parent.Map != pawn.Map)
                {
                    continue;
                }
                if (bloom.parent.Position.DistanceTo(pawn.Position) > bloom.Props.workRadius)
                {
                    continue;
                }
                float current;
                bloom.pending.TryGetValue(pawn, out current);
                bloom.pending[pawn] = current + xp;
            }
        }

        /// <summary>
        /// 每 tick 的记账钩子：载入后第一次 tick 才重建冷却表（此时 Pawn 引用已完成解析）。
        /// 结算节流只有基类一处（settleIntervalTicks = 旧版的 60 tick）。
        /// </summary>
        protected override void ExtraTick()
        {
            LoadCooldownsIfNeeded();
        }

        /// <summary>攒到经验才值得结算；没经验时基类什么都不做（与旧版 <c>pending.Count == 0</c> 提前返回一致）。</summary>
        protected override bool WantsProduce()
        {
            return pending.Count > 0;
        }

        /// <summary>
        /// 结算「攒下的经验 → 魔力」。
        ///
        /// 返回 0：旧行为是<b>每个小人各找一次最近的魔力池再 AddMana</b>
        /// （池子满了会跳到下一个），合并成一整笔交给基类推送会改变多池 / 池子快满时的目标选择，
        /// 因此这里逐笔付款，不触发基类的整笔推送。
        /// </summary>
        protected override float ProducePerSettle(int ticks)
        {
            SettlePendingXp();
            return 0f;
        }

        /// <summary>载入后第一次 tick 才重建冷却表（此时 Pawn 引用已完成解析）。</summary>
        private void LoadCooldownsIfNeeded()
        {
            if (!cooldownsNeedRebuild)
            {
                return;
            }
            cooldownsNeedRebuild = false;
            lastTrivialTick.Clear();
            if (savedPawns == null || savedTicks == null)
            {
                return;
            }
            for (int i = 0; i < savedPawns.Count && i < savedTicks.Count; i++)
            {
                if (savedPawns[i] != null)
                {
                    lastTrivialTick[savedPawns[i]] = savedTicks[i];
                }
            }
            savedPawns = null;
            savedTicks = null;
        }

        private void SettlePendingXp()
        {
            if (pending.Count == 0)
            {
                return;
            }

            List<Pawn> pawns = new List<Pawn>(pending.Keys);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                float xp;
                if (!pending.TryGetValue(pawn, out xp))
                {
                    continue;
                }
                pending.Remove(pawn);

                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != parent.Map)
                {
                    continue;
                }
                if (xp <= 0f)
                {
                    continue;
                }

                if (xp < Props.trivialXpThreshold)
                {
                    int last;
                    if (lastTrivialTick.TryGetValue(pawn, out last) && now - last < Props.trivialCooldownTicks)
                    {
                        // 零碎小活还在冷却：这轮经验作废，也不挂劳累
                        continue;
                    }
                    lastTrivialTick[pawn] = now;
                }

                float amount = Math.Min(Props.maxManaPerWork, xp * Props.manaPerXp);
                Pay(amount);
                ApplyFatigue(pawn);
            }
        }

        private void Pay(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            FedCount++;
            LastPayout = amount;
            // 基类统一找池子：半径 = 旧版的 poolRadius，条件 = AcceptsMana 且未满（feedRelays = false）
            IManaSource pool = FindTarget();
            if (pool != null)
            {
                pool.AddMana(amount);
            }
        }

        private void ApplyFatigue(Pawn pawn)
        {
            if (Props.hediffDef != null)
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                if (hediff == null)
                {
                    hediff = pawn.health.AddHediff(Props.hediffDef);
                }
                if (hediff != null)
                {
                    float max = Props.hediffDef.maxSeverity > 0f ? Props.hediffDef.maxSeverity : 1f;
                    hediff.Severity = Math.Min(max, hediff.Severity + Props.fatigueSeverity);
                }
            }

            if (Props.thoughtDef != null && pawn.needs != null && pawn.needs.mood != null)
            {
                bool already = false;
                foreach (Thought_Memory memory in pawn.needs.mood.thoughts.memories.Memories)
                {
                    if (memory.def == Props.thoughtDef)
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(Props.thoughtDef);
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            // 文案沿用旧版 Keyed（半径 / 已喂次数 / 上次产出），不走基类的通用「有池 / 无池」文案。
            return "Botania.BitterBloomStatus".Translate(
                Props.workRadius.ToString("0"),
                FedCount,
                LastPayout.ToString("0"));
        }

        public override void PostExposeData()
        {
            // 基类负责：botaniaBitterBloomBuffer / botaniaBitterBloomNextSettleTick
            base.PostExposeData();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 新键（带前缀）
                Scribe_Values.Look(ref FedCount, Props.FedCountSaveKey, 0);
                Scribe_Values.Look(ref LastPayout, Props.LastPayoutSaveKey, 0f);
                Scribe_Collections.Look(ref savedPawns, Props.TrivialPawnsSaveKey, LookMode.Reference);
                Scribe_Collections.Look(ref savedTicks, Props.TrivialTicksSaveKey, LookMode.Value);

                // 旧版存档键：只读不写（回写会和新键重复占坑）。
                // pawn 引用的列表在本阶段只是登记 loadID，真正的解析在 ResolvingCrossRefs。
                Scribe_Values.Look(ref FedCount, Props.legacyFedCountKey, FedCount);
                Scribe_Values.Look(ref LastPayout, Props.legacyLastPayoutKey, LastPayout);
                Scribe_Collections.Look(ref legacySavedPawns, Props.legacyTrivialPawnsKey, LookMode.Reference);
                Scribe_Collections.Look(ref legacySavedTicks, Props.legacyTrivialTicksKey, LookMode.Value);
                return;
            }

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                // 引用列表必须在引用解析阶段再走一遍，才会从 loadID 变成真正的 Pawn。
                Scribe_Collections.Look(ref savedPawns, Props.TrivialPawnsSaveKey, LookMode.Reference);
                Scribe_Collections.Look(ref legacySavedPawns, Props.legacyTrivialPawnsKey, LookMode.Reference);
                if (savedTicks == null)
                {
                    // 新键没有冷却表 → 这是旧档，接住旧键读出来的那份
                    savedPawns = legacySavedPawns;
                    savedTicks = legacySavedTicks;
                }
                legacySavedPawns = null;
                legacySavedTicks = null;
                // 冷却表的重建推迟到第一次 tick（此时 Pawn 引用才有效）。
                cooldownsNeedRebuild = savedPawns != null && savedTicks != null;
                return;
            }

            // 保存阶段：只写新键
            Scribe_Collections.Look(ref savedPawns, Props.TrivialPawnsSaveKey, LookMode.Reference);
            Scribe_Collections.Look(ref savedTicks, Props.TrivialTicksSaveKey, LookMode.Value);
        }
    }
}
