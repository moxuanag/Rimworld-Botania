using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 爆破型法杖的子弹：命中后额外在落点炸一个「十字」。
    ///
    /// ── 为什么伤害由这里自己算，而不是交给 GenExplosion ──
    /// 交给引擎爆炸时，实测伤害和面板数字对不上：面板 40 子弹 + 10 爆炸，
    /// 打魔力钢墙却测到「命中墙 140 / 周围墙 80 / 空地爆炸 80」。
    /// 把引擎反编译逐条读过，按代码应该是：
    ///     · 子弹 40（Bullet 伤害类型，无建筑倍率）
    ///     · 爆炸每格 10，建筑 × buildingDamageFactorImpassable(4) = 40
    ///     · 命中墙 40 + 40 = 80，周围墙 40
    /// 也就是实测值整体偏高，爆炸那侧正好是两倍。
    /// 已排除：ExplosionCellsToHit 的贴墙扩散（已改 overrideCells）、
    ///         Projectile.Impact 被调用两次（TickInterval 只会走一条路）、
    ///         Projectile.Impact 自带爆炸（它只做音效与销毁）。
    /// 剩下无法从代码确认的部分，改成把伤害握在自己手里：
    /// 对十字 5 格里的每个 Thing 显式调一次 TakeDamage，伤害值就是 BlastDamage，
    /// 每格每物只结算一次，不受取格、视线、扩散逻辑影响。
    ///
    /// 视觉与音效仍借引擎爆炸表现（damAmount 传 0，不会造成伤害），
    /// 屏幕震动、闪光、爆炸音和原版一致。
    ///
    /// 伤害口径：
    /// · 子弹本体 40，由 base.Impact 结算，只打被直接命中的目标
    /// · 十字爆炸每格 10，经 TakeDamage 结算；
    ///   建筑仍会吃到 Bomb 对建筑的倍率，这一层在引擎的 DamageWorker.Apply 里，
    ///   不经过本文件（10 × buildingDamageFactorImpassable 4 = 40）。
    /// </summary>
    public class Projectile_WandBlast : Bullet
    {
        /// <summary>十字半径，只用于爆炸的视觉尺寸。</summary>
        private const float CrossRadius = 1.4f;

        /// <summary>每格爆炸伤害（以 Pawn 为基准，建筑由引擎倍率放大）。</summary>
        private const int BlastDamage = 10;

        private const float BlastArmorPen = 0.4f;

        /// <summary>
        /// 排查期开关：打开时把「请求伤害 / 实际扣血 / 血量变化」写进日志。
        /// 数字核对完改回 false。
        /// </summary>
        private const bool DiagnosticLog = true;

        private static readonly List<Thing> TmpThings = new List<Thing>();

        private static readonly HashSet<Thing> Damaged = new HashSet<Thing>();

        protected override void Impact(Thing hitThing, bool blockedByShield)
        {
            // 必须在 base.Impact 之前取：那一步会销毁子弹，之后 Map 变 null
            Map map = Map;
            IntVec3 center = hitThing != null && hitThing.Spawned
                ? hitThing.PositionHeld
                : DestinationCell;

            if (DiagnosticLog && hitThing != null)
            {
                int before = hitThing.HitPoints;
                base.Impact(hitThing, blockedByShield);
                Log.Message(string.Format(
                    "[Botania] blast bullet -> {0}: hp {1} -> {2} (delta {3})",
                    hitThing.LabelShortCap, before, hitThing.HitPoints, before - hitThing.HitPoints));
            }
            else
            {
                base.Impact(hitThing, blockedByShield);
            }

            if (map == null || !center.InBounds(map))
            {
                return;
            }

            List<IntVec3> cells = new List<IntVec3>(5) { center };
            for (int i = 0; i < 4; i++)
            {
                IntVec3 c = center + GenAdj.CardinalDirections[i];
                if (c.InBounds(map))
                {
                    cells.Add(c);
                }
            }

            // 只借表现：damAmount 传 0，伤害由下面的循环负责
            GenExplosion.DoExplosion(
                center,
                map,
                CrossRadius,
                DamageDefOf.Bomb,
                Launcher,
                0,
                0f,
                overrideCells: cells);

            Damaged.Clear();
            try
            {
                for (int ci = 0; ci < cells.Count; ci++)
                {
                    IntVec3 cell = cells[ci];
                    TmpThings.Clear();
                    TmpThings.AddRange(map.thingGrid.ThingsListAt(cell));
                    for (int i = 0; i < TmpThings.Count; i++)
                    {
                        Thing t = TmpThings[i];
                        if (t == null || t.Destroyed)
                        {
                            continue;
                        }
                        if (t.def.category == ThingCategory.Mote || t.def.category == ThingCategory.Ethereal)
                        {
                            continue;
                        }
                        // 多格建筑可能同时落在两个格子里，只结算一次
                        if (!Damaged.Add(t))
                        {
                            continue;
                        }

                        float angle = t.Position == center
                            ? Rand.RangeInclusive(0, 359)
                            : (t.Position - center).AngleFlat;

                        DamageInfo dinfo = new DamageInfo(
                            def: DamageDefOf.Bomb,
                            amount: BlastDamage,
                            armorPenetration: BlastArmorPen,
                            angle: angle,
                            instigator: Launcher,
                            hitPart: null,
                            weapon: null,
                            category: DamageInfo.SourceCategory.ThingOrUnknown,
                            intendedTarget: null);

                        int before = t.HitPoints;
                        var res = t.TakeDamage(dinfo);

                        if (DiagnosticLog)
                        {
                            Log.Message(string.Format(
                                "[Botania] blast cell {0} {1}: asked {2}, dealt {3}, hp {4} -> {5}",
                                cell, t.LabelShortCap, BlastDamage,
                                res.totalDamageDealt, before, t.HitPoints));
                        }
                    }
                }
            }
            finally
            {
                Damaged.Clear();
                TmpThings.Clear();
            }
        }
    }
}
