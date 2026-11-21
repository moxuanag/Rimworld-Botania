using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚造物的行为：找最近的敌对目标并冲上去近战。
    ///
    /// 不继承原版动物思考树（那套只会逃跑/游荡，不会主动进攻建筑），
    /// 用和 Boss 一样的最小思考树 + 这个 JobGiver，行为最可预测。
    /// </summary>
    public class JobGiver_GaiaSpawnlingCombat : ThinkNode_JobGiver
    {
        private const float SearchRadius = 40f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                return null;
            }

            Thing target = FindTarget(pawn);
            if (target == null)
            {
                return JobMaker.MakeJob(JobDefOf.Wait, 60);
            }

            if (!pawn.CanReach(new LocalTargetInfo(target), PathEndMode.Touch, Danger.Deadly, true, true))
            {
                return JobMaker.MakeJob(JobDefOf.Wait, 60);
            }

            return JobMaker.MakeJob(JobDefOf.AttackMelee, target);
        }

        private static Thing FindTarget(Pawn pawn)
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
                if (d < bestDist)
                {
                    bestDist = d;
                    best = th;
                }
            }
            return best;
        }
    }
}
