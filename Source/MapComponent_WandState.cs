using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 每 60 tick 扫一遍地图上的玩家小人，做一件事：
    /// 手里有**基础森林法杖** → 授予「绽花」「就地转化」两个技能；没有 → 收回。
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

        private int ticks;

        private List<AbilityDef> spellDefs;

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
            // 只认 CompWand：法杖改型的两个变体没有它，因此拿不到这两个法术
            bool hasWand = pawn.equipment.Primary?.GetComp<CompWand>() != null;

            SyncAbilities(pawn, hasWand);
        }

        private void SyncAbilities(Pawn pawn, bool hasWand)
        {
            Pawn_AbilityTracker tracker = pawn.abilities;
            if (tracker == null)
            {
                return;
            }
            List<AbilityDef> defs = SpellDefs;
            for (int i = 0; i < defs.Count; i++)
            {
                AbilityDef def = defs[i];
                bool known = tracker.GetAbility(def) != null;
                if (hasWand && !known)
                {
                    tracker.GainAbility(def);
                }
                else if (!hasWand && known)
                {
                    tracker.RemoveAbility(def);
                }
            }
        }

    }
}
