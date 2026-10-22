using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 爆破型法杖的子弹：命中后额外在落点炸一个「十字」。
    ///
    /// 为什么显式传 overrideCells，而不是只给半径让引擎自己取格：
    /// 引擎的 DamageWorker.ExplosionCellsToHit 除了按半径取格，还会
    ///   1) 做 GenSight 视线过滤 —— 隔墙、隔角落的格子会被跳过；
    ///   2) 对每个「空地」再补一圈相邻墙体（adjWallCells），
    ///      也就是说贴着墙炸时，命中格会多于半径本来覆盖的数量。
    /// 这两条都会让实际伤害不可预期。实测反编译确认：
    ///   return openCells.Concat(adjWallCells);
    /// 所以这里直接把「中心 + 上下左右」算好传进去，引擎原样结算这 5 格。
    ///
    /// 走引擎爆炸管线（而不是自己遍历格子逐个 TakeDamage）的好处：
    /// 护甲、掩体、友伤、建筑倍率、火焰概率、屏幕震动全部和原版爆炸一致。
    ///
    /// 伤害怎么分：
    /// · 子弹本体 40（由 base.Impact 结算，只打被直接命中的目标）
    /// · 十字爆炸 10（5 格，含中心）
    /// 对小人：命中目标吃 40 + 10 = 50，旁边四格各吃 10。
    /// 对建筑：Bomb 这个伤害类型对不可通行建筑有 4 倍倍率
    ///        （buildingDamageFactorImpassable = 4），所以每格是 10 × 4 = 40。
    ///        这是原版爆炸行为，不是这里的 bug。
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

            GenExplosion.DoExplosion(
                center,
                map,
                CrossRadius,
                DamageDefOf.Bomb,
                Launcher,
                BlastDamage,
                BlastArmorPen,
                overrideCells: cells);
        }
    }
}
