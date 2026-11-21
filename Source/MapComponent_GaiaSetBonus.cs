using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚套装检测：同时穿着盖亚甲 + 盖亚头盔时，给一个小人挂上「盖亚之赐」Hediff。
    ///
    /// 用 MapComponent 而不是 apparel 的 comp：
    /// 因为要判断「两件同时穿」，放在装备自己身上互相看不见；
    /// 而 MapComponentTick 每 120 tick 扫一次殖民者，逻辑最简单也最稳。
    ///
    /// 具体 buff 数值写在 HediffDef Botania_GaiaSetBonus 的 statOffsets 里，
    /// 以后调数值不用改代码。
    /// </summary>
    public class MapComponent_GaiaSetBonus : MapComponent
    {
        private const string ArmorDefName = "Botania_GaiaArmor";
        private const string HelmetDefName = "Botania_GaiaHelmet";
        private const string BuffDefName = "Botania_GaiaSetBonus";

        public MapComponent_GaiaSetBonus(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (Find.TickManager.TicksGame % 120 != 0)
            {
                return;
            }

            HediffDef buffDef = DefDatabase<HediffDef>.GetNamedSilentFail(BuffDefName);
            if (buffDef == null)
            {
                return;
            }

            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.health == null || pawn.apparel == null)
                {
                    continue;
                }

                bool hasSet = WearsDef(pawn, ArmorDefName) && WearsDef(pawn, HelmetDefName);
                Hediff existing = FindHediff(pawn, buffDef);

                if (hasSet && existing == null)
                {
                    Hediff hediff = HediffMaker.MakeHediff(buffDef, pawn);
                    hediff.Severity = 1f;
                    pawn.health.AddHediff(hediff);
                    Messages.Message("Botania.GaiaSetBonus.On".Translate(pawn.LabelShort),
                        new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                }
                else if (!hasSet && existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }
        }

        private static bool WearsDef(Pawn pawn, string defName)
        {
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (apparel?.def != null && apparel.def.defName == defName)
                {
                    return true;
                }
            }
            return false;
        }

        private static Hediff FindHediff(Pawn pawn, HediffDef def)
        {
            if (pawn.health.hediffSet?.hediffs == null)
            {
                return null;
            }
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def == def)
                {
                    return hediff;
                }
            }
            return null;
        }
    }
}
