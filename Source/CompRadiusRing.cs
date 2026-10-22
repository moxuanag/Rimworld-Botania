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

    /// <summary>
    /// 第二圈（内圈）半径，用于「除了大范围之外还有一个贴身小范围」的建筑。
    /// 典型例子是白雏菊：外圈 6 格是吸魔范围，内圈 1.5 格是它实际能转化的那 8 格。
    /// 和 CompRadiusRing 分成两个类型，是为了让同一个建筑能同时挂两圈
    /// （TryGetComp 对同一类型只返回第一个）。
    /// </summary>
    public class CompProperties_SecondaryRing : CompProperties
    {
        /// <summary>内圈半径。1×1 建筑填 1.5 就能覆盖周围 8 格（含斜角）。</summary>
        public float radius = 1.5f;

        public CompProperties_SecondaryRing()
        {
            compClass = typeof(CompSecondaryRing);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            if (radius <= 0f)
            {
                yield return "SecondaryRing: radius 必须大于 0";
            }
        }
    }

    public class CompSecondaryRing : ThingComp
    {
        public CompProperties_SecondaryRing Props => (CompProperties_SecondaryRing)props;

        public float Radius => Props.radius;
    }
}
