using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 「调查盖亚传送门」——一项**正式工作**，和原版研究一样由小人执行：
    /// 走到门口站定，按 ResearchSpeed 逐 tick 推进进度，进度满才开启波次。
    ///
    /// 和之前「站旁边自动涨」的区别：必须有小人在做这个 Job，离开就停；
    /// 进度按小人的研究速度算，快的研究员更快。
    /// </summary>
    public class JobDriver_InvestigateGaiaPortal : JobDriver
    {
        private const TargetIndex PortalInd = TargetIndex.A;

        // 保险上限：正常由进度提前结束；防止小人被卡死在这份工作上
        private const int MaxWorkTicks = 20000;

        private Thing Portal => job.GetTarget(PortalInd).Thing;

        private CompGaiaPortal Comp
        {
            get
            {
                Thing t = Portal;
                return t == null ? null : t.TryGetComp<CompGaiaPortal>();
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(PortalInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(PortalInd);
            this.FailOnCannotTouch(PortalInd, PathEndMode.InteractionCell);

            // 1) 走到传送门旁边
            yield return Toils_Goto.GotoThing(PortalInd, PathEndMode.InteractionCell);

            // 2) 站定调查：每 tick 把「研究速度」记进组件进度
            Toil work = new Toil();
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = MaxWorkTicks;
            work.tickAction = () =>
            {
                CompGaiaPortal c = Comp;
                if (c == null || !c.NeedsInvestigation)
                {
                    ReadyForNextToil();
                    return;
                }
                float speed = pawn.GetStatValue(StatDefOf.ResearchSpeed);
                if (speed <= 0f)
                {
                    speed = 0.1f;
                }
                if (c.AddInvestigationWork(speed))
                {
                    ReadyForNextToil();
                }
            };
            work.WithProgressBar(PortalInd, () => Comp == null ? 0f : Comp.InvestigationPercent);
            work.WithEffect(EffecterDefOf.Research, PortalInd);
            yield return work;
        }
    }
}
