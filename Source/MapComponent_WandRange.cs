using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 选中小人并拿着森林法杖时，画出两个范围：
    /// 青色 = 施法距离（能点多远），绿色 = 绽花的影响半径。
    /// 用 MapComponent 是因为装备上的 Comp 不会进入绘制流程，MapComponentDraw 才稳定。
    /// </summary>
    public class MapComponent_WandRange : MapComponent
    {
        private static readonly Color CastColor = new Color(0.45f, 0.80f, 1f, 0.55f);
        private static readonly Color BloomColor = new Color(0.42f, 0.95f, 0.58f, 0.70f);

        public MapComponent_WandRange(Map map) : base(map)
        {
        }

        public override void MapComponentDraw()
        {
            Selector selector = Find.Selector;
            if (selector == null)
            {
                return;
            }
            var selected = selector.SelectedObjects;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!(selected[i] is Pawn pawn) || pawn.Map != map || pawn.equipment == null)
                {
                    continue;
                }
                ThingWithComps primary = pawn.equipment.Primary;
                if (primary == null)
                {
                    continue;
                }
                CompWand wand = primary.GetComp<CompWand>();
                if (wand == null)
                {
                    continue;
                }
                GenDraw.DrawRadiusRing(pawn.Position, wand.Props.castRange, CastColor);
                GenDraw.DrawRadiusRing(pawn.Position, wand.Props.bloomRadius, BloomColor);
            }
        }
    }
}
