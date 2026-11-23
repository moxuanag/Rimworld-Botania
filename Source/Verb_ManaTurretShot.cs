using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 炮塔花专用的射击 Verb：在原版 <see cref="Verb_Shoot"/> 之上只加一件事——
    /// **每成功打出一发，就在炮口放一个闪光 Mote**，用来表现「连射」的节奏。
    ///
    /// 为什么放在 Verb 而不是建筑里：连射是 Verb 逐发执行的，
    /// 只有在 <c>TryCastShot</c> 才能拿到「这一发真的打出去了」的时机。
    /// 命中、弹道、音效、伤害全部沿用 <see cref="Verb_Shoot"/>，不复制任何原版逻辑。
    /// </summary>
    public class Verb_ManaTurretShot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            bool fired = base.TryCastShot();
            if (fired)
            {
                TrySpawnMuzzleFlash();
            }
            return fired;
        }

        private void TrySpawnMuzzleFlash()
        {
            Building_ManaTurretGun turret = caster as Building_ManaTurretGun;
            if (turret == null || !turret.Spawned)
            {
                return;
            }
            CompManaTurret comp = turret.ManaComp;
            CompProperties_ManaTurret props = comp?.TurretProps;
            if (props == null || props.muzzleFlashMote == null)
            {
                return;
            }
            IntVec3 target = CurrentTarget.IsValid ? CurrentTarget.Cell : turret.Position;
            turret.SpawnMoteAtMuzzle(props.muzzleFlashMote, props.muzzleFlashSize, target);
        }
    }
}
