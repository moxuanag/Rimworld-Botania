using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 把「调查盖亚传送门」挂进**研究（Research）工作类型**（不单独占一列）：
    /// 玩家在「工作」面板里给小人排研究优先级，小人就会自己来调查，
    /// 优先级、打断、寻路全部交给原版工作系统。
    /// 调查速度按殖民者的**智慧（Intellectual）等级**加成，见 CompGaiaPortal。
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

        /// <summary>只有玩家把传送门的「自动调查」打开，工作系统才会主动派人。</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (PortalDef == null || InvestigateJob == null)
            {
                return true;
            }
            return !HasAutoPortalOnMap(pawn);
        }

        private bool HasAutoPortalOnMap(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return false;
            }
            List<Thing> portals = map.listerThings.ThingsOfDef(PortalDef);
            for (int i = 0; i < portals.Count; i++)
            {
                CompGaiaPortal c = portals[i].TryGetComp<CompGaiaPortal>();
                if (c != null && c.NeedsInvestigation && c.AutoInvestigate)
                {
                    return true;
                }
            }
            return false;
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
