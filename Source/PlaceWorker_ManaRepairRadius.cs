using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 放下建筑恢复器 / 装备修复器（蓝图与重定位）时，画出它真正的作用半径。
    /// 半径直接读 Def 上 CompProperties_ManaRepairer 的值，改参数不用改代码。
    /// </summary>
    public class PlaceWorker_ManaRepairRadius : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            float radius = FindRadius(def);
            if (radius > 0.1f)
            {
                GenDraw.DrawRadiusRing(center, radius);
            }
        }

        private static float FindRadius(ThingDef def)
        {
            if (def?.comps == null)
            {
                return 0f;
            }
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is CompProperties_ManaRepairer repairer)
                {
                    return repairer.radius;
                }
            }
            return 0f;
        }
    }
}
