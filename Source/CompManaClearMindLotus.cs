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

        /// <summary>被心灵干扰实际影响到的小人 → 最后一次被观测到的 tick。
        /// 只有这些人计费：性别不符、心灵迟钝（0 敏感）的人不花钱。</summary>
        private readonly Dictionary<Pawn, int> affected = new Dictionary<Pawn, int>();

        /// <summary>超过这么久没再观测到，就认为已经不影响了。</summary>
        private const int AffectedMemoryTicks = 600;

        private static readonly Dictionary<Map, bool> droneCache = new Dictionary<Map, bool>();

        private static int droneCacheTick = -999;


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
            // 只要记录到「有人确实被干扰影响到」就结算。
            // 不再去查地图条件——那个查询在实机上没拿到东西，白跑一趟。

            int now = Find.TickManager.TicksGame;
            int pawns = 0;
            List<Pawn> stale = null;
            foreach (KeyValuePair<Pawn, int> pair in affected)
            {
                Pawn pawn = pair.Key;
                // 结算时复查距离：小人离开范围后不应该继续被收费
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != parent.Map
                    || now - pair.Value > AffectedMemoryTicks
                    || pawn.Position.DistanceTo(parent.Position) > Props.radius)
                {
                    if (stale == null)
                    {
                        stale = new List<Pawn>();
                    }
                    stale.Add(pawn);
                    continue;
                }
                pawns++;
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    affected.Remove(stale[i]);
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

        /// <summary>心灵干扰正在影响这个小人：记到覆盖他的每朵花头上，作为计费依据。</summary>
        public static void NotifyAffected(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;

            // 多朵莲重叠覆盖同一个人时，只让「最近的那一朵」记账，
            // 否则同一个人会被几朵花各收一份钱。
            CompManaClearMindLotus owner = null;
            float best = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                float distance = lotus.parent.Position.DistanceTo(pawn.Position);
                if (distance > lotus.Props.radius)
                {
                    continue;
                }
                if (distance < best)
                {
                    best = distance;
                    owner = lotus;
                }
            }
            if (owner != null)
            {
                owner.affected[pawn] = now;
            }
        }

        /// <summary>这张图上有没有已经摆出来的心灵干扰器（建筑）。结果缓存两秒，避免每帧扫全图。</summary>
        public static bool HasDroneBuilding(Map map)
        {
            if (map == null)
            {
                return false;
            }
            int now = Find.TickManager.TicksGame;
            if (now - droneCacheTick > 120)
            {
                droneCache.Clear();
                droneCacheTick = now;
            }
            bool cached;
            if (droneCache.TryGetValue(map, out cached))
            {
                return cached;
            }
            bool found = false;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (thing != null && thing.def != null && thing.def.defName == "PsychicDronerShipPart" && thing.Spawned)
                {
                    found = true;
                    break;
                }
            }
            droneCache[map] = found;
            return found;
        }

        /// <summary>这朵花是否正在为某个小人「部分」挡干扰（只看圈内、且只对无实体的事件干扰生效）。</summary>
        public static bool PartialShield(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            if (HasDroneBuilding(pawn.Map))
            {
                // 场上有干扰器建筑：那种是全额挡，不走「减半」这条路
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
            if (!HasDroneBuilding(pawn.Map))
            {
                // 场上没有干扰器建筑 → 不是全免档，走 PartialShield 减半
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
            if (HasDroneBuilding(parent.Map))
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
