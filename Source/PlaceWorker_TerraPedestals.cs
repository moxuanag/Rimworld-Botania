using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 放置 / 移动泰拉凝聚台时，把四个基座的位置直接画在地上。
    ///
    /// 为什么要有：3×3 的台子看起来方方正正，玩家不会知道「基座要摆在**对角**那一格」，
    /// 也不会知道差一根就罢工；只靠检视栏那句「还缺 1 根」等于让人拆了重猜。
    /// 这里在蓝图阶段就把四个点标出来，放完以后由 MapComponent_Rings 继续显示。
    ///
    /// 注意：本引擎传进 DrawGhost 的 center 已经是**占用区的中点**
    /// （3×3 时是正中那格），不要再按 size 偏移一次，否则整组提示会跑到右上角
    /// （PlaceWorker_ManaRadius 实机踩过同一个坑）。
    /// </summary>
    public class PlaceWorker_TerraPedestals : PlaceWorker
    {
        private static readonly Color FreeColor = new Color(0.55f, 0.88f, 0.95f, 0.85f);
        private static readonly Color HaveColor = new Color(0.55f, 0.92f, 0.62f, 0.85f);
        private static readonly Color BlockedColor = new Color(0.95f, 0.72f, 0.35f, 0.9f);

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            if (def == null || def.Size.x < 2 || def.Size.z < 2)
            {
                return;
            }

            CellRect rect = CellRect.CenteredOn(center, def.Size.x, def.Size.z);
            List<IntVec3> cells = TerraAltarCorners.Of(rect);

            Map map = thing?.Map ?? Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            // 三色：已有基座（绿）/ 空位可放（天蓝）/ 被别的建筑占住（橙）
            CompProperties_TerraAltar props = def.GetCompProperties<CompProperties_TerraAltar>();
            ThingDef pedestalDef = props?.pedestalDef;

            List<IntVec3> free = new List<IntVec3>();
            List<IntVec3> have = new List<IntVec3>();
            List<IntVec3> blocked = new List<IntVec3>();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }
                bool blockedHere = false;
                bool haveHere = false;
                List<Thing> things = cell.GetThingList(map);
                for (int k = 0; k < things.Count; k++)
                {
                    Thing t = things[k];
                    if (t.def == null || t.def.category == ThingCategory.Mote)
                    {
                        continue;
                    }
                    if (pedestalDef != null && t.def == pedestalDef)
                    {
                        haveHere = true;
                        continue;
                    }
                    if (t.def.passability == Traversability.Impassable || t.def.building != null)
                    {
                        blockedHere = true;
                    }
                }
                if (haveHere)
                {
                    have.Add(cell);
                }
                else if (blockedHere)
                {
                    blocked.Add(cell);
                }
                else
                {
                    free.Add(cell);
                }
            }

            if (have.Count > 0)
            {
                GenDraw.DrawFieldEdges(have, HaveColor);
            }
            if (free.Count > 0)
            {
                GenDraw.DrawFieldEdges(free, FreeColor);
            }
            if (blocked.Count > 0)
            {
                GenDraw.DrawFieldEdges(blocked, BlockedColor);
            }
        }
    }
}
