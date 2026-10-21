using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 森林法杖的两个法术（绽花 / 就地转化）。
    /// 走原版灵能技能同一条路：AbilityDef + Pawn_AbilityTracker + Command_Ability，
    /// 所以按钮样式、热键、悬停提示都与灵能一致。
    /// 魔力不在小人身上，而是存在手里那根法杖里，费用由 CompWand 结算。
    ///
    /// 交互：点按钮 → 范围跟着鼠标走 → 左键选格 → 在那一格结算。
    /// 超出 6 格就只提示、不放。
    /// </summary>
    public abstract class Ability_WandSpell : Ability
    {
        protected Ability_WandSpell(Pawn pawn) : base(pawn)
        {
            HookVerb();
        }

        protected Ability_WandSpell(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
            HookVerb();
        }

        /// <summary>
        /// 把 this 挂到自己的 cast verb 上。
        /// 这一步不做的话，Verb_CastAbility.OnGUI / DrawAttachmentExtraLabel 会访问
        /// 空的 ability 字段，每帧抛 NullReferenceException，把整个 GUI 打挂
        /// （表现为「点技能后所有 UI 消失」）。原版灵能也是在 Ability 侧完成挂载的。
        /// </summary>
        private void HookVerb()
        {
            try
            {
                if (verb is Verb_CastAbility cast && cast.ability != this)
                {
                    cast.ability = this;
                }
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("Botania: 挂载 ability 到 verb 失败：" + e, 0x5B07A2);
            }
        }

        /// <summary>手里那根法杖，没有就返回 null。</summary>
        protected CompWand FindWand()
        {
            Pawn caster = pawn;
            if (caster == null || caster.equipment == null)
            {
                return null;
            }
            return caster.equipment.Primary?.GetComp<CompWand>();
        }

        /// <summary>施法距离。</summary>
        public float CastRange => def?.verbProperties?.range ?? 6f;

        /// <summary>这次法术的影响半径，供施法选点时画圈用。</summary>
        public abstract float EffectRadius { get; }

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            HookVerb();
            CompWand wand = FindWand();
            Pawn caster = pawn;
            if (wand == null || caster == null || caster.Map == null)
            {
                return false;
            }

            IntVec3 center = target.IsValid ? target.Cell : caster.Position;
            if (!center.InBounds(caster.Map))
            {
                return false;
            }
            if (center.DistanceTo(caster.Position) > CastRange)
            {
                Messages.Message("Botania.SpellOutOfRange".Translate(CastRange.ToString("F0")),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!Apply(wand, center, caster.Map))
            {
                return false;
            }

            // 只有真正生效才进冷却，失败等于没放
            base.Activate(target, dest);
            return true;
        }

        /// <summary>真正的效果与开销，返回 false 表示什么都没发生。</summary>
        protected abstract bool Apply(CompWand wand, IntVec3 center, Map map);
    }

    public class Ability_WandBloom : Ability_WandSpell
    {
        public Ability_WandBloom(Pawn pawn) : base(pawn)
        {
        }

        public Ability_WandBloom(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
        }

        public override float EffectRadius => FindWand()?.Props.bloomRadius ?? 4f;

        protected override bool Apply(CompWand wand, IntVec3 center, Map map)
        {
            return wand.TryBloomAt(center, map);
        }
    }

    public class Ability_WandConvert : Ability_WandSpell
    {
        public Ability_WandConvert(Pawn pawn) : base(pawn)
        {
        }

        public Ability_WandConvert(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
        }

        public override float EffectRadius => FindWand()?.Props.convertRadius ?? 1.5f;

        protected override bool Apply(CompWand wand, IntVec3 center, Map map)
        {
            return wand.TryConvertAt(center, map);
        }
    }
}
