using Verse;

namespace Botania
{
    /// <summary>
    /// 每 TickRare 推进所有血肉核心的种子计时。
    /// 存在的理由：ThingComp.CompTick 在 Pawn 上实测没有可靠触发（检视栏倒计时一直不动），
    /// MapComponent 的 tick 是必然执行的，用它当作外部时钟最稳。
    /// </summary>
    public class MapComponent_BotaniaCores : MapComponent
    {
        private const int Interval = 250;

        private int counter;

        public MapComponent_BotaniaCores(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            counter++;
            if (counter < Interval)
            {
                return;
            }
            counter = 0;

            var registry = CompFleshmassCoreSeeds.Registry;
            for (int i = registry.Count - 1; i >= 0; i--)
            {
                CompFleshmassCoreSeeds comp = registry[i];
                if (comp == null || comp.parent == null || comp.parent.Map != map)
                {
                    continue;
                }
                comp.Advance(Interval);
            }
        }
    }
}
