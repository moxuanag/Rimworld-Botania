using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 地图 overlay（范围环 / 圆环）的公共绘制工具。
    ///
    /// 为什么需要它：<see cref="GenDraw.DrawRadiusRing"/> 有两个硬限制，
    /// 每个需要画大范围环的建筑都会踩：
    ///
    ///  1. 半径上限是 <c>GenRadial.MaxRadialPatternRadius</c>（约 79 格，引擎用 20000 格的预计算表）。
    ///     超过就每帧报 "Cannot draw radius ring of radius N: not enough squares in the precalculated list"
    ///     并且**什么都不画**。
    ///  2. 自己用 <c>GenDraw.DrawLineBetween</c> 补画时，**必须**传 layer 抬到
    ///     <c>AltitudeLayer.MetaOverlays</c>，否则线段贴在 y≈0，被地形/建筑盖住，
    ///     玩家看到的就是「点了没反应」。
    ///
    /// 所以统一走 <see cref="DrawRadiusRingSafe"/>：
    /// 能在引擎能力内就用引擎的环（好看、性能好），超限才用线段画（并自动抬高）。
    /// </summary>
    public static class BotaniaOverlays
    {
        /// <summary>线段环的分段数（128 段在 100 格半径下仍然平滑）。</summary>
        private const int LineSegments = 128;

        /// <summary>
        /// 安全的半径环绘制：自动处理引擎半径上限与渲染高度问题。
        /// </summary>
        /// <param name="center">圆心格</param>
        /// <param name="radius">半径（格）</param>
        public static void DrawRadiusRingSafe(IntVec3 center, float radius)
        {
            if (radius <= 0.1f)
            {
                return;
            }
            if (radius <= GenRadial.MaxRadialPatternRadius)
            {
                GenDraw.DrawRadiusRing(center, radius);
                return;
            }
            DrawRingWithLines(center, radius);
        }

        /// <summary>超限半径用线段画圆（抬到 MetaOverlays，保证可见）。</summary>
        private static void DrawRingWithLines(IntVec3 center, float radius)
        {
            float layer = AltitudeLayer.MetaOverlays.AltitudeFor();
            Vector3 c = center.ToVector3Shifted();
            c.y = 0f;
            Vector3 prev = c + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= LineSegments; i++)
            {
                float ang = i * Mathf.PI * 2f / LineSegments;
                Vector3 cur = c + new Vector3(
                    Mathf.Sin(ang) * radius,
                    0f,
                    Mathf.Cos(ang) * radius);
                // 3 参重载 = 用 layer 抬高 + 内置白色线材质（与强制目标线同一套渲染）
                GenDraw.DrawLineBetween(prev, cur, layer);
                prev = cur;
            }
        }
    }
}
