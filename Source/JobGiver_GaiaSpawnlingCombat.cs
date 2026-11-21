using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚造物的行为，逻辑与 Boss 一致（但用近战而不是技能）：
    ///   1) 「野外可达」（不穿墙/不穿关着的门）的敌对目标 -> 就近冲上去近战
    ///      小人与炮塔/建筑同等优先级
    ///   2) 全都不可达 -> 找一面朝目标方向的墙/门 -> 直接近战砸它（破墙）
    ///   3) 没有敌人 -> 回到 Boss 身边；已经贴近就在附近小范围游荡
    ///
    /// 注意：不要用原版动物思考树，也不要继承 AnimalConstant
    /// （里面有 JobGiver_AnimalFlee，会让造物一直逃，表现为「一出来就跑」）。
    /// </summary>
    public class JobGiver_GaiaSpawnlingCombat : ThinkNode_JobGiver
    {
        private const float SearchRadius = 45f;
        private const float BossLeashRange = 7f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                return null;
            }

            Map map = pawn.Map;

            // 1) 野外可达的最近敌对目标（小人 / 炮塔 / 建筑同优先级）
            Thing wild = FindNearestTarget(pawn, true);
            if (wild != null)
            {
                return JobMaker.MakeJob(JobDefOf.AttackMelee, wild);
            }

            // 2) 目标全被墙挡住 -> 砸朝目标方向的墙/门
            Thing blocked = FindNearestTarget(pawn, false);
            if (blocked != null)
            {
                Building wall = FindBreachWall(pawn, blocked);
                if (wall != null)
                {
                    return JobMaker.MakeJob(JobDefOf.AttackMelee, wall);
                }
            }

            // 3) 没有敌人：围着 Boss 转
            Pawn boss = FindBoss(pawn);
            if (boss != null)
            {
                float distToBoss = pawn.Position.DistanceTo(boss.Position);
                if (distToBoss > BossLeashRange)
                {
                    IntVec3 back = CellFinder.RandomClosewalkCellNear(boss.Position, map, 5,
                        c => c.Walkable(map) && !c.Fogged(map));
                    if (back.IsValid)
                    {
                        return JobMaker.MakeJob(JobDefOf.Goto, back);
                    }
                }
                else if (Rand.Chance(0.35f))
                {
                    IntVec3 wander = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 3,
                        c => c.Walkable(map) && !c.Fogged(map));
                    if (wander.IsValid)
                    {
                        return JobMaker.MakeJob(JobDefOf.GotoWander, wander);
                    }
                }
            }

            return JobMaker.MakeJob(JobDefOf.Wait, 120);
        }

        private static Pawn FindBoss(Pawn pawn)
        {
            Map map = pawn.Map;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Pawn_GaiaBoss boss && !boss.Dead)
                {
                    return boss;
                }
            }
            return null;
        }

        /// <summary>找最近的敌对目标；wildReachable=true 只要野外可达的，false 只要不可达的。</summary>
        private static Thing FindNearestTarget(Pawn pawn, bool wildReachable)
        {
            Map map = pawn.Map;
            List<Thing> candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.AttackTarget);
            Thing best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing th = candidates[i];
                if (th == null || th == pawn || th.Destroyed || !th.Spawned || th.Faction == null)
                {
                    continue;
                }
                if (th is Pawn p && p.Downed)
                {
                    continue;
                }
                if (pawn.Faction == null || !th.HostileTo(pawn.Faction))
                {
                    continue;
                }
                float d = th.Position.DistanceTo(pawn.Position);
                if (d > SearchRadius)
                {
                    continue;
                }
                if (IsWildReachable(pawn, th) != wildReachable)
                {
                    continue;
                }
                if (d < bestDist)
                {
                    bestDist = d;
                    best = th;
                }
            }
            return best;
        }

        private static bool IsWildReachable(Pawn pawn, Thing th)
        {
            try
            {
                TraverseParms parms = TraverseParms.For(pawn, Danger.Deadly,
                    TraverseMode.NoPassClosedDoors, false);
                return pawn.Map.reachability.CanReach(pawn.Position, new LocalTargetInfo(th),
                    PathEndMode.Touch, parms);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>找一面「自己这侧可达」且朝目标方向的墙/门。</summary>
        private static Building FindBreachWall(Pawn pawn, Thing target)
        {
            Map map = pawn.Map;
            Building best = null;
            float bestScore = float.MaxValue;
            IntVec3 origin = pawn.Position;
            IntVec3 toward = target.Position - origin;
            int radius = 20;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    IntVec3 c = new IntVec3(origin.x + dx, 0, origin.z + dz);
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    Building b = map.edificeGrid[c];
                    if (b == null || b.Destroyed || !b.Spawned || b.def?.building == null)
                    {
                        continue;
                    }
                    BuildingProperties bp = b.def.building;
                    if (!bp.isWall && !(b is Building_Door))
                    {
                        continue;
                    }
                    if (b.Faction == null || pawn.Faction == null || !b.HostileTo(pawn.Faction))
                    {
                        continue;
                    }
                    if (!HasReachableNeighbour(pawn, b))
                    {
                        continue;
                    }
                    float score = b.Position.DistanceTo(origin) * 0.6f
                                  + b.Position.DistanceTo(toward + origin) * 0.4f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = b;
                    }
                }
            }
            return best;
        }

        private static bool HasReachableNeighbour(Pawn pawn, Building b)
        {
            Map map = pawn.Map;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 c = b.Position + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map) || !c.Walkable(map))
                {
                    continue;
                }
                try
                {
                    TraverseParms parms = TraverseParms.For(pawn, Danger.Deadly,
                        TraverseMode.NoPassClosedDoors, false);
                    if (map.reachability.CanReach(pawn.Position, new LocalTargetInfo(c),
                            PathEndMode.OnCell, parms))
                    {
                        return true;
                    }
                }
                catch (System.Exception)
                {
                    // 忽略
                }
            }
            return false;
        }
    }
}
