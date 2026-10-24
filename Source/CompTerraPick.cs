using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Botania
{
    public class CompProperties_TerraPick : CompProperties
    {
        /// <summary>检查间隔（tick）。15 tick ≈ 0.25 秒，够跟上采矿进度又不至于每帧算。</summary>
        public int intervalTicks = 15;

        /// <summary>相邻格是否必须划过采矿标记。默认 false：周围自然岩石/矿脉顺手带走。</summary>
        public bool requireDesignation = false;

        public CompProperties_TerraPick()
        {
            compClass = typeof(CompTerraPick);
        }
    }

    /// <summary>
    /// 泰拉碎岩镐：装备后采矿时，**正在挖的那格和周围八格一起被挖穿**。
    ///
    /// 口径（2026-10-24 玩家定）：按**百分比**同步，不是按绝对耐久。
    /// 玩家给的例子：中心 2000 耐久的压埋机械、外圈 400 耐久的砂岩，
    /// 挖完中心那一刻外圈也要同时挖完。按绝对伤害同步会让 400 的砂岩先崩，不符合要求。
    ///
    /// 所以每一轮只算一件事：中心还剩百分之几，就把周围八格打到「相同的百分比」。
    /// 这样不管两边耐久差多少倍、石种多杂，九格永远同时挖穿。
    ///
    /// 只对 `Mineable`（自然岩石/矿脉）生效，**不会啃玩家自己的墙**。
    /// 必须写 `tickerType Normal`：物品不写就永远不 tick，整个机制静默失效。
    /// </summary>
    public class CompTerraPick : ThingComp
    {
        private CompProperties_TerraPick Props => (CompProperties_TerraPick)props;

        public override void CompTick()
        {
            if (!parent.IsHashIntervalTick(Props.intervalTicks))
            {
                return;
            }
            Pawn pawn = (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Downed || pawn.InMentalState)
            {
                return;
            }
            Job job = pawn.jobs?.curJob;
            if (job == null || job.def != JobDefOf.Mine || !job.targetA.IsValid)
            {
                return;
            }

            IntVec3 center = job.targetA.HasThing ? job.targetA.Thing.Position : job.targetA.Cell;
            Map map = pawn.Map;
            if (!center.InBounds(map))
            {
                return;
            }

            Mineable mine = center.GetFirstMineable(map);
            float progress;
            if (mine == null || mine.Destroyed || mine.MaxHitPoints <= 0f)
            {
                // 中心已经挖穿（或被别人挖掉）：周围直接补满
                progress = 1f;
            }
            else
            {
                progress = 1f - mine.HitPoints / (float)mine.MaxHitPoints;
            }
            if (progress <= 0.001f)
            {
                return;
            }
            SyncNeighbors(pawn, map, center, Mathf.Clamp01(progress));
        }

        /// <summary>把周围八格打到和中心**相同的完成百分比**。</summary>
        private void SyncNeighbors(Pawn pawn, Map map, IntVec3 center, float progress)
        {
            ThingDef weaponDef = parent.def;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    IntVec3 cell = center + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    if (Props.requireDesignation
                        && map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) == null)
                    {
                        continue;
                    }
                    Mineable other = cell.GetFirstMineable(map);
                    if (other == null || other.Destroyed || other.MaxHitPoints <= 0f)
                    {
                        continue;
                    }
                    float targetHp = other.MaxHitPoints * (1f - progress);
                    float damage = other.HitPoints - targetHp;
                    if (damage <= 0.5f)
                    {
                        continue;
                    }
                    bool willBreak = damage >= other.HitPoints - 0.01f;
                    string defName = other.def.defName;
                    int expectedYield = YieldOf(other);
                    ThingDef dropDef = other.def.building?.mineableThing;
                    if (willBreak && Prefs.DevMode)
                    {
                        Log.Message("[Botania] 碎岩镐 3×3 挖穿 " + defName + " @" + cell
                            + "（进度 " + progress.ToStringPercent() + "，应掉 " + expectedYield + "）");
                    }
                    other.TakeDamage(new DamageInfo(DamageDefOf.Blunt, damage, 1f, -1f,
                        pawn, null, weaponDef));
                    if (willBreak && Prefs.DevMode && other.Destroyed)
                    {
                        Log.Message("[Botania] 实际掉落："
                            + DropSummary(cell, map, dropDef, expectedYield));
                    }
                }
            }
        }

        private static int YieldOf(Mineable m)
        {
            BuildingProperties bp = m.def.building;
            if (bp == null || bp.mineableYield <= 0)
            {
                return 1;
            }
            return bp.mineableYield;
        }

        /// <summary>开发者模式下核对：这一格到底掉了多少（排查「只掉一个矿」）。</summary>
        private static string DropSummary(IntVec3 cell, Map map, ThingDef drop, int expectedYield)
        {
            int count = 0;
            int stacks = 0;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (drop != null && things[i].def == drop)
                {
                    count += things[i].stackCount;
                    stacks++;
                }
            }
            string what = drop != null ? drop.defName : "?";
            return what + " ×" + count + "（" + stacks + " 堆），期望 " + expectedYield
                + "，该格现有物品 " + things.Count + " 件";
        }
    }
}
