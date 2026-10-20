using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力弹的发射逻辑：开火前先扣 50 魔力。
    /// 扣不出来就不开火（并把原因提示给玩家），而不是打出一发空气弹。
    /// </summary>
    public class Verb_ManaBolt : Verb_Shoot
    {
        private const int WarnIntervalTicks = 180;

        private int lastWarnTick = -99999;

        protected override bool TryCastShot()
        {
            CompManaWeapon weapon = FindWeapon();
            if (weapon == null)
            {
                // 不是以法杖形态使用（比如 NPC 或其他情况），走原版逻辑
                return base.TryCastShot();
            }

            float cost = weapon.Props.manaPerShot;
            if (!weapon.TrySpend(cost))
            {
                if (Find.TickManager != null && Find.TickManager.TicksGame - lastWarnTick > WarnIntervalTicks)
                {
                    lastWarnTick = Find.TickManager.TicksGame;
                    Messages.Message(
                        "Botania.WandOutOfMana".Translate(cost.ToString("F0"), weapon.Mana.ToString("F0")),
                        MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            bool fired = base.TryCastShot();
            if (!fired)
            {
                // 没真正打出去就把魔力退回去，避免白扣
                weapon.AddMana(cost);
            }
            return fired;
        }

        private CompManaWeapon FindWeapon()
        {
            Pawn pawn = CasterPawn;
            if (pawn == null || pawn.equipment == null)
            {
                return null;
            }
            ThingWithComps primary = pawn.equipment.Primary;
            if (primary == null)
            {
                return null;
            }
            return primary.GetComp<CompManaWeapon>();
        }
    }
}
