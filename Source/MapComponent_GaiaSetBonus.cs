using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 套装检测：同时穿着整套装备时给小人挂上对应的增益 Hediff。
    ///
    /// 目前两套：
    ///   盖亚套装（盖亚甲 + 盖亚头盔）  -> Botania_GaiaSetBonus
    ///   泰拉套装（泰拉甲 + 泰拉头盔）  -> Botania_TerrasteelBlessing（移速 +100% + 每 2 秒愈合一处伤口）
    ///
    /// 引擎会自动实例化所有 MapComponent 子类（Map.FillComponents 反射扫描），
    /// 不需要在 XML 里注册。
    /// </summary>
    public class MapComponent_GaiaSetBonus : MapComponent
    {
        private sealed class SetDef
        {
            public string armor;
            public string helmet;
            public string buff;
            public string messageKey;
        }

        private static readonly SetDef[] Sets =
        {
            new SetDef
            {
                armor = "Botania_GaiaArmor",
                helmet = "Botania_GaiaHelmet",
                buff = "Botania_GaiaSetBonus",
                messageKey = "Botania.GaiaSetBonus.On",
            },
            new SetDef
            {
                armor = "Botania_TerrasteelArmor",
                helmet = "Botania_TerrasteelHelmet",
                buff = "Botania_TerrasteelBlessing",
                messageKey = "Botania.TerrasteelSetBonus.On",
            },
        };

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

            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.health == null || pawn.apparel == null)
                {
                    continue;
                }

                for (int s = 0; s < Sets.Length; s++)
                {
                    SetDef set = Sets[s];
                    bool complete = WearsDef(pawn, set.armor) && WearsDef(pawn, set.helmet);
                    HediffDef buffDef = DefDatabase<HediffDef>.GetNamedSilentFail(set.buff);
                    if (buffDef == null)
                    {
                        continue;
                    }
                    Hediff existing = FindHediff(pawn, buffDef);

                    if (complete && existing == null)
                    {
                        Hediff hediff = HediffMaker.MakeHediff(buffDef, pawn);
                        hediff.Severity = 1f;
                        pawn.health.AddHediff(hediff);
                        // 用户要求：套装 buff 不再弹消息（只在 Hediff 面板可见）
                    }
                    else if (!complete && existing != null)
                    {
                        pawn.health.RemoveHediff(existing);
                    }
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
