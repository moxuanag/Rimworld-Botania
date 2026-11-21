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
            public bool mendsWounds;   // 泰拉套装：每 2 秒治愈一处伤口
        }

        private static readonly SetDef[] Sets =
        {
            new SetDef
            {
                armor = "Botania_GaiaArmor",
                helmet = "Botania_GaiaHelmet",
                buff = "Botania_GaiaSetBonus",
                mendsWounds = false,
            },
            new SetDef
            {
                armor = "Botania_TerrasteelArmor",
                helmet = "Botania_TerrasteelHelmet",
                buff = "Botania_TerrasteelBlessing",
                mendsWounds = true,
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

            Faction player = Faction.OfPlayer;
            var colonists = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.health == null || pawn.apparel == null)
                {
                    continue;
                }
                // 玩家阵营的所有小人（含奴隶/囚犯），不再只限 FreeColonistsSpawned
                if (player != null && pawn.Faction != player)
                {
                    continue;
                }
                if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
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
                        BotaniaDebug.Log("[Botania][套装] " + pawn.LabelShort + " 获得 " + buffDef.defName);
                        // 用户要求：套装 buff 不再弹消息（只在 Hediff 面板可见）
                    }
                    else if (!complete && existing != null)
                    {
                        pawn.health.RemoveHediff(existing);
                        BotaniaDebug.Log("[Botania][套装] " + pawn.LabelShort + " 失去 " + set.buff);
                    }

                    // 泰拉套装：每 2 秒（本方法每 120 tick 调一次）治愈一处伤口
                    if (complete && set.mendsWounds)
                    {
                        MendOneWound(pawn);
                    }
                }
            }
        }

        /// <summary>治愈一处伤口：优先最严重的非永久 Hediff_Injury，直接移除。</summary>
        private static void MendOneWound(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null)
            {
                return;
            }
            Hediff_Injury worst = null;
            float worstSeverity = 0f;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury inj && inj.Severity > 0.01f && !inj.IsPermanent())
                {
                    if (inj.Severity > worstSeverity)
                    {
                        worstSeverity = inj.Severity;
                        worst = inj;
                    }
                }
            }
            if (worst == null)
            {
                return;
            }
            string label = worst.Label;
            pawn.health.RemoveHediff(worst);
            BotaniaDebug.Log("[Botania][愈合] " + pawn.LabelShort + " 治愈了「" + label + "」");
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
