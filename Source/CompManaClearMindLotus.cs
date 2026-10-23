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
            // 只要有心灵干扰在生效就得付费：
            //  · 建筑来源（机械族干扰器）→ 全额挡住（见 IsShielded）
            //  · 事件来源（无实体）→ 只把心情惩罚减半（见 PartialShield）
            // 这样玩家不会以为「花没反应就是 bug」。
            if (ActiveDrone(parent.Map) == null)
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

        /// <summary>当前生效的心灵干扰（没有就返回 null）。</summary>
        private static GameCondition ActiveDrone(Map map)
        {
            return map == null ? null : map.gameConditionManager.GetActiveCondition(GameConditionDefOf.PsychicDrone);
        }

        /// <summary>这朵花是否正在为某个小人「部分」挡干扰（只看圈内、且只对无实体的事件干扰生效）。</summary>
        public static bool PartialShield(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            GameCondition condition = ActiveDrone(pawn.Map);
            if (condition == null || condition.conditionCauser is Building)
            {
                // 没有干扰，或干扰有实体来源（那种是全额挡，不走这里）
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

        /// <summary>这个小人是否被某朵正在付费的明心莲罩着。</summary>
        public static bool IsShielded(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            GameCondition condition = ActiveDrone(pawn.Map);
            if (condition == null || !(condition.conditionCauser is Building))
            {
                // 只有「有实体来源」的干扰才全额挡；事件干扰走 PartialShield 减半
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
            if (!IsPaying)
            {
                return "Botania.ClearMindLotusIdle".Translate();
            }
            GameCondition condition = ActiveDrone(parent.Map);
            bool fromBuilding = condition != null && condition.conditionCauser is Building;
            if (fromBuilding)
            {
                return "Botania.ClearMindLotusActive".Translate(Props.manaPerPawnSecond.ToString("0"));
            }
            return "Botania.ClearMindLotusWeaken".Translate(Props.manaPerPawnSecond.ToString("0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref paidUntilTick, "paidUntilTick", 0);
        }
    }
}
