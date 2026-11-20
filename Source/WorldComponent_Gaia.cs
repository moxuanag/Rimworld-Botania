using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Botania
{
    /// <summary>
    /// 世界级缓存：记住我们创建出来的盖亚阵营。
    ///
    /// 背景：Botania_Gaia 是 hidden + requiredCountAtGameStart=0 的阵营，
    /// 引擎会在世界运行中清理「没有据点也没有小人的隐藏阵营」，
    /// 于是仅靠 FirstFactionOfDef 查找会在下一次调用时找不到，导致**重复创建**。
    /// 这里用 WorldComponent 持有一个引用并写进存档，保证同一个存档只有一个盖亚阵营。
    /// </summary>
    public class WorldComponent_Gaia : WorldComponent
    {
        public Faction gaiaFaction;

        public WorldComponent_Gaia(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref gaiaFaction, "gaiaFaction");
        }
    }
}
