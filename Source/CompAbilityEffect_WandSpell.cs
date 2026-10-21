using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 法杖法术的标准做法：原版 Ability + CompAbilityEffect。
    ///
    /// 原版所有"对一片区域生效"的技能都是这个结构（见 Data/Royalty 的 AgonyPulse 等）：
    ///   1. AbilityDef 里 verbProperties 用原版 Verb_CastAbility（不自定义）；
    ///   2. statBases 里写 Ability_EffectRadius —— 引擎据此在**鼠标所在格**画出作用范围；
    ///   3. comps 里挂 CompProperties_AbilityXxx，真正的效果写在 CompAbilityEffect.Apply 里。
    ///
    /// 之前的自定义 Verb/Ability 会拿到空的 ability 引用，导致 OnGUI 每帧 NRE、UI 全丢。
    /// 现在完全交给引擎，不再有任何自定义绘制代码。
    /// </summary>
    public class CompProperties_AbilityWandSpell : CompProperties_AbilityEffect
    {
        /// <summary>作用半径，必须与 AbilityDef 里的 Ability_EffectRadius 保持一致。</summary>
        public float effectRadius = 4f;

        /// <summary>每株植物消耗的魔力。</summary>
        public float manaPerPlant = 20f;

        /// <summary>每次催熟增加的成长度。</summary>
        public float bloomGrowth = 0.25f;
    }

    public class CompProperties_AbilityWandBloom : CompProperties_AbilityWandSpell
    {
        public CompProperties_AbilityWandBloom()
        {
            compClass = typeof(CompAbilityEffect_WandBloom);
        }
    }

    public class CompProperties_AbilityWandConvert : CompProperties_AbilityWandSpell
    {
        public CompProperties_AbilityWandConvert()
        {
            compClass = typeof(CompAbilityEffect_WandConvert);
        }
    }

    /// <summary>两个法术共用的部分：拿到手里的法杖。</summary>
    public abstract class CompAbilityEffect_WandSpell : CompAbilityEffect
    {
        protected CompAbilityEffect_WandSpell()
        {
        }

        protected CompWand FindWand()
        {
            Pawn caster = parent?.pawn;
            if (caster?.equipment == null)
            {
                return null;
            }
            return caster.equipment.Primary?.GetComp<CompWand>();
        }
    }

    public class CompAbilityEffect_WandBloom : CompAbilityEffect_WandSpell
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            CompWand wand = FindWand();
            Pawn caster = parent?.pawn;
            if (wand == null || caster?.Map == null || !target.IsValid)
            {
                return;
            }
            wand.TryBloomAt(target.Cell, caster.Map);
        }
    }

    public class CompAbilityEffect_WandConvert : CompAbilityEffect_WandSpell
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            CompWand wand = FindWand();
            Pawn caster = parent?.pawn;
            if (wand == null || caster?.Map == null || !target.IsValid)
            {
                return;
            }
            wand.TryConvertAt(target.Cell, caster.Map);
        }
    }
}
