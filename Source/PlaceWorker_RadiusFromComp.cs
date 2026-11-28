using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 通用的「放置时画作用范围」：半径直接读 Def 上的 CompProperties_RadiusRing。
    /// 这样新增任何带范围的建筑，只要挂上 RadiusRing 就能同时获得
    /// 1) 放置时的预览环  2) 选中时的常驻环（由 MapComponent_Rings 负责）。
    ///
    /// 如果建筑还挂了 CompProperties_SecondaryRing，放置时会把内圈一起画出来，
    /// 和选中时的表现保持一致。
    /// </summary>
    public class PlaceWorker_RadiusFromComp : PlaceWorker
    {
        private static readonly Color InnerRing = new Color(0.55f, 0.92f, 0.62f, 0.85f);

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            // 本引擎传进来的 center 已经是占用区的中点，不能再按 size 偏移
            float radius = RadiusOf(def);
            if (radius > 0f)
            {
                GenDraw.DrawRadiusRing(center, radius);
            }

            float inner = SecondaryRadiusOf(def);
            if (inner > 0f)
            {
                GenDraw.DrawRadiusRing(center, inner, InnerRing);
            }
        }

        /// <summary>
        /// 从 Def 的 comps 里找半径；没有就返回 0。
        ///
        /// 优先级：任何实现了 <see cref="IDynamicPlaceRadius"/> 的 CompProperties 先答
        /// （例如丰饶之花按当前研究档位给 5/6/7 格），否则回落到
        /// <see cref="CompProperties_RadiusRing.radius"/> 这个写死的值。
        /// </summary>
        public static float RadiusOf(ThingDef def)
        {
            if (def == null || def.comps == null)
            {
                return 0f;
            }
            // 动态半径先扫一遍：它可能挂在别的 CompProperties 上（不在 RadiusRing 那个 comp 里）
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is IDynamicPlaceRadius dynamicRadius)
                {
                    return dynamicRadius.CurrentPlaceRadius;
                }
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

        /// <summary>从 Def 的 comps 里找内圈半径；没有就返回 0。</summary>
        public static float SecondaryRadiusOf(ThingDef def)
        {
            if (def == null || def.comps == null)
            {
                return 0f;
            }
            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties_SecondaryRing ring = def.comps[i] as CompProperties_SecondaryRing;
                if (ring != null)
                {
                    return ring.radius;
                }
            }
            return 0f;
        }
    }
}
