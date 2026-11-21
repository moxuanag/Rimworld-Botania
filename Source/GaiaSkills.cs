using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>盖亚 Boss 的技能。</summary>
    public enum GaiaSkill
    {
        None = 0,
        EarthWrath = 1,   // 大地之怒（阶段 I）
        Entangle = 2,     // 根须缠绕（阶段 I）
        ManaBeam = 3,     // 魔力射线（阶段 II）
        SummonSeeds = 4,  // 召唤盖亚之种（II / III 各一次）
        WorldSoul = 5,    // 世界之魂（III 一次性）
        Leap = 6          // 世界之跃（寻路死锁时的突进，带落点预警）
    }

    /// <summary>
    /// 技能数值与几何计算。具体调度在 Pawn_GaiaBoss 里，这里只放「怎么算」。
    /// 数值按 2026-11 的方案：冷却先减半跑通，之后再调。
    /// </summary>
    public static class GaiaSkills
    {
        // ---- 数值 ----
        public const int EarthWrathWarn = 120;      // 2 秒预警
        public const int EarthWrathCd = 240;        // 4 秒冷却
        public const float EarthWrathRadius = 6f;
        public const float EarthWrathHalfAngle = 45f;   // 90° 扇形：正面 3 格，每远一格左右各 +1
        public const float EarthWrathDamage = 60f;
        public const int EarthWrathKnockback = 3;

        // ---- 根须缠绕（分级：一阶段 1 级 / 二阶段 2 级 / 三阶段 3 级）----
        public static readonly int[] EntangleCooldownSec = { 15, 12, 9 };
        public static readonly int[] EntangleWindupSec   = { 5, 4, 3 };
        public static readonly int[] EntangleRadius      = { 7, 10, 13 };
        public static readonly int[] EntangleTargets     = { 3, 5, 7 };
        public static readonly int[] EntangleGrowSec     = { 5, 4, 3 };
        public static readonly int[] EntangleRootSec     = { 3, 4, 5 };
        public static readonly int[] EntangleDamage      = { 2, 4, 6 };

        public const int EntangleWarn = 120;        // 旧值（保留兼容，新流程用分级值）
        public const int EntangleCd = 360;          // 6 秒冷却
        public const float EntangleRange = 14f;
        public const int EntangleMaxTargets = 2;
        public const int EntangleRootTicks = 180;   // 3 秒定身

        public const int ManaBeamCharge = 150;      // 2.5 秒蓄力（用户要求减半）
        public const int ManaBeamCd = 300;          // 5 秒冷却
        public const float ManaBeamRange = 30f;
        public const float ManaBeamDamage = 40f;

        public const int SummonSeedsCount = 3;
        public const string SeedPawnKind = "Botania_GaiaSpawnling";

        public const int WorldSoulCharge = 1200;    // 20 秒蓄力
        public const float WorldSoulSpeedPerTick = 2.5f / 60f;
        public const float WorldSoulMaxRadius = 50f;
        public const float WorldSoulDamage = 90f;
        public const int WorldSoulStunTicks = 120;

        // 世界之跃（突进）：先蓄力 45 tick，在落点显示轮廓，然后瞬移
        // ---- 世界之跃（技能形态）----
        public const int LeapSkillCooldown = 900;    // 15 秒
        public const float LeapSkillRange = 30f;     // 30 格
        public const int LeapWindup = 300;           // 5 秒前摇
        public const float LeapExplosionRadius = 1.5f;  // 3x3
        public const int LeapExplosionDamage = 7;
        public const int LeapSilenceTicks = 300;     // 兜底突进后沉默 5 秒

        public const int LeapWarn = 45;
        public const float LeapImpactRadius = 2.2f;
        public const int LeapImpactDamage = 12;
        public const int LeapKnockback = 2;

        // ---- 几何 ----

        /// <summary>以 boss 为顶点、朝向 dir 的扇形格子（只取有视线、同高度的）。</summary>
        public static List<IntVec3> ConeCells(Pawn boss, IntVec3 dir, float radius, float halfAngle)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = boss.Map;
            IntVec3 origin = boss.Position;
            float dirX = dir.x - origin.x;
            float dirZ = dir.z - origin.z;
            float dirLen = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
            if (dirLen <= 0.01f)
            {
                dirZ = 1f;
                dirLen = 1f;
            }
            dirX /= dirLen;
            dirZ /= dirLen;

            int r = Mathf.CeilToInt(radius);
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    IntVec3 c = new IntVec3(origin.x + dx, 0, origin.z + dz);
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    float vx = c.x - origin.x;
                    float vz = c.z - origin.z;
                    float len = Mathf.Sqrt(vx * vx + vz * vz);
                    if (len <= 0.01f || len > radius)
                    {
                        continue;
                    }
                    float cos = (vx * dirX + vz * dirZ) / len;
                    // 容差 0.03：对角格常常刚好压在边界上，不加容差会被排除
                    if (cos < Mathf.Cos(halfAngle * Mathf.Deg2Rad) - 0.03f)
                    {
                        continue;
                    }
                    if (!GenSight.LineOfSight(origin, c, map))
                    {
                        continue;
                    }
                    cells.Add(c);
                }
            }
            return cells;
        }

        /// <summary>
        /// 某格是否落在扇形内（距离 + 角度 + 视线）。
        /// 用几何判定而不是「格子集合包含」，可以容忍格子边缘的取整误差。
        /// </summary>
        public static bool IsInCone(Pawn boss, IntVec3 dir, IntVec3 cell, float radius, float halfAngle)
        {
            Map map = boss.Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            IntVec3 origin = boss.Position;
            float vx = cell.x - origin.x;
            float vz = cell.z - origin.z;
            float len = Mathf.Sqrt(vx * vx + vz * vz);
            if (len <= 0.01f || len > radius + 0.5f)
            {
                return false;
            }
            float dx = dir.x - origin.x;
            float dz = dir.z - origin.z;
            float dirLen = Mathf.Sqrt(dx * dx + dz * dz);
            if (dirLen <= 0.01f)
            {
                dz = 1f;
                dirLen = 1f;
            }
            dx /= dirLen;
            dz /= dirLen;
            float cos = (vx * dx + vz * dz) / len;
            if (cos < Mathf.Cos(halfAngle * Mathf.Deg2Rad) - 0.03f)
            {
                return false;
            }
            return GenSight.LineOfSight(origin, cell, map);
        }

        /// <summary>从 boss 指向 target 的直线格子（宽 3 格，方便玩家看清）。</summary>
        public static List<IntVec3> BeamCells(Pawn boss, IntVec3 target, float range)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = boss.Map;
            IntVec3 origin = boss.Position;
            Vector3 from = origin.ToVector3().Yto0();
            Vector3 to = target.ToVector3().Yto0();
            Vector3 dir = (to - from);
            if (dir.magnitude <= 0.01f)
            {
                return cells;
            }
            dir.Normalize();
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            for (float d = 1f; d <= range; d += 0.5f)
            {
                for (int w = -1; w <= 1; w++)
                {
                    Vector3 p = from + dir * d + perp * w;
                    IntVec3 c = p.ToIntVec3();
                    if (!c.InBounds(map) || cells.Contains(c))
                    {
                        continue;
                    }
                    cells.Add(c);
                }
            }
            return cells;
        }

        /// <summary>圆环上的格子（用于世界之魂的波前）。</summary>
        public static List<IntVec3> RingCells(Map map, IntVec3 center, float radiusMin, float radiusMax)
        {
            List<IntVec3> cells = new List<IntVec3>();
            int rMax = Mathf.CeilToInt(radiusMax);
            for (int dx = -rMax; dx <= rMax; dx++)
            {
                for (int dz = -rMax; dz <= rMax; dz++)
                {
                    IntVec3 c = new IntVec3(center.x + dx, 0, center.z + dz);
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    float len = Mathf.Sqrt(dx * dx + dz * dz);
                    if (len >= radiusMin && len <= radiusMax)
                    {
                        cells.Add(c);
                    }
                }
            }
            return cells;
        }

        // ---- 效果 ----

        public static void DamagePawnsInCells(Map map, List<IntVec3> cells, DamageDef def,
            float amount, Thing instigator, float armorPen, float bodyHeightBottomChance = 0f)
        {
            HashSet<Pawn> hit = new HashSet<Pawn>();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = cells[i].GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    Pawn p = things[j] as Pawn;
                    if (p == null || p.Dead || p.Downed || !hit.Add(p))
                    {
                        continue;
                    }
                    DamageInfo dinfo = new DamageInfo(def, amount, armorPen, -1f, instigator);
                    if (bodyHeightBottomChance > 0f && Rand.Chance(bodyHeightBottomChance))
                    {
                        dinfo.SetBodyRegion(BodyPartHeight.Bottom, BodyPartDepth.Outside);
                    }
                    p.TakeDamage(dinfo);
                }
            }
        }

        /// <summary>
        /// 对格子范围内的**所有敌对 Thing** 造成伤害（含炮塔、门、墙等建筑），
        /// 只对 Pawn 做击退。Boss 因此具备拆家能力。
        /// </summary>
        public static void DamageHostileThingsInCells(Map map, Faction bossFaction, List<IntVec3> cells,
            DamageDef def, float amount, Thing instigator, float armorPen, bool knockback, IntVec3 from)
        {
            HashSet<Thing> hit = new HashSet<Thing>();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = cells[i].GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing th = things[j];
                    if (th == null || th == instigator || th.Destroyed || !th.Spawned)
                    {
                        continue;
                    }
                    if (!hit.Add(th))
                    {
                        continue;
                    }
                    // 只打敌对阵营的东西（自己人、中立建筑不动）
                    if (th.Faction == null || bossFaction == null || !th.HostileTo(bossFaction))
                    {
                        continue;
                    }
                    th.TakeDamage(new DamageInfo(def, amount, armorPen, -1f, instigator));
                    if (knockback && th is Pawn p2 && !p2.Dead)
                    {
                        Knockback(p2, from, EarthWrathKnockback);
                    }
                }
            }
        }

        /// <summary>把目标朝远离 from 的方向推开 cells 格（落地不可走就少推）。</summary>
        public static void Knockback(Pawn p, IntVec3 from, int cells)
        {
            Map map = p.Map;
            if (map == null)
            {
                return;
            }
            IntVec3 dir = (p.Position - from);
            if (dir.LengthHorizontal <= 0)
            {
                dir = IntVec3.North;
            }
            IntVec3 best = p.Position;
            for (int i = 1; i <= cells; i++)
            {
                IntVec3 next = p.Position + dir * i;
                if (!next.InBounds(map) || !next.Walkable(map))
                {
                    break;
                }
                best = next;
            }
            if (best != p.Position)
            {
                p.Position = best;
                p.pather?.StopDead();
            }
        }

        /// <summary>点燃目标（如果有火）。</summary>
        public static void TryIgnite(Pawn p, float chance)
        {
            if (p == null || p.Dead || !Rand.Chance(chance))
            {
                return;
            }
            p.TryAttachFire(0.5f, null);
        }
    }
}
