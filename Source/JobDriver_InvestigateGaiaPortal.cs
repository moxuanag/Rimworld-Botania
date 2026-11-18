using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 「调查盖亚传送门」——一项**正式工作**，归研究类，需要小人亲自来做。
    ///
    /// 关键点（踩过坑）：传送门是 2×2 不可通行建筑，它的 InteractionCell 可能落在
    /// 建筑内部，用 PathEndMode.InteractionCell 会让小人「永远够不到」，
    /// 工作一启动就失败，引擎会在一 tick 里反复派活（日志报 "started 10 jobs in one tick"）。
    /// 所以 job 的第二个目标（TargetB）由派活方预先算成**可达的相邻格**，
    /// 这里只负责走过去（OnCell）然后站定干活。
    /// </summary>
    public class JobDriver_InvestigateGaiaPortal : JobDriver
    {
        private const TargetIndex PortalInd = TargetIndex.A;
        private const TargetIndex StandInd = TargetIndex.B;

        // 保险上限：正常由进度提前结束；防止小人被永久卡在这份工作上
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

            // 1) 走到预先算好的相邻站位（普通格，不是 InteractionCell）
            yield return Toils_Goto.GotoCell(StandInd, PathEndMode.OnCell);

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
            yield return work;
        }
    }
}
