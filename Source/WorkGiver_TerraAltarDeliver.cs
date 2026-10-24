using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 把「给凝聚台送料」做成**搬运工作（Hauling）**里的一项正式工作，
    /// 而不是按钮点一下派一次活。
    ///
    /// 为什么要这样：RimWorld 的工作调度是按 WorkType 扫 WorkGiver 的。
    /// 只要这条路走通，小人空闲时就会自己排进来，优先级、打断、寻路全部交给原版
    /// 工作系统处理——这比「按钮 → 手动 TryTakeOrderedJob」正确得多，也不会打断
    /// 正在干活的小人。
    ///
    /// 注意：这里**没有**自定义 JobDef / JobDriver（本引擎那套不生效，踩坑第 3 条），
    /// 交回去的是原版 `JobDefOf.HaulToCell`，司机是原版的。
    /// </summary>
    public class WorkGiver_TerraAltarDeliver : WorkGiver_Scanner
    {
        private static ThingDef altarDef;

        private static ThingDef AltarDef
        {
            get
            {
                if (altarDef == null)
                {
                    altarDef = DefDatabase<ThingDef>.GetNamedSilentFail("Botania_TerrasteelAltar");
                }
                return altarDef;
            }
        }

        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                ThingDef def = AltarDef;
                return def != null ? ThingRequest.ForDef(def)
                    : ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
            }
        }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ThingDef def = AltarDef;
            if (def == null || pawn.Map == null)
            {
                return null;
            }
            return pawn.Map.listerThings.ThingsOfDef(def);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            ThingDef def = AltarDef;
            if (def == null || pawn.Map == null)
            {
                return true;
            }
            List<Thing> altars = pawn.Map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < altars.Count; i++)
            {
                CompTerraAltar comp = altars[i].TryGetComp<CompTerraAltar>();
                if (comp != null && comp.AutoHaul)
                {
                    return false;
                }
            }
            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompTerraAltar altar = t?.TryGetComp<CompTerraAltar>();
            if (altar == null || !altar.AutoHaul)
            {
                return false;
            }
            // 台子自己的活：基座不全时先别送料，免得材料堆一地
            if (altar.MissingPedestals > 0)
            {
                return false;
            }
            Thing stack;
            IntVec3 dropCell;
            int count;
            if (!TerraAltarHauling.TryPlan(altar, pawn, out stack, out dropCell, out count))
            {
                return false;
            }
            // 不再预定整座台子：几个搬运工同时送不同材料是合理的，
            // 材料的预定已经在 TryPlan 里用 CanReserveAndReach 判过了。
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompTerraAltar altar = t?.TryGetComp<CompTerraAltar>();
            if (altar == null)
            {
                return null;
            }
            Thing stack;
            IntVec3 dropCell;
            int count;
            if (!TerraAltarHauling.TryPlan(altar, pawn, out stack, out dropCell, out count))
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, stack, dropCell);
            job.count = count;
            // 台面不是储物区：到点就地放下
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }
}
