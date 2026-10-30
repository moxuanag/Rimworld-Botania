using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 阿卡纳蔷薇：把附近小人「还没学完的那部分」抽成魔力。
    /// 范围内的小人会带上「灵思被引」——学得更慢、心情更差；已经全技能满级的人抽不出东西。
    /// </summary>
    public class CompProperties_ManaRosaArcana : CompProperties
    {
        /// <summary>影响半径（格）。</summary>
        public float radius = 8f;

        /// <summary>每个可抽取的小人每秒产出多少魔力。</summary>
        public float manaPerPawnSecond = 6f;

        /// <summary>抽取时给小人挂的 hediff。</summary>
        public HediffDef hediffDef;

        /// <summary>
        /// 心情惩罚用的记忆念头。Mood 是 Need 不是 Stat，
        /// 写进 hediff 的 statOffsets 会让提示框空引用，所以心情必须走 Thought。
        /// </summary>
        public ThoughtDef thoughtDef;

        /// <summary>向魔力池输送的半径。</summary>
        public float poolRadius = 6f;

        public CompProperties_ManaRosaArcana()
        {
            compClass = typeof(CompManaRosaArcana);
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

    public class CompManaRosaArcana : ThingComp
    {
        /// <summary>场上所有蔷薇，用来判断「这个小人是否还被别的花管着」（读档后自动自洽）。</summary>
        public static readonly List<CompManaRosaArcana> All = new List<CompManaRosaArcana>();

        private const int TicksPerSettle = 60;

        private int ticksAccum;

        /// <summary>正在被抽取的小人，离开范围时要摘掉 hediff。</summary>
        private List<Pawn> siphoning = new List<Pawn>();

        /// <summary>上一次采样时各小人的累计经验，用来判断「这一秒他到底有没有在学」。</summary>
        private Dictionary<Pawn, float> lastXp = new Dictionary<Pawn, float>();

        public CompProperties_ManaRosaArcana Props => (CompProperties_ManaRosaArcana)props;

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < TicksPerSettle)
            {
                return;
            }
            ticksAccum = 0;
            Settle();
        }

        private void Settle()
        {
            List<Pawn> inRange = new List<Pawn>();
            Map map = parent.Map;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
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

            if (mana > 0f)
            {
                CompManaStorage pool = FindPool();
                if (pool != null)
                {
                    pool.AddMana(mana);
                }
            }

            CleanupHediffs(inRange);
            PruneXpSnapshots(inRange);
        }

        /// <summary>上一次结算时范围内的总人数 / 正在被抽取的人数，供信息栏显示。</summary>
        public int LastInRangeCount { get; private set; }

        public int LastLearningCount { get; private set; }

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

        private CompManaStorage FindPool()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.poolRadius, true))
            {
                CompManaStorage storage = thing.TryGetComp<CompManaStorage>();
                if (storage != null && storage.AcceptsMana && !storage.IsFull)
                {
                    return storage;
                }
            }
            return null;
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
            Scribe_Collections.Look(ref siphoning, "siphoningPawns", LookMode.Reference);
            if (siphoning == null)
            {
                siphoning = new List<Pawn>();
            }
        }
    }
}
