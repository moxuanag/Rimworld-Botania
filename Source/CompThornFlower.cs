using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 荆棘花的属性。
    /// 行为：花自带 manaCapacity 的缓冲，战斗时先用缓冲，不够再从网络抽。
    /// 只在「范围内没有敌人」时缓慢回充，因此断网时还能撑一阵。
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

        /// <summary>花自身的魔力缓冲上限。</summary>
        public float manaCapacity = 150f;

        /// <summary>缓冲回充速度（魔力/秒），只在没有敌人时生效。</summary>
        public float rechargePerSecond = 25f;

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
            if (manaCapacity < 0f)
            {
                yield return "thorn flower: manaCapacity 不能为负";
            }
        }
    }

    /// <summary>
    /// 荆棘花：魔力驱动的范围防御花。
    /// 自带 150 缓冲；结算时先扣缓冲，不够才向附近的魔力池 / 中继塔要。
    /// </summary>
    public class CompThornFlower : ThingComp
    {
        private int ticksToPulse;
        private int rechargeTickCounter;
        private float mana;

        public CompProperties_ThornFlower Props
        {
            get { return (CompProperties_ThornFlower)props; }
        }

        public float Mana
        {
            get { return mana; }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (ticksToPulse <= 0)
            {
                ticksToPulse = Props.pulseTicks;
            }
            if (mana < 0f)
            {
                mana = 0f;
            }
            if (mana > Props.manaCapacity)
            {
                mana = Props.manaCapacity;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mana, "botaniaThornMana", 0f);
        }

        public override void CompTick()
        {
            Map map = parent.Map;
            if (map == null || !parent.Spawned)
            {
                return;
            }

            // 回充：只在没有敌人时进行，避免战斗中被网络实时补满
            rechargeTickCounter++;
            if (rechargeTickCounter >= 60)
            {
                rechargeTickCounter = 0;
                Recharge(map);
            }

            ticksToPulse--;
            if (ticksToPulse > 0)
            {
                return;
            }
            ticksToPulse = Props.pulseTicks;
            Pulse(map);
        }

        /// <summary>没有敌人时，从网络把缓冲补满。</summary>
        private void Recharge(Map map)
        {
            if (mana >= Props.manaCapacity - 0.01f)
            {
                return;
            }
            if (Props.rechargePerSecond <= 0f)
            {
                return;
            }
            // 战斗中不回充：先看范围内有没有活着的敌对目标
            if (CollectTargets(map).Count > 0)
            {
                return;
            }
            float want = Mathf.Min(Props.manaCapacity - mana, Props.rechargePerSecond);
            float got = DrainAcross(map, parent.Position, want);
            if (got > 0f)
            {
                mana = Mathf.Min(Props.manaCapacity, mana + got);
            }
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

            // ── 伤害：先算这一轮付得起几个（缓冲 + 网络） ──
            int affordable;
            if (Props.manaPerTarget <= 0f)
            {
                affordable = targets.Count;
            }
            else
            {
                float available = mana + TotalAvailableMana(map, origin);
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

        /// <summary>从网络里抽魔力，返回实际抽到的量。</summary>
        private float DrainAcross(Map map, IntVec3 origin, float amount)
        {
            float left = amount;
            int guard = 0;
            while (left > 0.01f && guard++ < 16)
            {
                IManaSource source = ManaSourceUtil.FindNearest(origin, map, Props.sourceRadius);
                if (source == null)
                {
                    break;
                }
                float got = source.DrainMana(left);
                if (got <= 0.01f)
                {
                    break;
                }
                left -= got;
            }
            return amount - left;
        }

        /// <summary>为一次命中支付魔力：先扣缓冲，不足再从网络补。</summary>
        private bool TryPayMana(Map map, IntVec3 origin)
        {
            float cost = Props.manaPerTarget;
            if (cost <= 0f)
            {
                return true;
            }

            // 缓冲够：直接扣缓冲，不碰网络
            if (mana >= cost)
            {
                mana -= cost;
                return true;
            }

            // 缓冲不够：把缓冲用光，剩下的向网络要
            float remainder = cost - mana;
            mana = 0f;
            float got = DrainAcross(map, origin, remainder);
            if (got <= 0.01f)
            {
                return false;
            }
            // 只抽到一部分时按比例算作一次不完整的命中，仍然打出去
            return true;
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
            float network = TotalAvailableMana(map, parent.Position);
            int count = CollectTargets(map).Count;
            if (count == 0)
            {
                return "Botania.Thorn.Idle".Translate(mana, Props.manaCapacity);
            }
            int affordable = Props.manaPerTarget <= 0f
                ? count
                : (int)((mana + network) / Props.manaPerTarget);
            if (affordable > Props.maxTargets)
            {
                affordable = Props.maxTargets;
            }
            if (affordable > count)
            {
                affordable = count;
            }
            return "Botania.Thorn.Status".Translate(count, affordable, mana, Props.manaCapacity, Props.damagePerPulse);
        }
    }
}
