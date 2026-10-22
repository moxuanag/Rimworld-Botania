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
        private const int TicksPerSettle = 60;

        private int ticksAccum;

        /// <summary>正在被抽取的小人，离开范围时要摘掉 hediff。</summary>
        private List<Pawn> siphoning = new List<Pawn>();

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
            foreach (Pawn pawn in inRange)
            {
                if (!HasSomethingLeftToLearn(pawn))
                {
                    continue;
                }
                mana += Props.manaPerPawnSecond;
                EnsureHediff(pawn);
            }

            if (mana > 0f)
            {
                CompManaStorage pool = FindPool();
                if (pool != null)
                {
                    pool.AddMana(mana);
                }
            }

            CleanupHediffs(inRange);
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

        private void EnsureHediff(Pawn pawn)
        {
            if (Props.hediffDef == null)
            {
                return;
            }
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (existing == null)
            {
                pawn.health.AddHediff(Props.hediffDef);
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
                    siphoning.RemoveAt(i);
                    continue;
                }
                if (stillInRange.Contains(pawn))
                {
                    continue;
                }
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
                siphoning.RemoveAt(i);
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
            return "Botania.RosaArcanaSiphoning".Translate(siphoning.Count);
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
