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

        private static List<IntVec3> FilterParity(List<IntVec3> cells, int parity)
        {
            List<IntVec3> result = new List<IntVec3>(cells.Count / 2 + 1);
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (((c.x + c.z) & 1) == parity)
                {
                    result.Add(c);
                }
            }
            return result;
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
                    // 世界之魂：红区（闪烁）+ 绿区（墙后安全）
                    if (boss.TryGetWorldSoulZones(out List<IntVec3> red, out List<IntVec3> safe,
                            out List<IntVec3> blockers, out Color redColor))
                    {
                        // 全部格子都画（不抽样、不隔帧），保证「整片」而不是稀稀拉拉；
                        // 闪烁靠颜色亮暗切换实现。
                        if (red != null && red.Count > 0)
                        {
                            GenDraw.DrawFieldEdges(red, redColor);
                        }
                        if (safe != null && safe.Count > 0)
                        {
                            GenDraw.DrawFieldEdges(safe, new Color(0.30f, 1f, 0.40f, 0.60f));
                        }
                        if (blockers != null && blockers.Count > 0)
                        {
                            GenDraw.DrawFieldEdges(blockers, new Color(0.55f, 0.55f, 0.6f, 0.5f));
                        }
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
