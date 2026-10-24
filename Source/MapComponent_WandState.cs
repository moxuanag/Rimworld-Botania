using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 每 60 tick 扫一遍地图上的玩家小人，按手里的武器授予/收回技能：
    /// 基础森林法杖 → 绽花 / 就地转化；泰拉刃 → 剑气（2026-10-24 加）。
    ///
    /// 判定用的是 CompWand（只有基础森林法杖挂了它），不是 CompManaWeapon。
    /// 法杖改型出来的狙击型 / 爆破型同样带 CompManaWeapon（它们要蓄能、要开火），
    /// 但**不该有这两个法术**，所以不能按 CompManaWeapon 判断。
    ///
    /// 之所以用轮询而不是装备事件：这一版引擎不会给装备的 Comp 派发装备/卸下通知。
    /// </summary>
    public class MapComponent_WandState : MapComponent
    {
        private const int Interval = 60;

        private static readonly string[] SpellDefNames =
        {
            "Botania_SpellBloom",
            "Botania_SpellConvert"
        };

        private static readonly string[] TerraWaveDefNames =
        {
            "Botania_TerraWave"
        };

        private int ticks;

        private List<AbilityDef> spellDefs;

        private List<AbilityDef> terraWaveDefs;

        public MapComponent_WandState(Map map) : base(map)
        {
        }

        private List<AbilityDef> SpellDefs
        {
            get
            {
                if (spellDefs == null)
                {
                    spellDefs = new List<AbilityDef>();
                    for (int i = 0; i < SpellDefNames.Length; i++)
                    {
                        AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(SpellDefNames[i]);
                        if (def != null)
                        {
                            spellDefs.Add(def);
                        }
                    }
                }
                return spellDefs;
            }
        }

        private List<AbilityDef> TerraWaveDefs
        {
            get
            {
                if (terraWaveDefs == null)
                {
                    terraWaveDefs = LoadDefs(TerraWaveDefNames);
                }
                return terraWaveDefs;
            }
        }

        private static List<AbilityDef> LoadDefs(string[] names)
        {
            List<AbilityDef> list = new List<AbilityDef>();
            for (int i = 0; i < names.Length; i++)
            {
                AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(names[i]);
                if (def != null)
                {
                    list.Add(def);
                }
            }
            return list;
        }

        public override void MapComponentTick()
        {
            ticks++;
            if (ticks < Interval)
            {
                return;
            }
            ticks = 0;
            SyncAll();
        }

        private void SyncAll()
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return;
            }
            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(player);
            for (int i = 0; i < pawns.Count; i++)
            {
                SyncPawn(pawns[i]);
            }
        }

        private void SyncPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.equipment == null)
            {
                return;
            }
            ThingWithComps primary = pawn.equipment.Primary;
            // 只认 CompWand：法杖改型的两个变体没有它，因此拿不到这两个法术
            bool hasWand = primary?.GetComp<CompWand>() != null;
            bool hasBlade = primary?.GetComp<CompTerraBlade>() != null;

            SyncAbilities(pawn, SpellDefs, hasWand);
            SyncAbilities(pawn, TerraWaveDefs, hasBlade);
        }

        private void SyncAbilities(Pawn pawn, List<AbilityDef> defs, bool shouldHave)
        {
            Pawn_AbilityTracker tracker = pawn.abilities;
            if (tracker == null)
            {
                return;
            }
            for (int i = 0; i < defs.Count; i++)
            {
                AbilityDef def = defs[i];
                bool known = tracker.GetAbility(def) != null;
                if (shouldHave && !known)
                {
                    tracker.GainAbility(def);
                }
                else if (!shouldHave && known)
                {
                    tracker.RemoveAbility(def);
                }
            }
        }

    }
}
