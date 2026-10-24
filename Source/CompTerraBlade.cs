using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉刃的「标记件」：只做一件事——让 MapComponent_WandState 认出
    /// 「手里拿的是泰拉刃」，从而授予/收回剑气技能。
    ///
    /// 为什么不复用 CompManaWeapon：剑气不从刃里扣魔力，而是从 8 格内的魔力源抽，
    /// 所以刃本身不需要魔力条，也不该出现「充能」按钮。
    /// </summary>
    public class CompProperties_TerraBlade : CompProperties
    {
        /// <summary>一道剑气要抽多少魔力。</summary>
        public float waveManaCost = 400f;

        /// <summary>找魔力源的半径。</summary>
        public float searchRadius = 8f;

        public CompProperties_TerraBlade()
        {
            compClass = typeof(CompTerraBlade);
        }
    }

    public class CompTerraBlade : ThingComp
    {
        public CompProperties_TerraBlade Props => (CompProperties_TerraBlade)props;
    }
}
