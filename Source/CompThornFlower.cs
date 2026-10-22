using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 荆棘花的属性。
    /// 行为：每 pulseTicks 结算一次；半径内所有敌对目标先吃减速（免费），
    /// 再按当前魔力余额决定能对几个目标造成伤害（由近到远）。
    /// </summary>
    public class CompProperties_ThornFlower : CompProperties
    {
        /// <summary>伤害与减速的作用半径。</summary>
        public float radius = 10f;

        /// <summary>寻找魔力池 / 中继塔的半径。</summary>
        public float sourceRadius = 10f;

        /// <summary>结算间隔（tick）。30 tick = 0.5 秒。</summary>
        public int pulseTicks = 30;

        /// <summary>每目标每结算的伤害。</summary>
        public float damagePerPulse = 3f;

        /// <summary>每目标每结算消耗的魔力。</summary>
        public float manaPerTarget = 2f;

        /// <summary>单次结算最多打几个目标。</summary>
        public int maxTargets = 12;

        /// <summary>减速 hediff（免费，只要在范围内就挂）。</summary>
        public HediffDef slowHediff;

        /// <summary>减速 hediff 每次刷新后的持续时间。</summary>
        public int slowDuration = 90;

        /// <summary>命中时在目标脚下生成的 Mote。</summary>
        public ThingDef thornMote;

        public CompProperties_ThornFlower()
        {
            compClass = typeof(CompThornFlower);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            if (slowHediff == null)
            {
                yield return "thorn flower: slowHediff 未设置，减速不会生效";
            }
            if (pulseTicks <= 0)
            {
                yield return "thorn flower: pulseTicks 必须大于 0";
            }
            if (manaPerTarget < 0f)
            {
                yield return "thorn flower: manaPerTarget 不能为负";
            }
        }
    }

    /// <summary>
    /// 荆棘花：魔力驱动的范围防御花。
    /// 敌人在范围内时，先被免费减速，再按魔力余额挨个被尖刺扎。
    /// </summary>
    public class CompThornFlower : ThingComp
    {
        private int ticksToPulse;

        public CompProperties_ThornFlower Props
        {
            get { return (CompProperties_ThornFlower)props; }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (ticksToPulse <= 0)
            {
                ticksToPulse = Props.pulseTicks;
            }
        }

        public override void CompTick()
        {
            Map map = parent.Map;
            if (map == null || !parent.Spawned)
            {
                return;
            }
            ticksToPulse--;
            if (ticksToPulse > 0)
            {
                return;
            }
            ticksToPulse = Props.pulseTicks;
            Pulse(map);
        }

        private void Pulse(Map map)
        {
            List<Pawn> targets = CollectTargets(map);
            if (targets.Count == 0)
            {
                return;
            }

            // 由近到远，保证魔力有限时先扎离花最近的那批
            IntVec3 origin = parent.Position;
            targets.Sort(delegate(Pawn a, Pawn b)
            {
                int da = origin.DistanceToSquared(a.Position);
                int db = origin.DistanceToSquared(b.Position);
                return da.CompareTo(db);
            });

            // ── 减速：不消耗魔力，全员生效 ──
            for (int i = 0; i < targets.Count; i++)
            {
                ApplySlow(targets[i]);
            }

            // ── 伤害：先算这一轮付得起几个 ──
            int affordable = 0;
            if (Props.manaPerTarget <= 0f)
            {
                affordable = targets.Count;
            }
            else
            {
                float available = TotalAvailableMana(map, origin);
                affordable = (int)(available / Props.manaPerTarget);
            }
            if (affordable > Props.maxTargets)
            {
                affordable = Props.maxTargets;
            }
            if (affordable > targets.Count)
            {
                affordable = targets.Count;
            }
            if (affordable <= 0)
            {
                return;
            }

            for (int i = 0; i < affordable; i++)
            {
                Pawn pawn = targets[i];
                if (!TryPayMana(map, origin))
                {
                    break;
                }
                HitTarget(pawn, map);
            }
        }

        private List<Pawn> CollectTargets(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
            {
                Pawn pawn = thing as Pawn;
                if (pawn == null)
                {
                    continue;
                }
                if (!pawn.Spawned || pawn.Dead || pawn.Downed)
                {
                    continue;
                }
                // 只打敌对势力：不碰自己人、盟友、中立商队
                if (!pawn.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                result.Add(pawn);
            }
            return result;
        }

        private float TotalAvailableMana(Map map, IntVec3 origin)
        {
            float sum = 0f;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(origin, map, Props.sourceRadius, true))
            {
                IManaSource source = ManaSourceUtil.AsSource(thing);
                if (source != null)
                {
                    sum += source.ManaAmount;
                }
            }
            return sum;
        }

        /// <summary>为一次命中支付魔力；抽不够就返回 false（不会白扣）。</summary>
        private bool TryPayMana(Map map, IntVec3 origin)
        {
            if (Props.manaPerTarget <= 0f)
            {
                return true;
            }
            float left = Props.manaPerTarget;
            int guard = 0;
            while (left > 0.01f && guard++ < 16)
            {
                IManaSource source = ManaSourceUtil.FindNearest(origin, map, Props.sourceRadius);
                if (source == null)
                {
                    return false;
                }
                float got = source.DrainMana(left);
                if (got <= 0.01f)
                {
                    return false;
                }
                left -= got;
            }
            return left <= 0.01f;
        }

        private void ApplySlow(Pawn pawn)
        {
            if (Props.slowHediff == null)
            {
                return;
            }
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.slowHediff);
            if (existing == null)
            {
                existing = pawn.health.AddHediff(Props.slowHediff);
            }
            if (existing == null)
            {
                return;
            }
            HediffComp_Disappears disappears = existing.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = Props.slowDuration;
            }
        }

        private void HitTarget(Pawn pawn, Map map)
        {
            DamageInfo dinfo = new DamageInfo(
                DamageDefOf.Cut,
                Props.damagePerPulse,
                0f,
                -1f,
                parent,
                null,
                null,
                DamageInfo.SourceCategory.ThingOrUnknown,
                null);
            pawn.TakeDamage(dinfo);

            if (Props.thornMote != null && pawn.Spawned)
            {
                MoteMaker.MakeStaticMote(pawn.Position, map, Props.thornMote, 1f);
            }
        }

        public override string CompInspectStringExtra()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            float available = TotalAvailableMana(map, parent.Position);
            int count = CollectTargets(map).Count;
            if (count == 0)
            {
                return "Botania.Thorn.Idle".Translate();
            }
            int affordable = Props.manaPerTarget <= 0f
                ? count
                : (int)(available / Props.manaPerTarget);
            if (affordable > Props.maxTargets)
            {
                affordable = Props.maxTargets;
            }
            if (affordable > count)
            {
                affordable = count;
            }
            return "Botania.Thorn.Status".Translate(count, affordable, Props.damagePerPulse);
        }
    }
}
