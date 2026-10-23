using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 苦丽花：半径内的小人每完成一件工作，就被抽走一点精力——
    /// 挂上「劳累」hediff（移动速度下降）与同名的记忆念头（心情下降），
    /// 花则把这份劳累换成魔力。不做事的人喂不饱它。
    /// </summary>
    public class CompProperties_ManaBitterBloom : CompProperties
    {
        /// <summary>影响半径（格）。</summary>
        public float radius = 8f;

        /// <summary>一次工作完成产出多少魔力。</summary>
        public float manaPerWork = 400f;

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
            if (manaPerWork <= 0f)
            {
                yield return "manaPerWork must be greater than 0.";
            }
        }
    }

    public class CompManaBitterBloom : ThingComp
    {
        /// <summary>场上所有的苦丽花，工作完成时由 Harmony 补丁直接查这张表。</summary>
        public static readonly List<CompManaBitterBloom> All = new List<CompManaBitterBloom>();

        /// <summary>累计喂了多少次工作，供信息栏显示。</summary>
        public int FedCount;

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

            bool fed = false;
            for (int i = All.Count - 1; i >= 0; i--)
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
                bloom.PayMana();
                fed = true;
            }

            if (fed)
            {
                ApplyFatigue(pawn, All);
            }
        }

        private void PayMana()
        {
            FedCount++;
            CompManaStorage pool = FindPool();
            if (pool != null)
            {
                pool.AddMana(Props.manaPerWork);
            }
        }

        /// <summary>给小人挂劳累 hediff 与心情念头（同一朵花的配置即可）。</summary>
        private static void ApplyFatigue(Pawn pawn, List<CompManaBitterBloom> all)
        {
            CompProperties_ManaBitterBloom first = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].parent != null && all[i].parent.Spawned)
                {
                    first = all[i].Props;
                    break;
                }
            }
            if (first == null)
            {
                return;
            }

            if (first.hediffDef != null)
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(first.hediffDef);
                if (hediff == null)
                {
                    hediff = pawn.health.AddHediff(first.hediffDef);
                }
                if (hediff != null)
                {
                    float max = first.hediffDef.maxSeverity > 0f ? first.hediffDef.maxSeverity : 1f;
                    hediff.Severity = Math.Min(max, hediff.Severity + first.fatigueSeverity);
                }
            }

            if (first.thoughtDef != null && pawn.needs != null && pawn.needs.mood != null)
            {
                bool already = false;
                foreach (Thought_Memory memory in pawn.needs.mood.thoughts.memories.Memories)
                {
                    if (memory.def == first.thoughtDef)
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(first.thoughtDef);
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
            return "Botania.BitterBloomStatus".Translate(Props.radius.ToString("0"), FedCount);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref FedCount, "fedCount", 0);
        }
    }
}
