using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 明心莲：用魔力替范围内的小人挡住「心灵干扰」。
    /// 只对冲心灵干扰（PsychicDrone）那一条念头，不做整体心情加成；
    /// 断魔就失效，所以它是个吃钱的功能花，不是白给的心情挂件。
    /// </summary>
    public class CompProperties_ManaClearMindLotus : CompProperties
    {
        /// <summary>庇护半径（格）。</summary>
        public float radius = 8f;

        /// <summary>每个被庇护者每秒消耗多少魔力。</summary>
        public float manaPerPawnSecond = 20f;

        /// <summary>向魔力池取魔的半径。</summary>
        public float poolRadius = 6f;

        /// <summary>付一次钱能撑多少 tick（180 = 3 秒，避免每帧反复取魔）。</summary>
        public int paidDurationTicks = 180;

        public CompProperties_ManaClearMindLotus()
        {
            compClass = typeof(CompManaClearMindLotus);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerPawnSecond <= 0f)
            {
                yield return "manaPerPawnSecond must be greater than 0.";
            }
            if (paidDurationTicks <= 0)
            {
                yield return "paidDurationTicks must be greater than 0.";
            }
        }
    }

    public class CompManaClearMindLotus : ThingComp
    {
        /// <summary>场上所有明心莲，心灵干扰的补丁直接查这张表。</summary>
        public static readonly List<CompManaClearMindLotus> All = new List<CompManaClearMindLotus>();

        private const int TicksPerSettle = 60;

        private int ticksAccum;

        /// <summary>付过钱的截止 tick；在这之前范围内的心里干扰被压住。</summary>
        private int paidUntilTick;

        public CompProperties_ManaClearMindLotus Props => (CompProperties_ManaClearMindLotus)props;

        /// <summary>当前是否在提供庇护（已付费且未过期）。</summary>
        public bool IsPaying => parent != null && parent.Spawned && Find.TickManager.TicksGame < paidUntilTick;

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
            // 只对抗「有具体来源」的心灵干扰：机械族的心灵干扰器（建筑）制造的嗡鸣。
            // 剧情事件那种没有实体的干扰不管，免得一张图无差别全免。
            if (!SourceDroneActive(parent.Map))
            {
                return;
            }

            int pawns = 0;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.radius, true))
            {
                Pawn pawn = thing as Pawn;
                if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.RaceProps.Humanlike)
                {
                    pawns++;
                }
            }
            if (pawns <= 0)
            {
                return;
            }

            float cost = pawns * Props.manaPerPawnSecond;
            IManaSource source = ManaSourceUtil.FindNearest(parent.Position, parent.Map, Props.poolRadius, cost);
            if (source == null)
            {
                return;
            }
            float drained = source.DrainMana(cost);
            if (drained + 0.01f < cost)
            {
                // 不够就整次不生效，避免「半价挡半份」的模糊状态
                source.AddMana(drained);
                return;
            }
            paidUntilTick = Find.TickManager.TicksGame + Props.paidDurationTicks;
        }

        /// <summary>这张图上是否有「由建筑产生」的心灵干扰在生效。</summary>
        private static bool SourceDroneActive(Map map)
        {
            if (map == null)
            {
                return false;
            }
            GameCondition condition = map.gameConditionManager.GetActiveCondition(GameConditionDefOf.PsychicDrone);
            if (condition == null)
            {
                return false;
            }
            return condition.conditionCauser is Building;
        }

        /// <summary>这个小人是否被某朵正在付费的明心莲罩着。</summary>
        public static bool IsShielded(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            if (!SourceDroneActive(pawn.Map))
            {
                return false;
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                if (!lotus.IsPaying)
                {
                    continue;
                }
                if (lotus.parent.Position.DistanceTo(pawn.Position) <= lotus.Props.radius)
                {
                    return true;
                }
            }
            return false;
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            return IsPaying
                ? "Botania.ClearMindLotusActive".Translate(Props.manaPerPawnSecond.ToString("0"))
                : "Botania.ClearMindLotusIdle".Translate();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref paidUntilTick, "paidUntilTick", 0);
        }
    }
}
