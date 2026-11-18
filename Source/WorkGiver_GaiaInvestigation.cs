using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 把「调查盖亚传送门」挂进**研究（Research）工作类型**：
    /// 玩家在「工作」面板里给小人排研究优先级，小人就会自己来调查，
    /// 优先级、打断、寻路全部交给原版工作系统。
    ///
    /// 只认处于「待调查」状态的传送门；已经开启或黯淡中的不派活。
    /// </summary>
    public class WorkGiver_GaiaInvestigation : WorkGiver_Scanner
    {
        private static ThingDef portalDef;

        private static ThingDef PortalDef
        {
            get
            {
                if (portalDef == null)
                {
                    portalDef = DefDatabase<ThingDef>.GetNamedSilentFail("Botania_GaiaPortal");
                }
                return portalDef;
            }
        }

        private static JobDef InvestigateJob
        {
            get { return DefDatabase<JobDef>.GetNamedSilentFail("Botania_InvestigateGaiaPortal"); }
        }

        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                ThingDef def = PortalDef;
                return def == null ? ThingRequest.ForGroup(ThingRequestGroup.Undefined) : ThingRequest.ForDef(def);
            }
        }

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return PortalDef == null || InvestigateJob == null;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompGaiaPortal comp = t.TryGetComp<CompGaiaPortal>();
            if (comp == null || !comp.NeedsInvestigation)
            {
                return false;
            }
            if (pawn.WorkTagIsDisabled(WorkTags.Intellectual))
            {
                return false;
            }
            return pawn.CanReserveAndReach(t, PathEndMode.InteractionCell, Danger.Deadly, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            JobDef def = InvestigateJob;
            if (def == null)
            {
                return null;
            }
            IntVec3 stand = CompGaiaPortal.FindStandCell(t);
            if (!stand.IsValid)
            {
                return null;
            }
            return JobMaker.MakeJob(def, t, stand);
        }
    }
}
