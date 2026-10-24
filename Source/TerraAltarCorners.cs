using System.Collections.Generic;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉凝聚台的「四角基座」几何：唯一一份实现，放置预览、选中提示、Comp 判定都用它。
    ///
    /// 3×3 台面的四个**对角**格就是基座位：整体矩形向外扩一格后取四个角。
    /// 单独写一份是为了避免三处各写一遍偏移，日后改尺寸时漏改。
    /// </summary>
    public static class TerraAltarCorners
    {
        public static List<IntVec3> Of(CellRect rect)
        {
            return new List<IntVec3>
            {
                new IntVec3(rect.minX - 1, 0, rect.minZ - 1),
                new IntVec3(rect.maxX + 1, 0, rect.minZ - 1),
                new IntVec3(rect.minX - 1, 0, rect.maxZ + 1),
                new IntVec3(rect.maxX + 1, 0, rect.maxZ + 1)
            };
        }
    }
}
