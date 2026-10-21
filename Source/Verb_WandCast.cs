using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 施法选点时的绘制。
    /// 1. 鼠标所在格画「这次法术的影响半径」（绽花 4 格 / 转化 1.5 格）；
    /// 2. 小人身上画「6 格可施法范围」。
    ///
    /// 放在 Verb 上而不是 Ability 上：这一版引擎的 Ability.DrawHighlight 不可重写，
    /// Verb.DrawHighlight 可以。
    ///
    /// 注意：这里**不能**调 base.DrawHighlight —— 父类实现会碰 ability 相关状态，
    /// 一抛异常就会把整个 GUI 打断（表现为点技能后 UI 消失）。所以全部自己画，
    /// 并且整体 try/catch，任何意外只记一次日志、不往外抛。
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
