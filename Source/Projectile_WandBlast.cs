using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 爆破型法杖的子弹：命中后额外在落点炸一个「十字」。
    ///
    /// 为什么半径写 1.4（实测过，不是估的）：
    /// 用 GenRadial.RadialCellsAround 直接跑了一遍，结果如下 ——
    ///   r=1.0 / 1.2 / 1.4 / 1.41 → 4 格（上下左右，**不含中心**）
    ///   r=1.42 / 1.5            → 8 格（斜角被算了进来）
    ///   GenRadial.NumCellsInRadius(1.4) → 5
    /// 也就是说：RadialCellsAround 只给"周围"，中心格由 GenExplosion 自己单独结算，
    /// 4 + 1 = 5 格正好是十字；而半径一旦到 1.42 就会退化成 3×3 的九宫格。
    /// 所以 1.4 不是一个随手写的数，它卡在斜角距离 1.414 之下，留 0.014 的余量。
    ///
    /// 走引擎的爆炸管线（而不是自己遍历格子逐个 TakeDamage）的好处：
    /// 护甲、掩体、友伤、建筑、火焰概率、屏幕震动全部和原版爆炸一致。
    ///
    /// 伤害怎么分：
    /// · 子弹本体 40（由 base.Impact 结算，只打被直接命中的目标）
    /// · 十字爆炸 10（含中心格）
    /// 所以被直接命中的小人合计吃 50，旁边四格各吃 10；打空时落点五个格子各吃 10。
    /// </summary>
    public class Projectile_WandBlast : Bullet
    {
        private const float CrossRadius = 1.4f;

        private const int BlastDamage = 10;

        private const float BlastArmorPen = 0.4f;

        protected override void Impact(Thing hitThing, bool blockedByShield)
        {
            // 必须在 base.Impact 之前取：那一步会把子弹销毁，之后 Map 就变 null 了
            Map map = Map;
            IntVec3 center = hitThing != null && hitThing.Spawned
                ? hitThing.PositionHeld
                : DestinationCell;

            base.Impact(hitThing, blockedByShield);

            if (map == null || !center.IsValid || !center.InBounds(map))
            {
                return;
            }

            GenExplosion.DoExplosion(
                center,
                map,
                CrossRadius,
                DamageDefOf.Bomb,
                Launcher,
                BlastDamage,
                BlastArmorPen);
        }
    }
}
