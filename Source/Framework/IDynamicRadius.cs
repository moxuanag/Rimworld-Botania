namespace Botania
{
    /// <summary>
    /// 运行时范围环的「动态半径」契约。
    ///
    /// 为什么需要它：有些建筑的作用半径会随研究档位变化（丰饶之花 5 / 6 / 7 格），
    /// 但 <see cref="CompProperties_RadiusRing.radius"/> 在 Def 里是写死的静态值。
    /// 让真正知道当前半径的组件实现本接口，<see cref="CompRadiusRing.Radius"/> 就会
    /// 优先问它，选中时画的环和实际工作范围才不会脱节。
    ///
    /// 实现者：<see cref="CompFertilityFlower"/>（返回 EffectiveRadius）。
    /// </summary>
    public interface IDynamicRingRadius
    {
        /// <summary>当前实际生效的环半径（格）。调用方需自行处理 &lt;= 0 的情况。</summary>
        float CurrentRingRadius { get; }
    }

    /// <summary>
    /// 放置预览环的「动态半径」契约。
    ///
    /// 放置预览发生在建筑还没生成的时刻，拿不到 Comp 实例，只能问 Def 上的
    /// CompProperties。所以这个接口由 **CompProperties** 实现，
    /// 由 <see cref="PlaceWorker_RadiusFromComp.RadiusOf"/> 优先读取。
    ///
    /// 实现者：<see cref="CompProperties_FertilityFlower"/>（按当前已完成的研究算）。
    /// </summary>
    public interface IDynamicPlaceRadius
    {
        /// <summary>放置预览时该画的半径（格），按「此刻」的研究状态算。</summary>
        float CurrentPlaceRadius { get; }
    }
}
