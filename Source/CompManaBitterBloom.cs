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
    /// </summary>
    public class CompProperties_ManaBitterBloom : CompProperties
    {
        /// <summary>影响半径（格）。</summary>
        public float radius = 8f;

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
        /// <summary>场上所有的苦丽花，经验钩子直接查这张表。</summary>
        public static readonly List<CompManaBitterBloom> All = new List<CompManaBitterBloom>();

        private const int TicksPerSettle = 60;

        /// <summary>这朵花攒下的、还没结算的经验。</summary>
        private readonly Dictionary<Pawn, float> pending = new Dictionary<Pawn, float>();

        private readonly Dictionary<Pawn, int> lastTrivialTick = new Dictionary<Pawn, int>();

        private int ticksAccum;

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
                if (bloom.parent.Position.DistanceTo(pawn.Position) > bloom.Props.radius)
                {
                    continue;
                }
                float current;
                bloom.pending.TryGetValue(pawn, out current);
                bloom.pending[pawn] = current + xp;
            }
        }

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
            CompManaStorage pool = FindPool();
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
            // 零碎劳动的冷却也要存：否则读档就能绕过冷却重复吃经验
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                List<Pawn> pawns = new List<Pawn>(lastTrivialTick.Keys);
                List<int> ticks = new List<int>();
                for (int i = 0; i < pawns.Count; i++)
                {
                    ticks.Add(lastTrivialTick[pawns[i]]);
                }
                Scribe_Collections.Look(ref pawns, "trivialPawns", LookMode.Reference);
                Scribe_Collections.Look(ref ticks, "trivialTicks", LookMode.Value);
            }
            else
            {
                List<Pawn> pawns = null;
                List<int> ticks = null;
                Scribe_Collections.Look(ref pawns, "trivialPawns", LookMode.Reference);
                Scribe_Collections.Look(ref ticks, "trivialTicks", LookMode.Value);
                if (pawns != null && ticks != null)
                {
                    lastTrivialTick.Clear();
                    for (int i = 0; i < pawns.Count && i < ticks.Count; i++)
                    {
                        if (pawns[i] != null)
                        {
                            lastTrivialTick[pawns[i]] = ticks[i];
                        }
                    }
                }
            }

            Scribe_Values.Look(ref FedCount, "fedCount", 0);
            Scribe_Values.Look(ref LastPayout, "lastPayout", 0f);
        }
    }
}
