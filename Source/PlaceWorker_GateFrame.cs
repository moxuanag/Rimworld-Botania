using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 精灵门上梁的放置提示：把「两根立柱该在哪」用绿框标出来。
    ///
    /// 这里**故意不做 AllowsPlacing 校验**：本引擎传给 PlaceWorker 的坐标
    /// 是占用区中点（见 PlaceWorker_RadiusFromComp 的注释），不同版本对
    /// 起点/中点的约定容易搞错，写错了会把合法摆放也拦住。
    /// 门框是否完整由 CompManaGate 每 tick 检查，不完整时检视栏会直说，
    /// 玩家不会「摆了才发现」——只是不用红色预览挡路而已。
    /// </summary>
    public class PlaceWorker_GateFrame : PlaceWorker
    {
        private static readonly Color HintColor = new Color(0.55f, 0.92f, 0.62f, 0.75f);

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            List<IntVec3> cells = PillarCells(def, center);
            if (cells.Count > 0)
            {
                GenDraw.DrawFieldEdges(cells, HintColor);
            }
        }

        /// <summary>上梁两端下方的立柱格（上梁 3 宽、立柱 3 高）。</summary>
        private static List<IntVec3> PillarCells(ThingDef def, IntVec3 center)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (def == null)
            {
                return cells;
            }
            int width = def.size.x;
            int halfLeft = (width - 1) / 2;
            int minX = center.x - halfLeft;
            int maxX = minX + width - 1;
            for (int dz = 0; dz < 3; dz++)
            {
                cells.Add(new IntVec3(minX - 1, 0, center.z - dz));
                cells.Add(new IntVec3(maxX + 1, 0, center.z - dz));
            }
            return cells;
        }
    }
}
