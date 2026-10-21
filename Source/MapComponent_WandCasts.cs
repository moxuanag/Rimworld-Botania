using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>一条「正在走过去施法」的待办。</summary>
    public class PendingWandCast : IExposable
    {
        public Pawn pawn;
        public IntVec3 center;
        public string abilityDefName;
        public int retries;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref abilityDefName, "abilityDefName");
            Scribe_Values.Look(ref retries, "retries", 0);
        }
    }

    /// <summary>
    /// 让小人"走过去再施法"的落地方式：这里不用自定义 JobDef，
    /// 而是下发原版一定存在的 <see cref="JobDefOf.Goto"/>，然后每 15 tick 查一次
    /// ——走到施法距离内就替他放技能。
    /// 这么绕是因为自定义 JobDef 在上一版没能生效，改成纯原版 Job + 轮询后不依赖新 Def。
    /// </summary>
    public class MapComponent_WandCasts : MapComponent
    {
        private const int Interval = 15;

        /// <summary>走不到时最多重下几次寻路任务。</summary>
        private const int MaxRetries = 3;

        private List<PendingWandCast> pending = new List<PendingWandCast>();

        private int ticks;

        public MapComponent_WandCasts(Map map) : base(map)
        {
        }

        public static void Request(Map map, Pawn pawn, IntVec3 center, string abilityDefName)
        {
            if (map == null || pawn == null)
            {
                return;
            }
            MapComponent_WandCasts comp = map.GetComponent<MapComponent_WandCasts>();
            if (comp == null)
            {
                return;
            }
            comp.Add(pawn, center, abilityDefName);
        }

        private void Add(Pawn pawn, IntVec3 center, string abilityDefName)
        {
            pending.RemoveAll(x => x.pawn == pawn);
            pending.Add(new PendingWandCast
            {
                pawn = pawn,
                center = center,
                abilityDefName = abilityDefName
            });
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0)
            {
                return;
            }
            ticks++;
            if (ticks < Interval)
            {
                return;
            }
            ticks = 0;
            ResolveAll();
        }

        private void ResolveAll()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingWandCast cast = pending[i];
                if (Drop(cast))
                {
                    pending.RemoveAt(i);
                }
            }
        }

        /// <summary>返回 true 表示这条待办已经处理完，可以从列表里去掉。</summary>
        private bool Drop(PendingWandCast cast)
        {
            Pawn pawn = cast.pawn;
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.Map != map)
            {
                return true;
            }
            if (pawn.abilities == null)
            {
                return true;
            }
            Ability_WandSpell spell = pawn.abilities.GetAbility(
                DefDatabase<AbilityDef>.GetNamedSilentFail(cast.abilityDefName)) as Ability_WandSpell;
            if (spell == null)
            {
                return true;
            }

            // 到了：替他把技能放出去
            if (pawn.Position.DistanceTo(cast.center) <= spell.CastRange)
            {
                spell.ResolvePendingCast(cast.center);
                return true;
            }

            // 还没到：Goto 任务结束后（走到或被打断）就重新下一次
            if (pawn.CurJobDef != JobDefOf.Goto)
            {
                if (cast.retries >= MaxRetries)
                {
                    Messages.Message("Botania.SpellWalkFailed".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return true;
                }
                if (!spell.StartWalkTo(cast.center))
                {
                    return true;
                }
                cast.retries++;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "pendingWandCasts", LookMode.Deep);
            if (pending == null)
            {
                pending = new List<PendingWandCast>();
            }
        }
    }
}
