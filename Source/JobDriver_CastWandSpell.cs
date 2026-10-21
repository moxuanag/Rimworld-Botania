using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>释放法杖法术的通用 Job：走到目标附近 → 施法。</summary>
    public class JobDriver_CastWandSpell : JobDriver
    {
        private const TargetIndex TargetInd = TargetIndex.A;

        /// <summary>与 Ability_WandSpell 保持一致：站到离目标这么近就能放了。</summary>
        private float Range => job.verbToUse?.verbProps?.range ?? 6f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override System.Collections.Generic.IEnumerable<Toil> MakeNewToils()
        {
            // 目标格可能在途中失效，失效就结束
            this.FailOnDespawnedNullOrForbidden(TargetInd);

            // 1. 走到能施法的位置
            yield return Toils_Goto.GotoCell(TargetInd, PathEndMode.Touch)
                .FailOnCannotTouch(TargetInd, PathEndMode.Touch);

            // 2. 到了就近了，真正放出去
            yield return Toils_General.Wait(10);
            yield return new Toil
            {
                initAction = () =>
                {
                    Ability_WandSpell spell = job.ability as Ability_WandSpell;
                    if (spell == null)
                    {
                        return;
                    }
                    // 用当前站位重新结算一次，确保真的在范围内
                    spell.Activate(job.targetA, job.targetA);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
