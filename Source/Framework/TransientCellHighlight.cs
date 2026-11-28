using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「一次性」格子高亮：把某一格短暂地画成某种颜色（默认 90 tick ≈ 1.5 秒），到点自动消失；
    /// 也可以登记一个很长的时长，再由调用方 <see cref="Remove"/> 主动熄掉。
    ///
    /// 用途（丰饶之花）：
    ///  · <see cref="Green"/>：判定通过、正在攒工作量准备种下的那一格。登记时给一个很长的时长，
    ///    **种下去的同一次结算里调 <see cref="Remove"/> 立刻熄灭**，所以它是「亮着直到种下」；
    ///  · <see cref="Amber"/>：判定失败的格子（格子太挤 / 相邻播种限制 / 清完仍被占），
    ///    45 tick（0.75 秒）自己消失，不需要调用方管。
    ///
    /// 生命周期：调用方 <see cref="Add"/> 登记（记地图 + 格子 + 颜色 + 到期 tick），
    /// 每个 <see cref="MapComponent.MapComponentDraw"/> 帧由对应的地图调一次 <see cref="DrawFor"/>，
    /// 过滤出「本图 + 未过期」的格子，**按颜色分桶**、每桶一次 <c>GenDraw.DrawFieldEdges</c> 画完。
    ///
    /// 几个刻意的设计：
    ///  · 过滤与绘制都在 try/catch 里 —— 绘制代码抛异常会打断整层 GUI（UI 直接消失），
    ///    所以这里吞掉异常并只 <c>Log.ErrorOnce</c> 一条，绝不外抛；
    ///  · 颜色桶（<c>bucketCells</c>）复用，每帧不分配新 List，只在出现新颜色时才建一个桶；
    ///  · 同一格重复 <see cref="Add"/> 做「覆盖」而不是堆叠：颜色与到期时间一起更新，
    ///    所以「绿 → 琥珀」只需一次 Add，不会同时画两种颜色；
    ///  · 时间统一用 <c>Find.TickManager.TicksGame</c>：游戏暂停时高亮停在那里，不会白闪。
    /// </summary>
    public static class TransientCellHighlight
    {
        /// <summary>一条待绘制记录：哪张图、哪一格、什么颜色、什么时候过期。</summary>
        private struct Entry
        {
            public int mapId;
            public IntVec3 cell;
            public int expireTick;
            public Color color;
        }

        /// <summary>默认持续 tick 数（60 tick = 1 秒，90 tick = 1.5 秒）。</summary>
        private const int DefaultDurationTicks = 90;

        /// <summary>判定通过、正在种的那一格（半透明绿）。</summary>
        public static readonly Color Green = new Color(0.45f, 0.95f, 0.45f, 0.70f);

        /// <summary>判定失败的格子（半透明琥珀）。</summary>
        public static readonly Color Amber = new Color(0.95f, 0.75f, 0.30f, 0.55f);

        /// <summary>全部登记项（所有地图共用；<see cref="DrawFor"/> 里顺手清过期项）。</summary>
        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>颜色桶的数量上限：本仓库最多用到绿 / 琥珀两种，留点余量。</summary>
        private const int MaxColorBuckets = 4;

        /// <summary>每个桶对应一种颜色（与 <see cref="bucketCells"/> 同下标）。</summary>
        private static readonly List<Color> bucketColors = new List<Color>();

        /// <summary>每个颜色桶的格子缓冲，跨帧复用（不与 <see cref="bucketColors"/> 一起被清空）。</summary>
        private static readonly List<List<IntVec3>> bucketCells = new List<List<IntVec3>>();

        /// <summary>
        /// 登记一格高亮（默认绿色）。
        /// </summary>
        /// <param name="map">所在地图（null 直接忽略）。</param>
        /// <param name="cell">要高亮的格子（越界直接忽略）。</param>
        /// <param name="durationTicks">持续多少 tick；≤ 0 时按默认 90 tick（1.5 秒）。</param>
        public static void Add(Map map, IntVec3 cell, int durationTicks = DefaultDurationTicks)
        {
            Add(map, cell, Green, durationTicks);
        }

        /// <summary>
        /// 登记一格高亮，并指定颜色。
        /// </summary>
        /// <param name="map">所在地图（null 直接忽略）。</param>
        /// <param name="cell">要高亮的格子（越界直接忽略）。</param>
        /// <param name="color">画成什么颜色（<see cref="Green"/> / <see cref="Amber"/>）。</param>
        /// <param name="durationTicks">持续多少 tick；≤ 0 时按默认 90 tick（1.5 秒）。</param>
        public static void Add(Map map, IntVec3 cell, Color color, int durationTicks = DefaultDurationTicks)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map))
            {
                return;
            }
            // TickManager 还没准备好（读档 / 初始化阶段）就先不登记：这些调用本来也不会发生在那个时候。
            int now = CurrentTick;
            if (now < 0)
            {
                return;
            }
            int duration = durationTicks > 0 ? durationTicks : DefaultDurationTicks;
            int expireTick = now + duration;
            int mapId = map.uniqueID;

            // 同一格已经有了就覆盖（颜色 + 过期时间一起更新），不新增一条。
            for (int i = 0; i < entries.Count; i++)
            {
                Entry existing = entries[i];
                if (existing.mapId == mapId && existing.cell == cell)
                {
                    existing.color = color;
                    existing.expireTick = expireTick;
                    entries[i] = existing;
                    return;
                }
            }

            PruneExpired(now);
            entries.Add(new Entry
            {
                mapId = mapId,
                cell = cell,
                expireTick = expireTick,
                color = color
            });
        }

        /// <summary>
        /// 立刻熄灭某一格的高亮（不管它原本还有多久到期）。
        /// 「绿着直到种下」就靠它：种下去的那次结算里调一次，格子马上恢复正常。
        /// </summary>
        public static void Remove(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid)
            {
                return;
            }
            int mapId = map.uniqueID;
            // 倒序：同一格（正常只有一条，容错多条）删完为止。
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (entry.mapId == mapId && entry.cell == cell)
                {
                    entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 画本图当前仍有效的格子高亮。由 <see cref="MapComponent_Rings.MapComponentDraw"/> 每帧调用一次。
        /// </summary>
        /// <param name="map">要画哪张图（其它地图的登记项会被跳过，但过期项在这里统一清掉）。</param>
        public static void DrawFor(Map map)
        {
            if (map == null)
            {
                return;
            }
            try
            {
                int now = CurrentTick;
                if (now < 0)
                {
                    return;
                }
                int mapId = map.uniqueID;

                // 颜色桶每帧清空内容、保留 List 实例（不分配新 List）。
                for (int i = 0; i < bucketColors.Count; i++)
                {
                    bucketCells[i].Clear();
                }
                bucketColors.Clear();

                // 倒序遍历：既能顺手删过期项，又不会因为删除打乱遍历。
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = entries[i];
                    if (entry.expireTick <= now)
                    {
                        entries.RemoveAt(i);
                        continue;
                    }
                    if (entry.mapId != mapId)
                    {
                        continue;
                    }
                    if (!entry.cell.InBounds(map))
                    {
                        continue;
                    }
                    BucketFor(entry.color).Add(entry.cell);
                }

                // 一种颜色画一次：DrawFieldEdges 对整批格子只建一次网格，比逐格画便宜得多。
                for (int i = 0; i < bucketColors.Count; i++)
                {
                    List<IntVec3> cells = bucketCells[i];
                    if (cells.Count > 0)
                    {
                        GenDraw.DrawFieldEdges(cells, bucketColors[i]);
                    }
                }
            }
            catch (Exception e)
            {
                // 绘制异常绝不能外抛（会打断 GUI 整层）；ErrorOnce 保证不会刷屏。
                Log.ErrorOnce("[Botania] 临时格子高亮绘制失败：" + e, 738201);
            }
        }

        /// <summary>取某种颜色对应的格子桶（没有就建一个；桶满后并进最后一桶，宁可偏色也不漏画）。</summary>
        private static List<IntVec3> BucketFor(Color color)
        {
            for (int i = 0; i < bucketColors.Count; i++)
            {
                if (SameColor(bucketColors[i], color))
                {
                    return bucketCells[i];
                }
            }
            if (bucketColors.Count >= MaxColorBuckets)
            {
                return bucketCells[bucketCells.Count - 1];
            }
            List<IntVec3> cells = new List<IntVec3>();
            bucketColors.Add(color);
            bucketCells.Add(cells);
            return cells;
        }

        /// <summary>两个颜色是否完全一致（Color 的 == 走近似比较，这里要的是「登记时给的同一个值」）。</summary>
        private static bool SameColor(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        /// <summary>清掉所有已过期的登记项。</summary>
        private static void PruneExpired(int now)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].expireTick <= now)
                {
                    entries.RemoveAt(i);
                }
            }
        }

        /// <summary>当前 tick；TickManager 还没准备好时返回 -1。</summary>
        private static int CurrentTick
        {
            get
            {
                TickManager tickManager = Find.TickManager;
                return tickManager != null ? tickManager.TicksGame : -1;
            }
        }
    }
}
