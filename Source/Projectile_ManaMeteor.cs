using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 远古陨击花的「召唤陨石」弹体。
    ///
    /// 复用原版的 <c>SkyfallerBase</c> 机制（就是原版陨石事件用的那套）：
    /// 弹体一出膛就把自己变成一枚**从天空落下的陨石**（Botania_MeteorIncoming），
    /// 陨石按 Skyfaller 的抛物线落到落点并爆炸，爆炸半径与伤害由
    /// <c>&lt;skyfaller&gt;</c> 块决定（7.45 格 / Botania_HeavyManaBomb = 275 伤害）。
    ///
    /// 这样做的好处：
    ///   · 观感与原版陨石完全一致（下落弧线、投影、落地扬尘、震屏）
    ///   · 不自己造运动逻辑，原版改动也不会漏
    ///   · 仍然走引擎的命中判定：<paramref name="usedTarget"/> 是已算好偏差的落点，
    ///     所以 forcedMissRadius 的散布照常生效
    /// </summary>
    public class Projectile_ManaMeteor : Projectile_Explosive
    {
        private const string MeteorDefName = "Botania_MeteorIncoming";

        public override void Launch(
            Thing launcher,
            UnityEngine.Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire = false,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            Map map = Map;
            if (map == null)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            // 优先用引擎算好的落点（含 forcedMiss 偏差），退回到目标格
            IntVec3 cell = usedTarget.IsValid ? usedTarget.Cell : intendedTarget.Cell;
            if (!cell.InBounds(map))
            {
                cell = intendedTarget.IsValid ? intendedTarget.Cell : Position;
            }
            if (!cell.InBounds(map))
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            ThingDef meteor = DefDatabase<ThingDef>.GetNamedSilentFail(MeteorDefName);
            if (meteor != null)
            {
                SkyfallerMaker.SpawnSkyfaller(meteor, cell, map);
            }
            else
            {
                BotaniaDebug.Log("[陨击花] 找不到陨石 Def " + MeteorDefName + "，退化为普通爆炸");
                GenExplosion.DoExplosion(cell, map, def.projectile.explosionRadius, def.projectile.damageDef, this);
            }

            Destroy(DestroyMode.Vanish);
        }
    }
}
