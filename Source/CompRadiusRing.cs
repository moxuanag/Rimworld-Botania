using RimWorld;
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
    /// 作用范围的「数据挂件」：只存半径，不自己画。
    /// 实际绘制在 MapComponent_Rings 里，因为建筑的 Draw 不一定调到 Comp.PostDraw，
    /// 挂在 Comp 上会出现"有时有圈有时没圈"。
    /// </summary>
    public class CompRadiusRing : ThingComp
    {
        public CompProperties_RadiusRing Props => (CompProperties_RadiusRing)props;

        public float Radius => Props.radius;
    }
}
