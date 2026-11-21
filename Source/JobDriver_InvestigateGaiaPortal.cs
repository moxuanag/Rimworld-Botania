using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 「调查盖亚传送门」工作：小人**站到传送门中央**，靠 CompGaiaPortal.CompTick
    /// 按「站在门里的殖民者 + 研究速度」累计进度。
    ///
    /// 为什么进度不放在这个 JobDriver 里：本引擎里自定义 JobDriver 的 tickAction
    /// 行为不稳（之前出现过任务一启动就失败、进度条不动）。改成「司机只负责把人
    /// 送到位并按住，进度由组件按站位统计」，两条路互不依赖，坏一条另一条照常。
    /// </summary>
    public class JobDriver_InvestigateGaiaPortal : JobDriver
    {
        private const TargetIndex PortalInd = TargetIndex.A;
        private const TargetIndex StandInd = TargetIndex.B;

        // 按住小人的最长时长；正常由组件进度完成或玩家打断
        private const int MaxWorkTicks = 8000;

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

            // 1) 走进传送门（3×3 可通行，目标就是中央格）
            yield return Toils_Goto.GotoCell(StandInd, PathEndMode.OnCell);

            // 2) 站定按住：进度由 CompTick 统计，这里只负责「留在门里」
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
                if (Find.TickManager.TicksGame % 250 == 0)
                {
                    BotaniaDebug.Log($"[Botania] 调查中：{pawn.LabelShort} 位于门内，进度 {c.InvestigationPercent:P0}");
                }
            };
            work.WithProgressBar(PortalInd, () => Comp == null ? 0f : Comp.InvestigationPercent);
            yield return work;
        }
    }
}
