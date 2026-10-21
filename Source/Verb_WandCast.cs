using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 施法选点时的绘制：把「这一发会影响多大范围」画在**鼠标所在格**上，
    /// 而不是套在小人身上。
    /// 之所以放在 Verb 而不是 Ability 上：这一版引擎的 Ability.DrawHighlight 不可重写，
    /// Verb.DrawHighlight 可以。
    /// </summary>
    public class Verb_WandCast : Verb_CastAbility
    {
        private static readonly Color EffectRing = new Color(0.42f, 0.95f, 0.58f, 0.70f);

        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);
            if (!target.IsValid)
            {
                return;
            }
            Ability_WandSpell spell = ResolveSpell();
            if (spell == null)
            {
                return;
            }
            Map map = CasterPawn?.Map;
            if (map == null || !target.Cell.InBounds(map))
            {
                return;
            }
            float radius = spell.EffectRadius;
            if (radius > 0.5f)
            {
                GenDraw.DrawRadiusRing(target.Cell, radius, EffectRing);
            }
        }

        /// <summary>
        /// 正常情况下引擎会把 ability 挂到这个 verb 上；
        /// 万一没有，就反过来在小人身上找"用的是这根 verb"的那个法杖技能。
        /// </summary>
        private Ability_WandSpell ResolveSpell()
        {
            if (ability is Ability_WandSpell direct)
            {
                return direct;
            }
            Pawn caster = CasterPawn;
            if (caster?.abilities == null)
            {
                return null;
            }
            foreach (Ability candidate in caster.abilities.AllAbilitiesForReading)
            {
                if (candidate is Ability_WandSpell spell && spell.verb == this)
                {
                    return spell;
                }
            }
            return null;
        }
    }
}
