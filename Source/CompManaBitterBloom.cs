using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 苦丽花：半径内的小人每完成一件工作，就被抽走一点精力——
    /// 挂上「劳累」hediff（移动速度下降）与同名的记忆念头（心情下降），
    /// 花则把这份劳累换成魔力。
    ///
    /// 产量按「这次工作实际涨了多少经验」折算：盖屋顶那种零成本小活只涨几点经验，
    /// 收益自然趋近于零；缝一件衣服涨几十点，才有像样的产出。
    /// 极小的活还额外吃一条冷却，堵死「反复拆建屋顶」这类刷法。
    /// </summary>
    public class CompProperties_ManaBitterBloom : CompProperties
    {
        /// <summary>影响半径（格）。</summary>
        public float radius = 8f;

        /// <summary>每 1 点经验折多少魔力。</summary>
        public float manaPerXp = 10f;

        /// <summary>一次工作最多产多少魔力（给高经验工作封顶）。</summary>
        public float maxManaPerWork = 400f;

        /// <summary>低于这个经验值就算「零碎小活」，要吃冷却。</summary>
        public float trivialXpThreshold = 5f;

        /// <summary>零碎小活的冷却（3600 tick = 60 秒）。</summary>
        public int trivialCooldownTicks = 3600;

        /// <summary>每次工作给小人加多少 hediff 严重度。</summary>
        public float fatigueSeverity = 0.25f;

        /// <summary>劳累 hediff。</summary>
        public HediffDef hediffDef;

        /// <summary>配套的心情念头。</summary>
        public ThoughtDef thoughtDef;

        /// <summary>向魔力池输送的半径。</summary>
        public float poolRadius = 6f;

        public CompProperties_ManaBitterBloom()
        {
            compClass = typeof(CompManaBitterBloom);
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

    public class CompManaBitterBloom : ThingComp
    {
        /// <summary>场上所有的苦丽花，工作完成时由 Harmony 补丁直接查这张表。</summary>
        public static readonly List<CompManaBitterBloom> All = new List<CompManaBitterBloom>();

        /// <summary>各小人上一次被抽时的累计经验，用来算「这次工作涨了多少」。</summary>
        private static readonly Dictionary<Pawn, float> LastXp = new Dictionary<Pawn, float>();

        /// <summary>零碎小活的上一次结算 tick，用来做冷却。</summary>
        private static readonly Dictionary<Pawn, int> LastTrivialTick = new Dictionary<Pawn, int>();

        /// <summary>累计喂了多少次工作，供信息栏显示。</summary>
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

        /// <summary>小人完成一件工作：范围内每朵苦丽花各收一份魔力。</summary>
        public static void OnWorkCompleted(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return;
            }

            float xp = ConsumeXp(pawn);
            if (xp <= 0f)
            {
                // 一点经验都没涨（例如原地等待），不算干过活
                return;
            }

            List<CompManaBitterBloom> inRange = new List<CompManaBitterBloom>();
            for (int i = 0; i < All.Count; i++)
            {
                CompManaBitterBloom bloom = All[i];
                if (bloom == null || bloom.parent == null || !bloom.parent.Spawned || bloom.parent.Map != pawn.Map)
                {
                    continue;
                }
                if (bloom.parent.Position.DistanceTo(pawn.Position) > bloom.Props.radius)
                {
                    continue;
                }
                inRange.Add(bloom);
            }
            if (inRange.Count == 0)
            {
                return;
            }

            CompProperties_ManaBitterBloom first = inRange[0].Props;
            int now = Find.TickManager.TicksGame;
            if (xp < first.trivialXpThreshold)
            {
                int last;
                if (LastTrivialTick.TryGetValue(pawn, out last) && now - last < first.trivialCooldownTicks)
                {
                    // 零碎小活还在冷却里：不产魔，也不挂劳累，免得刷子靠它养心情惩罚之外的收益
                    return;
                }
                LastTrivialTick[pawn] = now;
            }

            for (int i = 0; i < inRange.Count; i++)
            {
                CompManaBitterBloom bloom = inRange[i];
                float amount = Math.Min(bloom.Props.maxManaPerWork, xp * bloom.Props.manaPerXp);
                bloom.Pay(amount);
            }
            ApplyFatigue(pawn, first);
        }

        /// <summary>取「这次工作涨的经验」并刷新快照。</summary>
        private static float ConsumeXp(Pawn pawn)
        {
            float total = TotalSkillXp(pawn);
            float previous;
            bool tracked = LastXp.TryGetValue(pawn, out previous);
            LastXp[pawn] = total;
            if (!tracked)
            {
                return 0f;
            }
            return Math.Max(0f, total - previous);
        }

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

        private void Pay(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            FedCount++;
            LastPayout = amount;
            CompManaStorage pool = FindPool();
            if (pool != null)
            {
                pool.AddMana(amount);
            }
        }

        /// <summary>给小人挂劳累 hediff 与心情念头。</summary>
        private static void ApplyFatigue(Pawn pawn, CompProperties_ManaBitterBloom props)
        {
            if (props.hediffDef != null)
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
                if (hediff == null)
                {
                    hediff = pawn.health.AddHediff(props.hediffDef);
                }
                if (hediff != null)
                {
                    float max = props.hediffDef.maxSeverity > 0f ? props.hediffDef.maxSeverity : 1f;
                    hediff.Severity = Math.Min(max, hediff.Severity + props.fatigueSeverity);
                }
            }

            if (props.thoughtDef != null && pawn.needs != null && pawn.needs.mood != null)
            {
                bool already = false;
                foreach (Thought_Memory memory in pawn.needs.mood.thoughts.memories.Memories)
                {
                    if (memory.def == props.thoughtDef)
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(props.thoughtDef);
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
            return "Botania.BitterBloomStatus".Translate(
                Props.radius.ToString("0"),
                FedCount,
                LastPayout.ToString("0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref FedCount, "fedCount", 0);
            Scribe_Values.Look(ref LastPayout, "lastPayout", 0f);
        }
    }
}
