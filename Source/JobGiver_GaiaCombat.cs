using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚的唯一行为节点：把「找目标 → 走近 → 站定」交给原版 AI 流程，
    /// 不再让 Pawn.Tick 直接抢 pather（那会和思考树互相打架，出现原地游荡）。
    ///
    /// 技能仍然由 Pawn_GaiaBoss.Tick 里的状态机驱动：
    ///  - 距离 &gt; EngageRange：返回 Goto，走近
    ///  - 距离 &lt;= EngageRange：返回 Wait，站定让技能放出来
    ///  - 没有目标：返回 Wait，原地待命（**不游荡**）
    /// </summary>
    public class JobGiver_GaiaCombat : ThinkNode_JobGiver
    {
        /// <summary>找 Boss 身边挡路的敌对墙/门（用于拆进去）。</summary>
        private static Thing FindBlockingStructure(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }
            IntVec3 origin = pawn.Position;
            Thing best = null;
            float bestDist = float.MaxValue;
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    IntVec3 c = new IntVec3(origin.x + dx, 0, origin.z + dz);
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    List<Thing> things = c.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing th = things[i];
                        Building b = th as Building;
                        if (b == null || b.Destroyed || !b.Spawned || b.def == null || b.def.building == null)
                        {
                            continue;
                        }
                        BuildingProperties bp = b.def.building;
                        bool isWallOrDoor = bp.isWall || b is Building_Door;
                        if (!isWallOrDoor)
                        {
                            continue;
                        }
                        if (b.Faction == null || !b.HostileTo(pawn.Faction))
                        {
                            continue;
                        }
                        float d = b.Position.DistanceTo(origin);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = b;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 目标不可达时找一个「朝目标方向、自己走得到」的中转格，
        /// 让 Boss 至少靠近，而不是原地站着发呆。
        /// </summary>
        private static IntVec3 FindStagingCell(Pawn pawn, Thing target)
        {
            Map map = pawn.Map;
            IntVec3 origin = pawn.Position;
            IntVec3 toward = target.Position - origin;
            if (toward.LengthHorizontal <= 0)
            {
                return IntVec3.Invalid;
            }
            float len = toward.LengthHorizontal;
            for (int step = 10; step >= 2; step--)
            {
                float scale = Mathf.Min(1f, step / len);
                IntVec3 candidate = new IntVec3(
                    origin.x + Mathf.RoundToInt(toward.x * scale),
                    0,
                    origin.z + Mathf.RoundToInt(toward.z * scale));
                if (!candidate.InBounds(map) || !candidate.Walkable(map))
                {
                    continue;
                }
                if (pawn.CanReach(new LocalTargetInfo(candidate), PathEndMode.OnCell, Danger.Deadly, true, true))
                {
                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            Pawn_GaiaBoss boss = pawn as Pawn_GaiaBoss;
            if (boss == null || boss.Dead || !boss.Spawned || boss.Map == null)
            {
                return null;
            }

            // 施法/世界之魂期间：站定，不移动
            if (boss.IsBusyCasting)
            {
                return JobMaker.MakeJob(JobDefOf.Wait, 20);
            }

            // 玩家指定了技能落点：先走到落点附近，再让 Tick 施放
            if (boss.TryGetOrderedMoveCell(out IntVec3 orderCell))
            {
                if (orderCell.DistanceTo(pawn.Position) <= Pawn_GaiaBoss.EngageRange)
                {
                    return JobMaker.MakeJob(JobDefOf.Wait, 20);
                }
                IntVec3 dest0 = orderCell;
                if (!dest0.Walkable(pawn.Map) || dest0.Fogged(pawn.Map))
                {
                    dest0 = CellFinder.RandomClosewalkCellNear(orderCell, pawn.Map, 2,
                        c => c.Walkable(pawn.Map) && !c.Fogged(pawn.Map));
                }
                if (dest0.IsValid)
                {
                    return JobMaker.MakeJob(JobDefOf.Goto, dest0);
                }
            }

            // 需要破墙：先站到那面墙「自己走得到」的一侧
            if (boss.BreachWall != null && boss.BreachWall.Spawned && !boss.BreachWall.Destroyed)
            {
                if (boss.TryGetBreachStandCell(out IntVec3 stand))
                {
                    if (stand.DistanceTo(pawn.Position) <= 1.5f)
                    {
                        return JobMaker.MakeJob(JobDefOf.Wait, 20);
                    }
                    return JobMaker.MakeJob(JobDefOf.Goto, stand);
                }
                return JobMaker.MakeJob(JobDefOf.Wait, 20);
            }

            Thing target = boss.AITarget;
            if (target == null || target.Destroyed || !target.Spawned)
            {
                // 没有目标：原地待命，别像动物一样到处逛
                return JobMaker.MakeJob(JobDefOf.Wait, 60);
            }

            float dist = target.Position.DistanceTo(pawn.Position);
            if (dist <= Pawn_GaiaBoss.EngageRange)
            {
                // 已经在攻击距离内：站定，技能由 Tick 驱动
                return JobMaker.MakeJob(JobDefOf.Wait, 30);
            }

            Map map = pawn.Map;
            // 关键：不能走到「目标所在格」——那个格子被目标占着，永远不可达。
            // 应该走到目标的**旁边**。
            IntVec3 dest = CellFinder.RandomClosewalkCellNear(target.Position, map, 2,
                c => c.Walkable(map) && !c.Fogged(map) && c != target.Position);

            // 走不到目标旁边：挑一个「朝目标方向、但自己走得到」的中转格逐步靠近
            if (!dest.IsValid || !pawn.CanReach(new LocalTargetInfo(dest), PathEndMode.OnCell, Danger.Deadly, true, true))
            {
                IntVec3 staging = FindStagingCell(pawn, target);
                if (staging.IsValid)
                {
                    return JobMaker.MakeJob(JobDefOf.Goto, staging);
                }
                boss.NotifyPathBlocked();
                return JobMaker.MakeJob(JobDefOf.Wait, 30);
            }

            return JobMaker.MakeJob(JobDefOf.Goto, dest);
        }
    }
}
