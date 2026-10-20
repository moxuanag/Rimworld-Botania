using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 放置魔力池（蓝图/蓝图重定位）时，画出半径 6 的连接范围环。
    /// 这个半径对应火红莲与魔力通量场的 searchRadius：
    /// 范围内的花可以把魔力送进池子，范围内的通量场可以抽取池中魔力。
    /// </summary>
    public class PlaceWorker_ManaRadius : PlaceWorker
    {
        public const float Radius = 6f;

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            // 注意：本引擎传进来的 center 已经是建筑占用区的中点（3x3 时是正中那格），
            // 不能再按 size 偏移，否则圆心会跑到右上角（实机踩过）。
            GenDraw.DrawRadiusRing(center, Radius);
        }
    }
}
