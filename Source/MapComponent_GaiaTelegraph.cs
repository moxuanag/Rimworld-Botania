using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 技能预警绘制：把地图上所有盖亚 Boss 当前的预警格子画出来。
    ///
    /// 为什么放在 MapComponent：每帧绘制需要 MapComponentDraw 这类稳定钩子，
    /// 而且技能范围是「跟着 Boss 状态变」的，由组件统一读、统一画最省事。
    /// 绘制整体 try/catch，任何异常只报一次，绝不让它把 GUI 打断。
    /// </summary>
    public class MapComponent_GaiaTelegraph : MapComponent
    {
        public MapComponent_GaiaTelegraph(Map map) : base(map)
        {
        }

        public override void MapComponentDraw()
        {
            try
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn_GaiaBoss boss = pawns[i] as Pawn_GaiaBoss;
                    if (boss == null || boss.Dead)
                    {
                        continue;
                    }
                    if (!boss.TryGetTelegraph(out List<IntVec3> cells, out Color color))
                    {
                        continue;
                    }
                    if (cells.Count == 0)
                    {
                        continue;
                    }
                    GenDraw.DrawFieldEdges(cells, color);
                }
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[Botania] 技能预警绘制失败：" + e, 6612341);
            }
        }
    }
}
