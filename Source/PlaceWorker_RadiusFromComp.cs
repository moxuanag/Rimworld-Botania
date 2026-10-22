using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 通用的「放置时画作用范围」：半径直接读 Def 上的 CompProperties_RadiusRing。
    /// 这样新增任何带范围的建筑，只要挂上 RadiusRing 就能同时获得
    /// 1) 放置时的预览环  2) 选中时的常驻环（由 MapComponent_Rings 负责）。
    /// </summary>
    public class PlaceWorker_RadiusFromComp : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            float radius = RadiusOf(def);
            if (radius <= 0f)
            {
                return;
            }
            // 本引擎传进来的 center 已经是占用区的中点，不能再按 size 偏移
            GenDraw.DrawRadiusRing(center, radius);
        }

        /// <summary>从 Def 的 comps 里找 RadiusRing 的半径；没有就返回 0。</summary>
        public static float RadiusOf(ThingDef def)
        {
            if (def == null || def.comps == null)
            {
                return 0f;
            }
            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties_RadiusRing ring = def.comps[i] as CompProperties_RadiusRing;
                if (ring != null)
                {
                    return ring.radius;
                }
            }
            return 0f;
        }
    }
}
