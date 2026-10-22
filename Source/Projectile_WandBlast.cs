using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 爆破型法杖的子弹：命中后额外在落点炸一个「十字」。
    ///
    /// 伤害由本类自行逐格结算，而不是交给 GenExplosion：
    /// 走引擎爆炸时，实测伤害比面板数字高（0.6.0 打魔力钢墙测到每格 80，
    /// 而按 DamageWorker.Apply 的算式 10 × buildingDamageFactorImpassable(4)
    /// 应为 40），逐条读反编译后仍未定位到那多出来的一倍。
    /// 与其留下一个说不清的数字，不如把伤害握在自己手里：
    /// 对十字 5 格里的每个 Thing 显式调一次 TakeDamage，每格每物只结算一次，
    /// 不受取格、视线、墙体扩散这些逻辑影响。
    ///
    /// 视觉与音效仍然借引擎的爆炸表现（damAmount 传 0，不造成伤害），
    /// 屏幕震动、闪光、爆炸音都和原版一致。
    ///
    /// 伤害口径：
    /// · 子弹本体 40，由 base.Impact 结算，只打被直接命中的目标
    ///   （会吃到武器品质的伤害倍率：传奇 ×1.5）
    /// · 十字爆炸每格 10，经 TakeDamage 结算；
    ///   建筑仍会吃到 Bomb 对建筑的倍率，这一层在引擎的 DamageWorker.Apply 里
    /// </summary>
    public class Projectile_WandBlast : Bullet
    {
        /// <summary>十字半径，只用于爆炸的视觉尺寸。</summary>
        private const float CrossRadius = 1.4f;

        /// <summary>每格爆炸伤害（以 Pawn 为基准，建筑由引擎倍率放大）。</summary>
        private const int BlastDamage = 10;

        private const float BlastArmorPen = 0.4f;

        private static readonly List<Thing> TmpThings = new List<Thing>();

        private static readonly HashSet<Thing> Damaged = new HashSet<Thing>();

        protected override void Impact(Thing hitThing, bool blockedByShield)
        {
            // 必须在 base.Impact 之前取：那一步会销毁子弹，之后 Map 变 null
            Map map = Map;
            IntVec3 center = hitThing != null && hitThing.Spawned
                ? hitThing.PositionHeld
                : DestinationCell;

            base.Impact(hitThing, blockedByShield);

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

                        t.TakeDamage(new DamageInfo(
                            def: DamageDefOf.Bomb,
                            amount: BlastDamage,
                            armorPenetration: BlastArmorPen,
                            angle: angle,
                            instigator: Launcher,
                            hitPart: null,
                            weapon: null,
                            category: DamageInfo.SourceCategory.ThingOrUnknown,
                            intendedTarget: null));
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
