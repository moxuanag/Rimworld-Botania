using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 凝聚台送料的「规划」部分：缺什么料、哪摞能搬、放到台面哪一格。
    ///
    /// 按谁去派活分两层：
    /// - 派活由 `WorkGiver_TerraAltarDeliver` 走**原版搬运工作（Hauling）**，不需要按钮；
    /// - 这里只回答「这个小人现在能不能给这座台子送料，送什么、放哪里」。
    ///
    /// 交回去的 job 是**原版** `JobDefOf.HaulToCell`，
    /// 没有自定义 JobDef / JobDriver（本引擎那套不生效，踩坑第 3 条）。
    /// </summary>
    public static class TerraAltarHauling
    {
        /// <summary>
        /// 规划一次送料。返回 false 表示这小人现在没得送（缺料、够不着、放不下……）。
        /// </summary>
        public static bool TryPlan(CompTerraAltar altar, Pawn pawn,
            out Thing stack, out IntVec3 dropCell, out int count)
        {
            stack = null;
            dropCell = IntVec3.Invalid;
            count = 0;
            if (altar?.parent == null || pawn?.Map == null || pawn.Map != altar.parent.Map)
            {
                return false;
            }
            ThingDef needDef;
            if (!altar.TryGetNeed(out needDef, out count) || needDef == null)
            {
                return false;
            }
            stack = FindStack(altar, pawn, needDef);
            if (stack == null)
            {
                return false;
            }
            dropCell = FindDropCell(altar, pawn);
            if (!dropCell.IsValid)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 找一摞这小人能搬的材料：不在台面上、没被禁用、能预备、能走过去。
        /// </summary>
        private static Thing FindStack(CompTerraAltar altar, Pawn pawn, ThingDef def)
        {
            Map map = pawn.Map;
            CellRect rect = altar.parent.OccupiedRect();
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
                // 已经在台面里的不算，否则会「搬来搬去」
                if (rect.Contains(t.Position))
                {
                    continue;
                }
                if (!pawn.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some))
                {
                    continue;
                }
                float d = t.Position.DistanceTo(altar.parent.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>
        /// 材料落点：台面上**除中心以外**的格。
        ///
        /// 中心 `CenterCell` 是 `FinishIngot` 生成成品的格子，材料堆在中心会挡住产出
        /// （踩坑第 21 条），所以一律放外圈八格；挑没东西、能站、走得到的那格。
        /// </summary>
        private static IntVec3 FindDropCell(CompTerraAltar altar, Pawn pawn)
        {
            Map map = pawn.Map;
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
                if (!pawn.CanReach(cell, PathEndMode.Touch, Danger.Some))
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
            return best;
        }
    }
}
