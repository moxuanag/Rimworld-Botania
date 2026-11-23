using System.Collections.Generic;
using System.Xml;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 阿卡纳蔷薇：把附近小人「还没学完的那部分」抽成魔力。
    /// 范围内的小人会带上「灵思被引」——学得更慢、心情更差；已经全技能满级的人抽不出东西。
    ///
    /// 框架迁移（0.8.4 S2）：找魔力池 / 结算节流 / 存档键交给 <see cref="CompManaProducer"/>，
    /// 本文件只保留「谁在学、每人抽多少、hediff / 念头怎么挂」。
    /// </summary>
    public class CompProperties_ManaRosaArcana : CompProperties_ManaProducer
    {
        /// <summary>
        /// 抽取半径（格）。旧字段名是 radius；
        /// 因为基类 <see cref="CompProperties_ManaProducer.radius"/> 是「找魔力池的半径」，
        /// 这里必须改名，避免同名（XML 里旧的 &lt;radius&gt; 需同步改成 &lt;siphonRadius&gt;）。
        /// </summary>
        public float siphonRadius = 8f;

        /// <summary>每个可抽取的小人每秒产出多少魔力。</summary>
        public float manaPerPawnSecond = 6f;

        /// <summary>抽取时给小人挂的 hediff。</summary>
        public HediffDef hediffDef;

        /// <summary>
        /// 心情惩罚用的记忆念头。Mood 是 Need 不是 Stat，
        /// 写进 hediff 的 statOffsets 会让提示框空引用，所以心情必须走 Thought。
        /// </summary>
        public ThoughtDef thoughtDef;

        public CompProperties_ManaRosaArcana()
        {
            compClass = typeof(CompManaRosaArcana);

            // 基类配置：每秒结算一次、无缓冲、只喂魔力池（行为与旧版一致）；
            // 送魔半径沿用旧 poolRadius = 6（XML 里旧的 <poolRadius> 需同步改成 <radius>）。
            radius = 6f;
            settleIntervalTicks = 60;
            bufferCapacity = 0f;
            feedRelays = false;
            saveKeyPrefix = "botaniaRosaArcana";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (hediffDef == null)
            {
                yield return "hediffDef must be set (the siphoning hediff).";
            }
            if (manaPerPawnSecond <= 0f)
            {
                yield return "manaPerPawnSecond must be greater than 0.";
            }
        }
    }

    public class CompManaRosaArcana : CompManaProducer
    {
        /// <summary>场上所有蔷薇，用来判断「这个小人是否还被别的花管着」（读档后自动自洽）。</summary>
        public static readonly List<CompManaRosaArcana> All = new List<CompManaRosaArcana>();

        /// <summary>存档键：前缀 + 字段名（本引擎把组件字段平铺在 Thing 节点下，键必须唯一）。</summary>
        private const string SiphoningSaveKey = "botaniaRosaArcanaSiphoning";

        /// <summary>旧档里这个列表的键；只读不写（保住老档里正在被抽取的小人）。</summary>
        private const string LegacySiphoningSaveKey = "siphoningPawns";

        /// <summary>正在被抽取的小人，离开范围时要摘掉 hediff。</summary>
        private List<Pawn> siphoning = new List<Pawn>();

        /// <summary>上一次采样时各小人的累计经验，用来判断「这一秒他到底有没有在学」。</summary>
        private Dictionary<Pawn, float> lastXp = new Dictionary<Pawn, float>();

        // 读档标记：必须跨 LoadingVars → ResolvingCrossRefs 两个阶段存活，
        // 因为引用列表要在这两阶段各读一次，且两个阶段都必须使用同一个键。
        private bool siphoningWasScribed;
        private bool useLegacySiphoningKey;

        public CompProperties_ManaRosaArcana Props => (CompProperties_ManaRosaArcana)props;

        /// <summary>上一次结算时范围内的总人数 / 正在被抽取的人数，供信息栏显示。</summary>
        public int LastInRangeCount { get; private set; }

        public int LastLearningCount { get; private set; }

        /// <summary>
        /// 一次结算（基类每 settleIntervalTicks = 60 tick 调用一次）：
        /// 收集范围内小人 → 挑出「还在学」的 → 按人数折算魔力 → 挂 hediff/念头 → 清理离场小人。
        /// 魔力入池由基类负责（本设备无缓冲：直接送半径内最近的魔力池）。
        /// 旧行为是每结算固定 +manaPerPawnSecond（结算间隔固定 60 tick = 1 秒），这里保持。
        /// </summary>
        protected override float ProducePerSettle(int ticks)
        {
            List<Pawn> inRange = new List<Pawn>();
            Map map = parent.Map;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.siphonRadius, true))
            {
                Pawn pawn = thing as Pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed)
                {
                    continue;
                }
                if (!pawn.RaceProps.Humanlike || pawn.skills == null)
                {
                    continue;
                }
                inRange.Add(pawn);
            }

            float mana = 0f;
            int learningCount = 0;
            foreach (Pawn pawn in inRange)
            {
                if (!HasSomethingLeftToLearn(pawn))
                {
                    continue;
                }

                // 「在学习」= 这一秒真的长了经验，或者正在做研究。
                // 加研究这一条是因为技能有每日经验上限：跑了一整天的小人即使还在研究，
                // 也已经涨不动经验了，只看增量会让他突然不产魔。
                float totalXp = TotalSkillXp(pawn);
                float previousXp;
                bool wasTracked = lastXp.TryGetValue(pawn, out previousXp);
                lastXp[pawn] = totalXp;
                bool xpGrew = wasTracked && totalXp > previousXp;
                if (!xpGrew && !IsResearching(pawn))
                {
                    continue;
                }

                learningCount++;
                mana += Props.manaPerPawnSecond;
                EnsureHediff(pawn);
            }

            LastInRangeCount = inRange.Count;
            LastLearningCount = learningCount;

            CleanupHediffs(inRange);
            PruneXpSnapshots(inRange);

            return mana;
        }

        /// <summary>正在做研究的人算「在学习」——技能每日上限满了也不至于突然停产。</summary>
        private static bool IsResearching(Pawn pawn)
        {
            JobDef job = pawn.CurJobDef;
            return job != null && job.defName == "Research";
        }

        /// <summary>所有技能累计经验之和，用来做「有没有在学习」的差分。</summary>
        private static float TotalSkillXp(Pawn pawn)
        {
            float total = 0f;
            foreach (SkillDef skillDef in DefDatabase<SkillDef>.AllDefs)
            {
                SkillRecord record = pawn.skills.GetSkill(skillDef);
                if (record == null)
                {
                    continue;
                }
                total += record.XpTotalEarned;
            }
            return total;
        }

        /// <summary>全技能满级（或全禁用）的小人抽不出魔力。</summary>
        private static bool HasSomethingLeftToLearn(Pawn pawn)
        {
            foreach (SkillDef skillDef in DefDatabase<SkillDef>.AllDefs)
            {
                SkillRecord record = pawn.skills.GetSkill(skillDef);
                if (record == null || record.TotallyDisabled)
                {
                    continue;
                }
                if (record.Level < 20)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 释放这个小人：只有当没有任何别的蔷薇还在管他时，才摘掉 hediff。
        /// 不依赖静态计数——读档、换档、花被拆都不会让计数失真。
        /// </summary>
        private void ReleasePawn(Pawn pawn)
        {
            siphoning.Remove(pawn);
            if (pawn == null || Props.hediffDef == null || pawn.health == null)
            {
                return;
            }
            for (int i = 0; i < All.Count; i++)
            {
                CompManaRosaArcana other = All[i];
                if (other != null && other != this && other.siphoning != null && other.siphoning.Contains(pawn))
                {
                    return;
                }
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private void EnsureHediff(Pawn pawn)
        {
            if (Props.hediffDef != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                if (existing == null)
                {
                    pawn.health.AddHediff(Props.hediffDef);
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

            if (!siphoning.Contains(pawn))
            {
                siphoning.Add(pawn);
            }
        }

        /// <summary>把离开范围的小人身上的 hediff 摘掉。</summary>
        private void CleanupHediffs(List<Pawn> stillInRange)
        {
            if (siphoning.Count == 0)
            {
                return;
            }
            for (int i = siphoning.Count - 1; i >= 0; i--)
            {
                Pawn pawn = siphoning[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned)
                {
                    // 死亡/离开地图的小人也要归还占用，否则 hediff 会永久留在身上
                    siphoning.RemoveAt(i);
                    ReleasePawn(pawn);
                    continue;
                }
                if (stillInRange.Contains(pawn))
                {
                    continue;
                }
                siphoning.RemoveAt(i);
                ReleasePawn(pawn);
            }
        }

        /// <summary>清掉已经离开范围的小人的经验快照，避免字典越攒越大。</summary>
        private void PruneXpSnapshots(List<Pawn> stillInRange)
        {
            if (lastXp.Count == 0)
            {
                return;
            }
            List<Pawn> stale = null;
            foreach (Pawn pawn in lastXp.Keys)
            {
                if (pawn == null || !stillInRange.Contains(pawn))
                {
                    if (stale == null)
                    {
                        stale = new List<Pawn>();
                    }
                    stale.Add(pawn);
                }
            }
            if (stale != null)
            {
                foreach (Pawn pawn in stale)
                {
                    lastXp.Remove(pawn);
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            return "Botania.RosaArcanaSiphoning".Translate(
                LastLearningCount,
                LastInRangeCount,
                Props.manaPerPawnSecond.ToString("0"));
        }

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
            All.Remove(this);
            ReleaseAll();
            base.PostDeSpawn(map, mode);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            All.Remove(this);
            ReleaseAll();
            base.PostDestroy(mode, previousMap);
        }

        private void ReleaseAll()
        {
            List<Pawn> pawns = new List<Pawn>(siphoning);
            siphoning.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                ReleasePawn(pawns[i]);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            ScribeSiphoning();
            if (siphoning == null)
            {
                siphoning = new List<Pawn>();
            }
        }

        /// <summary>
        /// 只保留旧版那一个字段的存档：正在被抽取的小人列表。
        /// 键带前缀（本引擎平铺字段，同 Thing 上多个组件不能用同名键）；
        /// 旧档的键是 siphoningPawns —— 没有新键时退回旧键读取，旧键不回写。
        /// Reference 列表必须在 LoadingVars 和 ResolvingCrossRefs 两个阶段
        /// 用同一个键各读一次，否则引用解析会拿不到数据。
        /// </summary>
        private void ScribeSiphoning()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Collections.Look(ref siphoning, SiphoningSaveKey, LookMode.Reference);
                return;
            }
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs)
            {
                return;
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // LoadingVars 期间 curXmlParent 就是当前 Thing 节点（组件字段平铺）；
                // 拿不到节点时按新档处理，不去碰旧键，避免残留未消费的引用注册。
                XmlNode thingNode = Scribe.loader.curXmlParent;
                bool hasNewKey = thingNode != null && thingNode[SiphoningSaveKey] != null;
                bool hasLegacyKey = thingNode != null && thingNode[LegacySiphoningSaveKey] != null;
                useLegacySiphoningKey = !hasNewKey && hasLegacyKey;
                siphoningWasScribed = hasNewKey || hasLegacyKey;
            }
            if (!siphoningWasScribed)
            {
                return;
            }
            Scribe_Collections.Look(
                ref siphoning,
                useLegacySiphoningKey ? LegacySiphoningSaveKey : SiphoningSaveKey,
                LookMode.Reference);
        }
    }
}
