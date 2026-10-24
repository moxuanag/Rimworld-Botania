using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    public class CompProperties_TerraPick : CompProperties
    {
        /// <summary>检查间隔（tick）。15 tick ≈ 0.25 秒，够跟上采矿进度又不至于每帧算。</summary>
        public int intervalTicks = 15;

        /// <summary>相邻格是否必须划过采矿标记。默认 false：自然岩石/矿脉顺手带走。</summary>
        public bool requireDesignation = false;

        public CompProperties_TerraPick()
        {
            compClass = typeof(CompTerraPick);
        }
    }

    /// <summary>
    /// 泰拉碎岩镐：装备后采矿时，**周围八格和正在挖的那格同步被挖**。
    ///
    /// 实现思路（关键）：不自己算挖掘速度，而是**照抄中间那格的进度**——
    /// 每隔 `intervalTicks` 记录一次中心矿物的耐久，把「掉了多少」原样打到周围八格上。
    /// 这样无论小人技能、采矿速度、石头种类、难度系数怎么变，九格永远同步，
    /// 不需要我去猜原版每一击多少伤害（踩坑：猜错就会出现「周围比中间快/慢」）。
    ///
    /// 只对自然岩石与矿脉生效（`Mineable`），不会顺手啃掉玩家自己的墙。
    /// 中心那格被挖穿时，把**剩余耐久**一次性补到周围，做到「挖完这格，周围同时崩」。
    ///
    /// 为什么要 `tickerType Normal`：物品不写在 Def 里的 tickerType 就永远不 tick
    /// （踩坑：自充能之类全失效）。
    /// </summary>
    public class CompTerraPick : ThingComp
    {
        private CompProperties_TerraPick Props => (CompProperties_TerraPick)props;

        private IntVec3 lastCenter = IntVec3.Invalid;
        private float lastHp;
        private bool tracking;

        public override void CompTick()
        {
            if (!parent.IsHashIntervalTick(Props.intervalTicks))
            {
                return;
            }
            Pawn pawn = (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Downed || pawn.InMentalState)
            {
                Reset();
                return;
            }
            Job job = pawn.jobs?.curJob;
            if (job == null || job.def != JobDefOf.Mine || !job.targetA.IsValid)
            {
                Reset();
                return;
            }

            IntVec3 center = job.targetA.HasThing ? job.targetA.Thing.Position : job.targetA.Cell;
            Map map = pawn.Map;
            if (!center.InBounds(map))
            {
                Reset();
                return;
            }

            Mineable mine = center.GetFirstMineable(map);
            if (mine == null || mine.Destroyed)
            {
                // 中心被挖穿了：把最后那截耐久补给周围，让它们同时崩
                if (tracking && lastHp > 0f)
                {
                    SpreadDamage(pawn, map, center, lastHp);
                }
                Reset();
                return;
            }

            if (!tracking || center != lastCenter)
            {
                // 换了一格：重新开始记账，不补伤害
                tracking = true;
                lastCenter = center;
                lastHp = mine.HitPoints;
                return;
            }

            float delta = lastHp - mine.HitPoints;
            lastHp = mine.HitPoints;
            if (delta > 0.01f)
            {
                SpreadDamage(pawn, map, center, delta);
            }
        }

        private void SpreadDamage(Pawn pawn, Map map, IntVec3 center, float amount)
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
                    if (other == null || other.Destroyed)
                    {
                        continue;
                    }
                    other.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amount, 1f, -1f,
                        pawn, null, weaponDef));
                }
            }
        }

        private void Reset()
        {
            tracking = false;
            lastCenter = IntVec3.Invalid;
            lastHp = 0f;
        }

        public override string CompInspectStringExtra()
        {
            if (!Props.requireDesignation)
            {
                return "Botania.TerraPickSpread".Translate();
            }
            return null;
        }
    }
}
