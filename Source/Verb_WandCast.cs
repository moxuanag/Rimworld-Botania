using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 施法选点时的绘制：
    /// 1. 6 格可施法范围画在小人身上（蓝）；
    /// 2. 这次法术的影响半径画在鼠标所在格（绿）。
    ///
    /// 放在 Verb 上而不是 Ability 上：这一版引擎的 Ability.DrawHighlight 不可重写，
    /// Verb.DrawHighlight 可以。
    ///
    /// 两道保险：ability 为空时什么都不画（父类方法会因为空 ability 抛 NRE 打挂 GUI，
    /// 所以这里也不调 base）；整体 try/catch，异常只记一次日志。
    /// </summary>
    public class Verb_WandCast : Verb_CastAbility
    {
        private static readonly Color EffectRing = new Color(0.42f, 0.95f, 0.58f, 0.70f);
        private static readonly Color RangeRing = new Color(0.42f, 0.80f, 1f, 0.55f);

        private const int ErrorLogKey = 0x5B07A1;

        public override void DrawHighlight(LocalTargetInfo target)
        {
            try
            {
                Pawn caster = CasterPawn;
                if (caster == null || caster.Map == null)
                {
                    return;
                }

                // 可施法范围：跟在小人身上，让玩家知道能点到哪儿
                float range = verbProps?.range ?? 0f;
                if (range > 0.5f)
                {
                    GenDraw.DrawRadiusRing(caster.Position, range, RangeRing);
                }

                if (!target.IsValid || !target.Cell.InBounds(caster.Map))
                {
                    return;
                }

                // 影响范围：跟在鼠标上
                if (ability is Ability_WandSpell spell)
                {
                    float effect = spell.EffectRadius;
                    if (effect > 0.5f)
                    {
                        GenDraw.DrawRadiusRing(target.Cell, effect, EffectRing);
                    }
                }

                GenDraw.DrawTargetHighlight(target);
            }
            catch (Exception e)
            {
                // 只记一次，绝不让异常冒到 GUI 层
                Log.ErrorOnce("Botania.Verb_WandCast.DrawHighlight 异常：" + e, ErrorLogKey);
            }
        }
    }
}
