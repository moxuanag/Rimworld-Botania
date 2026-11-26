using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力池的绘制类：在建筑上画一条实时魔力条（照原版 <c>Building_Battery</c> 的做法）。
    ///
    /// 为什么需要它：魔力池的储量只存在于 <see cref="CompManaStorage"/> 里，原版 <see cref="Building"/>
    /// 不会画它，玩家必须点开检视栏才知道池子里还有多少魔力。原版电池用
    /// <see cref="GenDraw.DrawFillableBar"/> 在建筑正上方画一根条，这里照抄同一条路。
    ///
    /// 尺寸由 <c>def.size</c> 推导，不写死：
    ///   1×1 简易魔力池 = 0.70 × 0.22
    ///   3×3 魔力钢池   = 2.10 × 0.34
    ///
    /// 存档兼容：本类**不新增任何 Scribe 字段**，储量仍然只由 <see cref="CompManaStorage"/> 存，
    /// 因此老档换 thingClass 后可以直接加载。
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_ManaPool : Building
    {
        /// <summary>填充部分的颜色：淡绿，和魔力主题一致。</summary>
        private static readonly Material BarFilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.62f, 0.95f, 0.62f));

        /// <summary>空槽的颜色：暗绿灰，比原版电池的纯黑柔和一些。</summary>
        private static readonly Material BarUnfilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.22f, 0.26f, 0.22f));

        /// <summary>条的宽度占建筑宽度的比例（3×3 池子留出两边各 0.45 格）。</summary>
        private const float BarWidthFactor = 0.70f;

        /// <summary>1×1 池子的条高。</summary>
        private const float BarHeightSmall = 0.22f;

        /// <summary>3×3 池子的条高。</summary>
        private const float BarHeightLarge = 0.34f;

        /// <summary>条与槽之间的留白（原版电池是 0.15，池子小一点用 0.10）。</summary>
        private const float BarMargin = 0.10f;

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            // 组件可能因为 Def 配错 / 旧档迁移期间暂时不存在：直接不画，绝不在绘制层抛异常
            CompManaStorage comp = this.TryGetComp<CompManaStorage>();
            if (comp == null)
            {
                return;
            }

            try
            {
                GenDraw.FillableBarRequest r = new GenDraw.FillableBarRequest
                {
                    center = drawLoc + Vector3.up * 0.1f,
                    size = new Vector2(
                        def.size.x * BarWidthFactor,
                        Mathf.Lerp(BarHeightSmall, BarHeightLarge, (def.size.x - 1f) / 2f)),
                    fillPercent = comp.ManaPercent,
                    filledMat = BarFilledMat,
                    unfilledMat = BarUnfilledMat,
                    margin = BarMargin,
                    rotation = Rotation
                };
                GenDraw.DrawFillableBar(r);
            }
            catch (System.Exception e)
            {
                // 每帧绘制里抛异常会打断整层 GUI：只报一次，然后静默放弃这一帧
                Log.ErrorOnce("Botania: Building_ManaPool.DrawAt failed: " + e, 0x4D50_1A);
            }
        }
    }
}
