using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    public class CompProperties_RadiusRing : CompProperties
    {
        /// <summary>选中该建筑时画出的范围半径。</summary>
        public float radius = 6f;

        public CompProperties_RadiusRing()
        {
            compClass = typeof(CompRadiusRing);
        }
    }

    /// <summary>
    /// 选中建筑时显示它的作用范围。
    /// 蓝图预览用的是 PlaceWorker，这里补的是"建好之后左键选中也能看见"。
    /// </summary>
    public class CompRadiusRing : ThingComp
    {
        public CompProperties_RadiusRing Props => (CompProperties_RadiusRing)props;

        public override void PostDraw()
        {
            if (!parent.Spawned)
            {
                return;
            }
            if (Find.Selector == null || !Find.Selector.IsSelected(parent))
            {
                return;
            }
            GenDraw.DrawRadiusRing(parent.Position, Props.radius);
        }
    }
}
