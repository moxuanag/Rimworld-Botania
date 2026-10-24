using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 凝聚台的「自动搬运」：把台上的材料缺口交给**搬运工**去补。
    ///
    /// 现状（2026-10-24 前）：材料只能玩家自己丢到 3×3 台面上，
    /// 没有任何机制会把材料搬上去。这个文件补的就是这一环。
    ///
    /// 实现上刻意**不写自定义 JobDef / JobDriver**（本引擎那套不生效，见踩坑第 3 条），
    /// 而是直接排原版的 `JobDefOf.HaulToCell`：小人走到材料堆、抓指定数量、
    /// 走到台面格上放下——搬运本来就是原版工作，不需要我们再造一套。
    ///
    /// 只挑**当前空着的搬运工**（手头没活），不强行打断正在干活的小人：
    /// 玩家点了「自动搬运」是希望「有空就去搬」，不是让全殖民地放下手里的活。
    /// </summary>
    public static class TerraAltarHauling
    {
        /// <summary>两次派单之间的最短间隔，避免同一 tick 反复派人。</summary>
        public const int OrderCooldownTicks = 180;

        private const float SearchRadius = 9999f;

        /// <summary>
        /// 派一次活。返回是否成功派出去（失败时 reason 里写原因，给检视栏用）。
        /// </summary>
        public static bool TryOrderHaul(CompTerraAltar altar, out string reason)
        {
            reason = null;
            if (altar == null)
            {
                reason = "no altar";
                return false;
            }
            Map map = altar.parent.Map;
            if (map == null)
            {
                reason = "no map";
                return false;
            }

            // 1. 先算台面上还缺什么（按 comp 自己的缺口算，含「蓝图不算」那套语义）
            ThingDef needDef = null;
            int needCount = 0;
            int haveA = altar.CountOnPlatformPublic(altar.Props.ingredientA);
            if (haveA < altar.Props.countA)
            {
                needDef = altar.Props.ingredientA;
                needCount = altar.Props.countA - haveA;
            }
            else
            {
                int haveB = altar.CountOnPlatformPublic(altar.Props.ingredientB);
                if (haveB < altar.Props.countB)
                {
                    needDef = altar.Props.ingredientB;
                    needCount = altar.Props.countB - haveB;
                }
            }
            if (needDef == null)
            {
                return false; // 不需要搬，安静退出
            }

            // 2. 找一摞可搬的材料（优先离家近、优先大堆）
            Thing stack = FindStack(map, needDef, altar.parent.Position, altar);
            if (stack == null)
            {
                reason = "no ingredient";
                return false;
            }

            // 3. 找一个闲着的搬运工
            Pawn hauler = FindIdleHauler(map, altar.parent.Position, stack);
            if (hauler == null)
            {
                reason = "no hauler";
                return false;
            }

            // 4. 排原版搬运 job：抓 needCount 个，放到台面中心
            IntVec3 dropCell = DropCell(altar);
            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, stack, dropCell);
            job.count = needCount;
            // 台面不是储物区，明确告诉 driver「到点就地放下」
            job.haulMode = HaulMode.ToCellNonStorage;
            hauler.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            altar.NoteHaulOrdered();
            if (Prefs.DevMode)
            {
                Log.Message("[Botania] 凝聚台派搬运：" + hauler.LabelShort + " → "
                    + needDef.label + " ×" + needCount + " → " + dropCell);
            }
            return true;
        }

        /// <summary>
        /// 材料落点：台面上**除中心以外**的格。
        ///
        /// 中心那格是泰拉钢的产出点（`FinishIngot` 在 CenterCell 生成成品），
        /// 把材料堆在中心会挡住成品落点，所以材料一律放外圈八格。
        /// 挑法：优先选没东西的格，其次选东西最少的格。
        /// </summary>
        private static IntVec3 DropCell(CompTerraAltar altar)
        {
            Map map = altar.parent.Map;
            CellRect rect = altar.parent.OccupiedRect();
            IntVec3 center = rect.CenterCell;
            IntVec3 best = IntVec3.Invalid;
            int bestCount = int.MaxValue;
            foreach (IntVec3 cell in rect)
            {
                if (cell == center || !cell.InBounds(map) || !cell.Standable(map))
                {
                    continue;
                }
                int count = cell.GetThingList(map).Count;
                if (count < bestCount)
                {
                    bestCount = count;
                    best = cell;
                }
                if (count == 0)
                {
                    break;
                }
            }
            if (best.IsValid)
            {
                return best;
            }
            return center.InBounds(map) ? center : altar.parent.Position;
        }

        private static Thing FindStack(Map map, ThingDef def, IntVec3 near, CompTerraAltar altar)
        {
            Thing best = null;
            float bestDist = float.MaxValue;
            List<Thing> all = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || t.stackCount <= 0)
                {
                    continue;
                }
                if (t.IsForbidden(Faction.OfPlayer))
                {
                    continue;
                }
                if (t.Position.DistanceTo(near) > SearchRadius)
                {
                    continue;
                }
                // 已经在这个台面里的不算（不然会「搬来搬去」）
                if (altar.parent.OccupiedRect().Contains(t.Position))
                {
                    continue;
                }
                float d = t.Position.DistanceTo(near);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>
        /// 「闲到可以派活」：没有 job，或者当前 job 只是原版的等待/闲逛。
        ///
        /// 踩过的坑：第一版要求 `curJob == null`，结果实机**永远等不到人**——
        /// 原版小人待命时挂着 `Wait` / `Wait_Wander`，curJob 几乎不为空。
        /// </summary>
        private static bool IsIdleEnough(Pawn p)
        {
            Job cur = p.jobs?.curJob;
            if (cur == null)
            {
                return true;
            }
            JobDef def = cur.def;
            if (def == null)
            {
                return false;
            }
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.Wait_Downed
                || def == JobDefOf.GotoWander
                || def.defName == "Wait_MaintainPosture";
        }

        private static Pawn FindIdleHauler(Map map, IntVec3 near, Thing stack)
        {
            Pawn best = null;
            float bestDist = float.MaxValue;
            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || p.Downed || p.Drafted)
                {
                    continue;
                }
                if (!p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling))
                {
                    continue;
                }
                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    continue;
                }
                // 手头有活就不打断它——但「原地发呆」也是 job（Wait/Wait_Wander），要放行
                if (!IsIdleEnough(p))
                {
                    continue;
                }
                if (!p.CanReserveAndReach(stack, PathEndMode.ClosestTouch, Danger.Some))
                {
                    continue;
                }
                float d = p.Position.DistanceTo(near);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
