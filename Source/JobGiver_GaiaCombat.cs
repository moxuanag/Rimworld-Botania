using RimWorld;
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

            IntVec3 dest = target.Position;
            Map map = pawn.Map;
            if (!dest.Walkable(map) || dest.Fogged(map))
            {
                dest = CellFinder.RandomClosewalkCellNear(target.Position, map, 2,
                    c => c.Walkable(map) && !c.Fogged(map));
            }
            if (!dest.IsValid)
            {
                // 目标位置不可达：站定，让技能按射程远程打（Tick 里会判定卡住）
                boss.NotifyPathBlocked();
                return JobMaker.MakeJob(JobDefOf.Wait, 30);
            }

            return JobMaker.MakeJob(JobDefOf.Goto, dest);
        }
    }
}
