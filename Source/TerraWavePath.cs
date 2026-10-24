using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>剑气扫到的一格，以及它在这条剑气里的「第几步」。</summary>
    public struct WaveCell
    {
        public IntVec3 cell;
        public int step;
    }

    /// <summary>
    /// 剑气的命中路径：从施法者 → 目标方向铺满 48 格，**三格宽**。
    ///
    /// 结算（`Ability_TerraWave`）和瞄准预览（`Verb_TerraWave`）都必须用**同一份**，
    /// 否则会出现「预览打到的格和实际打到的格不一致」这种最难查的错。
    ///
    /// 宽度取法：每一步在「中心格 + 它周围 8 格」里挑**离剑气轴线最近的三格**。
    /// 为什么不用「沿固定垂直方向偏 ±1」：正对着走时两者一样，但斜着走时固定垂直方向
    /// 落在对角格里，中间会留缝，看起来是一排点而不是一条带。按「离轴线最近」挑，
    /// 横向和斜向都是连续的三格宽。
    ///
    /// 伤害按**步数**算（同一横排三格伤害相同），加宽不改变 30 → 10 的递减手感。
    /// </summary>
    public static class TerraWavePath
    {
        /// <summary>剑气长度（格）。2026-10-24 玩家要求「在当前 12 格基础上 ×4」→ 48。</summary>
        public const int MaxCells = 48;

        /// <summary>剑气宽度：1 表示中线左右各一格，即三格宽。</summary>
        public const int HalfWidth = 1;

        public static List<WaveCell> Build(IntVec3 origin, IntVec3 aim, Map map)
        {
            List<WaveCell> result = new List<WaveCell>();
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
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            int width = HalfWidth * 2 + 1;
            List<KeyValuePair<float, IntVec3>> cand = new List<KeyValuePair<float, IntVec3>>(9);

            for (int step = 1; step <= MaxCells; step++)
            {
                IntVec3 center = origin + new IntVec3(
                    Mathf.RoundToInt(dir.x * step), 0, Mathf.RoundToInt(dir.z * step));

                cand.Clear();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        IntVec3 cell = center + new IntVec3(dx, 0, dz);
                        if (!cell.InBounds(map))
                        {
                            continue;
                        }
                        Vector3 v = cell.ToVector3Shifted() - origin.ToVector3Shifted();
                        cand.Add(new KeyValuePair<float, IntVec3>(Mathf.Abs(Vector3.Dot(v, perp)), cell));
                    }
                }
                cand.Sort((a, b) => a.Key.CompareTo(b.Key));

                int taken = 0;
                for (int i = 0; i < cand.Count && taken < width; i++)
                {
                    IntVec3 cell = cand[i].Value;
                    if (!done.Add(cell))
                    {
                        continue;
                    }
                    WaveCell wc;
                    wc.cell = cell;
                    wc.step = step;
                    result.Add(wc);
                    taken++;
                }
            }
            return result;
        }

        /// <summary>第 step 步的伤害：最近 30，最远 10，线性递减（总长 48 步）。</summary>
        public static float DamageAt(int step)
        {
            float t = MaxCells <= 1 ? 0f : (step - 1) / (float)(MaxCells - 1);
            return Mathf.Lerp(30f, 10f, t);
        }
    }
}
