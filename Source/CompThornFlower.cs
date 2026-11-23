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
    ///
    /// 框架迁移：取魔半径 / 结算间隔 / 存档键等公共配置继承 <see cref="CompProperties_ManaDevice"/>，
    /// 本类只保留荆棘花特有的战斗参数。
    /// 旧字段 <c>sourceRadius</c> / <c>pulseTicks</c> 已并入基类 <c>searchRadius</c> / <c>workIntervalTicks</c>，
    /// 构造函数默认值取旧 XML 里的 10 / 60，XML 字段改名前后行为一致。
    /// </summary>
    public class CompProperties_ThornFlower : CompProperties_ManaDevice
    {
        /// <summary>伤害与减速的作用半径。</summary>
        public float radius = 10f;

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

        /// <summary>旧版存档里的缓冲字段名（迁移兼容：只读不写）。</summary>
        public string legacyManaKey;

        /// <summary>缓冲的存档键（带前缀，避免同一件东西上多个组件平铺撞键）。</summary>
        public string ManaSaveKey => saveKeyPrefix + "Mana";

        public CompProperties_ThornFlower()
        {
            compClass = typeof(CompThornFlower);

            // 基类配置：取魔半径 10 格、每 60 tick 结算一次。
            // 旧 XML 是 sourceRadius=10 / pulseTicks=60，且旧版无论成功、缺魔还是没目标都固定 60 tick 重来，
            // 因此 retryIntervalTicks 也取 60。
            searchRadius = 10f;
            workIntervalTicks = 60;
            retryIntervalTicks = 60;
            manaTolerance = 0.01f;
            saveKeyPrefix = "botaniaThornFlower";
            legacyManaKey = "botaniaThornMana";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            // 结算间隔 > 0 由基类检查 workIntervalTicks，这里不再重复。
            if (slowHediff == null)
            {
                yield return "thorn flower: slowHediff 未设置，减速不会生效";
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
    ///
    /// 框架迁移：计时节流（Core.ShouldWorkNow）、找魔力源（Core.FindSource）、
    /// 存档（Core.ExposeData + 带前缀的缓冲键）交给 <see cref="CompManaDevice"/> / <see cref="ManaDeviceCore"/>。
    /// 因为它是「多目标 × 按目标计费 × 缓冲优先 × 可部分支付」，与基类「固定额、凑够才扣」语义不同，
    /// 所以覆写 <see cref="SettleOnce"/> 自己控制付款，只复用基类的调度与取源。
    /// </summary>
    public class CompThornFlower : CompManaDevice
    {
        private float mana;

        public CompProperties_ThornFlower Props => (CompProperties_ThornFlower)props;

        public float Mana => mana;

        /// <summary>
        /// 覆写整套周期：一次结算里先看目标——没有敌人就回充，有敌人就减速 + 按付得起的数量扎。
        /// 旧版是「回充计数器」与「结算计数器」两个 60 tick 计数同相触发，这里合并成同一次结算，
        /// 节流只剩基类一处（AGENTS §5.3）。
        /// </summary>
        protected override bool SettleOnce()
        {
            Map map = parent.Map;
            if (map == null)
            {
                // 正常不会走到（基类 ShouldWorkNow 已要求 Spawned），保守排下一次，避免每 tick 空转。
                Core.ScheduleNext(false);
                return false;
            }

            List<Pawn> targets = CollectTargets(map);
            // 旧版 Recharge 内部会再看一遍「范围内有没有敌人」，等价于这里的 targets.Count == 0。
            bool worked = targets.Count == 0 ? Recharge() : Pulse(map, targets);
            // 旧行为：不管打没打、缺不缺魔、有没有目标，下一次都固定 60 tick 后
            // （workIntervalTicks == retryIntervalTicks == 60）。
            Core.ScheduleNext(worked);
            return worked;
        }

        // 基类要求实现的两个抽象钩子：整套周期已被 SettleOnce 覆写，这里只做保守默认。
        protected override bool WantsWork()
        {
            return parent.Spawned;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>没有敌人时，从网络把缓冲补满。</summary>
        private bool Recharge()
        {
            if (mana >= Props.manaCapacity - 0.01f)
            {
                return false;
            }
            if (Props.rechargePerSecond <= 0f)
            {
                return false;
            }
            float want = Mathf.Min(Props.manaCapacity - mana, Props.rechargePerSecond);
            float got = DrainAcross(want);
            if (got > 0f)
            {
                mana = Mathf.Min(Props.manaCapacity, mana + got);
                return true;
            }
            return false;
        }

        private bool Pulse(Map map, List<Pawn> targets)
        {
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
                return false;
            }

            bool hit = false;
            for (int i = 0; i < affordable; i++)
            {
                Pawn pawn = targets[i];
                if (!TryPayMana())
                {
                    break;
                }
                HitTarget(pawn, map);
                hit = true;
            }
            return hit;
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
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(origin, map, Props.searchRadius, true))
            {
                IManaSource source = ManaSourceUtil.AsSource(thing);
                if (source != null)
                {
                    sum += source.ManaAmount;
                }
            }
            return sum;
        }

        /// <summary>
        /// 从网络里抽魔力（由近及远、可跨多个源），返回实际抽到的量。
        /// 旧版直接调 ManaSourceUtil.FindNearest；这里改用基类的取源口（Core.FindSource），搜索半径与排除条件一致。
        /// </summary>
        private float DrainAcross(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            float left = amount;
            int guard = 0;
            while (left > 0.01f && guard++ < 16)
            {
                IManaSource source = Core.FindSource(0f);
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

        /// <summary>为一次命中支付魔力：先扣缓冲，不足再从网络补（只抽到一部分也算命中，与旧版一致）。</summary>
        private bool TryPayMana()
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
            float got = DrainAcross(remainder);
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

        /// <summary>
        /// 开发调试按钮（仅开发者模式），两个都走荆棘花自己的真实机制：
        ///   · 强制脉冲：CollectTargets → Pulse（减速 / 按魔力扎人 / 扣缓冲 / 生成 Mote 全在这条路径里）
        ///   · 灌满缓冲：反复走 Recharge（DrainAcross → Core.FindSource，从附近真实魔力源抽魔再写回缓冲）
        /// 以前那两个「直接改 mana 字段 / 直接调 SettleOnce」的按钮已删除。
        /// </summary>
        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (!Prefs.DevMode)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.ThornForcePulse".Translate(),
                defaultDesc = "Botania.Dev.ThornForcePulseDesc".Translate(),
                action = DebugForcePulse
            };
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.ThornFillMana".Translate(),
                defaultDesc = "Botania.Dev.ThornFillManaDesc".Translate(),
                action = DebugFillManaFromNetwork
            };
        }

        /// <summary>
        /// 开发调试：强制走一次真实的「收集目标 → 脉冲」路径。
        /// 没有敌人时不会凭空造目标，只提示（真实机制里脉冲本来就需要目标）。
        /// </summary>
        private void DebugForcePulse()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            List<Pawn> targets = CollectTargets(map);
            if (targets.Count == 0)
            {
                Messages.Message(
                    "Botania.Dev.ThornNoTargets".Translate(Props.radius.ToString("0")),
                    new LookTargets(parent),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }
            Pulse(map, targets);
            Messages.Message(
                "Botania.Dev.ThornPulseDone".Translate(targets.Count),
                new LookTargets(parent),
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        /// <summary>
        /// 开发调试：反复走真实回充路径 Recharge，直到缓冲满或附近抽不到魔力。
        /// 每一次都经过 DrainAcross → Core.FindSource → IManaSource.DrainMana，
        /// 会真的消耗附近魔力池里的魔力，不直接给字段赋值。
        /// </summary>
        private void DebugFillManaFromNetwork()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            if (mana >= Props.manaCapacity - 0.01f)
            {
                Messages.Message(
                    "Botania.Dev.ThornFillFull".Translate(),
                    new LookTargets(parent),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }
            float before = mana;
            int guard = 0;
            while (mana < Props.manaCapacity - 0.01f)
            {
                if (guard++ > 1000 || !Recharge())
                {
                    break;
                }
            }
            float gained = mana - before;
            if (gained <= 0.01f)
            {
                Messages.Message(
                    "Botania.Dev.ThornFillNoSource".Translate(
                        mana.ToString("0"),
                        Props.manaCapacity.ToString("0")),
                    new LookTargets(parent),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }
            Messages.Message(
                "Botania.Dev.ThornFillDone".Translate(
                    gained.ToString("0"),
                    mana.ToString("0"),
                    Props.manaCapacity.ToString("0")),
                new LookTargets(parent),
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public override string CompInspectStringExtra()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return null;
            }
            // 荆棘花的提示带「敌人数量 / 够打几个 / 储能」这些专用参数，通用文案表达不了，
            // 因此保留专用 Keyed（文案与旧版逐字一致）。
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

        public override void PostExposeData()
        {
            base.PostExposeData();
            float savedMana = mana;
            Scribe_Values.Look(ref savedMana, Props.ManaSaveKey, 0f);
            // 旧版存档兼容：旧键只读不写（回写会和新键重复占坑）
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (!Props.legacyManaKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref savedMana, Props.legacyManaKey, savedMana);
                }
                // 旧版在 PostSpawnSetup 里把缓冲夹到 [0, 容量]；这里等价地在读档后夹一次。
                savedMana = Mathf.Clamp(savedMana, 0f, Props.manaCapacity);
            }
            mana = savedMana;
        }
    }
}
