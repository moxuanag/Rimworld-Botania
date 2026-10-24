using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 剑气的命中路径：从施法者 → 目标方向铺满 12 格。
    ///
    /// 结算（`Ability_TerraWave`）和瞄准预览（`Verb_TerraWave`）都必须用**同一份**，
    /// 否则会出现「预览打到的格和实际打到的格不一致」这种最难查的错。
    /// </summary>
    public static class TerraWavePath
    {
        public const int MaxCells = 12;

        public static List<IntVec3> Cells(IntVec3 origin, IntVec3 aim, Map map)
        {
            List<IntVec3> result = new List<IntVec3>();
            HashSet<IntVec3> done = new HashSet<IntVec3>();
            if (map == null || !origin.InBounds(map) || !aim.InBounds(map))
            {
                return result;
            }
            Vector3 dir = (aim.ToVector3Shifted() - origin.ToVector3Shifted()).normalized;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return result;
            }
            for (int step = 1; step <= MaxCells; step++)
            {
                IntVec3 cell = origin + new IntVec3(
                    Mathf.RoundToInt(dir.x * step), 0, Mathf.RoundToInt(dir.z * step));
                if (!cell.InBounds(map) || !done.Add(cell))
                {
                    continue;
                }
                result.Add(cell);
            }
            return result;
        }

        /// <summary>第 step 格的伤害：最近 30，最远 10，线性递减。</summary>
        public static float DamageAt(int index, int total)
        {
            float t = total <= 1 ? 0f : index / (float)(total - 1);
            return Mathf.Lerp(30f, 10f, t);
        }
    }
}
