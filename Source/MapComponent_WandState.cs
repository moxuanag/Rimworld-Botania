using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 每 60 tick 扫一遍地图上的玩家小人，做两件事：
    /// 1. 手里有森林法杖 → 授予「绽花」「就地转化」两个技能；没有 → 收回；
    /// 2. 手里有森林法杖 → 挂上「魔力储备」状态并同步数值；没有 → 移除。
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
            CompManaWeapon weapon = pawn.equipment.Primary?.GetComp<CompManaWeapon>();
            bool hasWand = weapon != null;

            SyncAbilities(pawn, hasWand);
            SyncHediff(pawn, weapon);
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

        private void SyncHediff(Pawn pawn, CompManaWeapon weapon)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Botania_WandMana");
            if (def == null)
            {
                return;
            }
            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (weapon == null)
            {
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                return;
            }
            if (existing == null)
            {
                pawn.health.AddHediff(def);
                existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            }
            if (existing != null)
            {
                existing.Severity = weapon.Mana;
            }
        }
    }
}
