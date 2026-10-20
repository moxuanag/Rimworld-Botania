using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 森林法杖的两个「法术」。
    /// 做成 verb 而不是 gizmo，是因为这一版引擎只把**verb**的命令显示到小人身上
    /// （原版跳跃背包的「跳跃」按钮就是同一条路），装备 Comp 的 gizmo 不显示。
    /// 因此施法需要先选中施法者并征召，然后用按钮指定目标格。
    /// </summary>
    public abstract class Verb_WandSkill : Verb
    {
        protected CompWand FindWand()
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
            return primary.GetComp<CompWand>();
        }

        protected override bool TryCastShot()
        {
            CompWand wand = FindWand();
            Pawn caster = CasterPawn;
            if (wand == null || caster == null || caster.Map == null)
            {
                return false;
            }

            IntVec3 cell = CurrentTarget.Cell;
            if (!cell.IsValid || !cell.InBounds(caster.Map))
            {
                // 目标落在图外时退化成"对自己施法"
                cell = caster.Position;
            }
            return Apply(wand, cell, caster.Map);
        }

        protected abstract bool Apply(CompWand wand, IntVec3 cell, Map map);
    }

    /// <summary>绽花：让目标格周围半径 4 内的植物长一截。</summary>
    public class Verb_WandBloom : Verb_WandSkill
    {
        protected override bool Apply(CompWand wand, IntVec3 cell, Map map)
        {
            return wand.TryBloomAt(cell, map);
        }
    }

    /// <summary>就地转化：把目标格周围的木料变成活木、石砖变成活石。</summary>
    public class Verb_WandConvert : Verb_WandSkill
    {
        protected override bool Apply(CompWand wand, IntVec3 cell, Map map)
        {
            return wand.TryConvertAt(cell, map);
        }
    }
}
