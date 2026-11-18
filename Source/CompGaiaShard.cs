using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚碎块的组件配置。碎块被摧毁后就地生成传送门。
    /// </summary>
    public class CompProperties_GaiaShard : CompProperties
    {
        public string portalDefName = "Botania_GaiaPortal";

        public CompProperties_GaiaShard()
        {
            compClass = typeof(CompGaiaShard);
        }
    }

    /// <summary>
    /// 盖亚碎块：从天砸下来的晶体块。玩家摧毁它之后，原地开一道盖亚传送门。
    /// 用 PostDestroy 而不是自定义 ThingClass，避免为了一个回调多写一个类型。
    /// </summary>
    public class CompGaiaShard : ThingComp
    {
        private bool spawnedPortal;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (spawnedPortal || previousMap == null || mode == DestroyMode.WillReplace)
            {
                return;
            }
            spawnedPortal = true;

            CompProperties_GaiaShard p = (CompProperties_GaiaShard)props;
            ThingDef portalDef = DefDatabase<ThingDef>.GetNamedSilentFail(p.portalDefName);
            if (portalDef == null)
            {
                Log.Error("[Botania] 盖亚碎块找不到传送门 Def: " + p.portalDefName);
                return;
            }

            IntVec3 pos = parent.Position;
            Rot4 rot = parent.Rotation;
            Building portal = GenSpawn.Spawn(portalDef, pos, previousMap, rot, WipeMode.Vanish) as Building;
            if (portal == null)
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaPortalAppeared.Label".Translate(),
                "Botania.GaiaPortalAppeared.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(portal));
        }
    }
}
