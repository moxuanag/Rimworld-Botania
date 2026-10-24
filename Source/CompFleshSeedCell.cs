using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 挂在血肉之种上的「细胞信息」：选中一枚种子时告诉玩家它是哪朵启命英的细胞、
    /// 现在多少岁、花跑到第几代了。没有覆盖它的启命英就不显示。
    /// </summary>
    public class CompProperties_FleshSeedCell : CompProperties
    {
        public CompProperties_FleshSeedCell()
        {
            compClass = typeof(CompFleshSeedCell);
        }
    }

    public class CompFleshSeedCell : ThingComp
    {
        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned || parent.Map == null)
            {
                return null;
            }

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, 8f, true))
            {
                CompManaLifeOrchid orchid = thing.TryGetComp<CompManaLifeOrchid>();
                if (orchid == null)
                {
                    continue;
                }
                int age;
                int generation;
                if (orchid.TryGetCellState(parent.Position, out age, out generation))
                {
                    return "Botania.FleshSeedCellInfo".Translate(
                        generation.ToString(),
                        age.ToString(),
                        orchid.Props.cellLifespan.ToString());
                }
            }
            return null;
        }
    }
}
