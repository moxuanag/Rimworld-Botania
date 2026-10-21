using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 森林法杖的两个法术。
    ///
    /// 结构照原版灵能（见 Data/Core/Defs/AbilityDefs/AbilityDefs.xml 的 PsycastBase）：
    ///   AbilityDef: abilityClass 指向自定义 Ability 子类 + 原版 Verb_CastAbility
    ///   + statBases 的 Ability_EffectRadius 由引擎在鼠标格画出作用范围。
    ///
    /// ！！这两个类名不要改、不要删 ！！
    /// 存档会按类名保存 Ability 实例（&lt;li Class="Botania.Ability_WandBloom"&gt;）。
    /// 改名或删类会让旧档加载时 "Could not find class"，引擎静默回退成
    /// RimWorld.Ability，技能就变成「有范围圈但没效果」。
    ///
    /// 注意：这里**只**重写 Activate，不碰任何绘制。
    /// 之前的 UI 崩溃是因为自定义了 Verb（拿不到 ability 引用），与 Ability 子类无关。
    /// </summary>
    public abstract class Ability_WandSpell : Ability
    {
        protected Ability_WandSpell(Pawn pawn) : base(pawn)
        {
        }

        protected Ability_WandSpell(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
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

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
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

        protected override bool Apply(CompWand wand, IntVec3 center, Map map)
        {
            return wand.TryConvertAt(center, map);
        }
    }
}
