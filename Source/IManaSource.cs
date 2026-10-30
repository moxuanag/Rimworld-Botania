using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 能提供魔力的东西。目前有两个实现：魔力池（CompManaStorage）和魔力中继塔（CompManaRelay）。
    /// 设备通过这个接口找"最近的可用魔力源"，因此池子和中继塔对设备是等价的，
    /// 中继塔之间也能互相抽取，天然形成链式中继。
    /// </summary>
    public interface IManaSource
    {
        /// <summary>当前存有/可提供的魔力量。</summary>
        float ManaAmount { get; }

        /// <summary>还能接受多少魔力。</summary>
        float FreeSpace { get; }

        /// <summary>尝试接受魔力，返回实际接受量。</summary>
        float AddMana(float amount);

        /// <summary>尝试抽取魔力，返回实际抽到的量。</summary>
        float DrainMana(float amount);
    }

    public static class ManaSourceUtil
    {
        /// <summary>
        /// 就近寻找可用魔力源。GenRadial 的遍历顺序本身就是由近及远，
        /// 所以第一个满足条件的即为最近的。
        /// </summary>
        /// <param name="needAtLeast">要求源至少有这么多魔力（**含等于**：刚好够用也应视为可用）。</param>
        /// <param name="exclude">要跳过的 Thing（中继塔排除自己，避免自抽自耗）。</param>
        public static IManaSource FindNearest(IntVec3 origin, Map map, float radius, float needAtLeast = 0f, Thing exclude = null)
        {
            return FindNearest(origin, map, radius, needAtLeast, exclude, false);
        }

        /// <summary>
        /// strictlyGreater=true 时要求「严格大于」，中继塔用它避免两塔互抽。
        /// 设备付款请用默认 false（含等于），否则池里刚好够也会被判成不足。
        /// </summary>
        public static IManaSource FindNearest(IntVec3 origin, Map map, float radius, float needAtLeast, Thing exclude, bool strictlyGreater)
        {
            if (map == null || radius <= 0f)
            {
                return null;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(origin, map, radius, true))
            {
                if (thing == exclude)
                {
                    continue;
                }
                IManaSource source = AsSource(thing);
                if (source == null)
                {
                    continue;
                }
                if (strictlyGreater ? source.ManaAmount > needAtLeast : source.ManaAmount >= needAtLeast)
                {
                    return source;
                }
            }
            return null;
        }

        /// <summary>把一个 Thing 视作魔力源；不是就返回 null。</summary>
        public static IManaSource AsSource(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            CompManaStorage pool = thing.TryGetComp<CompManaStorage>();
            if (pool != null)
            {
                return pool;
            }
            CompManaRelay relay = thing.TryGetComp<CompManaRelay>();
            if (relay != null)
            {
                return relay;
            }
            return null;
        }
    }
}
