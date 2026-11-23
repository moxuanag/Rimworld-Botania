using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 炮塔型功能花的公共配置。
    ///
    /// 炮塔不是「定时结算」设备：节奏由 <see cref="Building_TurretGun"/> 的预热 / 连射 / 冷却驱动，
    /// 所以本组件**不用** CompManaDevice 的周期结算，只借用它的「就近找魔力源 + 凑够才扣 + 存档」。
    /// 付费点在 BeginBurst：付得起才发射，付不起当轮空射并短冷却重试。
    /// </summary>
    public class CompProperties_ManaTurret : CompProperties_ManaDevice
    {
        /// <summary>每轮连射消耗的魔力。</summary>
        public float manaPerBurst = 15f;

        /// <summary>付不起魔力时的重试间隔（tick）。</summary>
        public int noManaRetryTicks = 120;

        /// <summary>每发射击的枪口闪光 Mote（由 Verb 逐发触发）。</summary>
        public ThingDef muzzleFlashMote;

        /// <summary>枪口闪光大小。</summary>
        public float muzzleFlashSize = 1.1f;

        /// <summary>枪口相对中心的偏移（格，朝目标方向）。</summary>
        public float muzzleForwardOffset = 0.55f;

        /// <summary>连射起手在炮口喷的烟/光（迫击炮用）。</summary>
        public ThingDef launchSmokeMote;

        /// <summary>发射烟大小。</summary>
        public float launchSmokeSize = 1.6f;

        /// <summary>是否允许玩家强制指定目标。</summary>
        public bool forcedTargetAllowed = true;

        public CompProperties_ManaTurret()
        {
            compClass = typeof(CompManaTurret);

            searchRadius = 10f;
            manaTolerance = 0.01f;
            workIntervalTicks = 60;
            retryIntervalTicks = 60;
            saveKeyPrefix = "botaniaManaTurret";
            inspectWorkingKey = "Botania.TurretReady";
            inspectNoManaKey = "Botania.TurretNoMana";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerBurst < 0f)
            {
                yield return "manaPerBurst must not be negative.";
            }
            if (noManaRetryTicks <= 0)
            {
                yield return "noManaRetryTicks must be greater than 0.";
            }
        }
    }

    /// <summary>炮塔花的魔力组件：只回答「这轮连射付不付得起」。</summary>
    public class CompManaTurret : CompManaDevice
    {
        public CompProperties_ManaTurret TurretProps => (CompProperties_ManaTurret)props;

        /// <summary>炮塔由 Building_TurretGun 驱动，组件自身不参与 tick。</summary>
        public override void CompTick()
        {
        }

        protected override bool WantsWork()
        {
            return false;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>附近魔力是否够打一轮（只查不扣）。</summary>
        public bool HasEnoughManaForBurst()
        {
            CompProperties_ManaTurret p = TurretProps;
            if (p.manaPerBurst <= 0f)
            {
                return true;
            }
            IManaSource source = Core.FindSource(p.manaPerBurst);
            return source != null && source.ManaAmount + p.manaTolerance >= p.manaPerBurst;
        }

        /// <summary>
        /// 炮塔的检视文案必须**实时**判断魔力，不能沿用基类缓存的 HasMana：
        /// 炮塔没有周期结算（CompTick 是空的），HasMana 只在真正开火付款时才更新，
        /// 所以刚造好时它仍是初始值 true —— 玩家会看到「魔力充足」但附近根本没有魔力池。
        /// </summary>
        public override string CompInspectStringExtra()
        {
            if (parent == null || parent.Map == null)
            {
                return null;
            }
            CompProperties_ManaTurret p = TurretProps;
            return HasEnoughManaForBurst()
                ? p.WorkingKey.Translate()
                : p.NoManaKey.Translate();
        }

        /// <summary>取附近最近的魔力源（不扣款），供建筑侧的调试工具使用。</summary>
        public IManaSource FindNearbySource()
        {
            return Core.FindSource(0f);
        }

        /// <summary>支付一轮连射的魔力（凑够才扣）。</summary>
        public bool TryPayBurst()
        {
            CompProperties_ManaTurret p = TurretProps;
            if (p.manaPerBurst <= 0f)
            {
                return true;
            }
            return Core.TryPay(p.manaPerBurst);
        }
    }

    /// <summary>
    /// 炮塔花的建筑类：在 <see cref="Building_TurretGun"/> 的连射流程里插入魔力付费，
    /// 并提供「按真实机制触发」的开发调试按钮。除付费与调试按钮外不改变原版行为。
    /// </summary>
    public class Building_ManaTurretGun : Building_TurretGun
    {
        private CompManaTurret manaComp;

        public CompManaTurret ManaComp
        {
            get
            {
                if (manaComp == null)
                {
                    manaComp = this.TryGetComp<CompManaTurret>();
                }
                return manaComp;
            }
        }

        private CompProperties_ManaTurret TurretProps => ManaComp?.TurretProps;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            manaComp = this.TryGetComp<CompManaTurret>();
        }

        /// <summary>允许玩家点选强制目标（原版只有可操控炮塔能用）。</summary>
        protected override bool CanSetForcedTarget
        {
            get
            {
                CompProperties_ManaTurret p = TurretProps;
                return p != null && p.forcedTargetAllowed;
            }
        }

        // 注意：**不要**在这里因为缺魔力而返回 Invalid。
        // 原版炮塔靠 currentTargetInt 驱动 TurretTop 旋转（瞄准动作），
        // 一旦缺魔就不索敌，炮塔会完全不转向，玩家看到的就是「死物」。
        // 缺魔只在 BeginBurst 拒绝发射，瞄准/转向照常。

        /// <summary>连射起手付费；付不起则本轮空射并短冷却重试。</summary>
        protected override void BeginBurst()
        {
            CompManaTurret comp = ManaComp;
            if (comp != null && !comp.TryPayBurst())
            {
                currentTargetInt = LocalTargetInfo.Invalid;
                CompProperties_ManaTurret p = TurretProps;
                burstCooldownTicksLeft = p != null ? p.noManaRetryTicks : 120;
                return;
            }
            base.BeginBurst();
            SpawnLaunchSmoke();
        }

        /// <summary>
        /// 选中时的范围提示。
        ///
        /// 引擎的 <see cref="Building_TurretGun.DrawExtraSelectionOverlays"/> 只在射程 < 90 时画外圈，
        /// 所以射程 ≥ 90 的炮塔（爆萼花 100 格）在选中时看不到自己能打多远 —— 这里补画。
        ///
        /// 两个坑（都踩过）：
        /// 1. <c>GenDraw.DrawRadiusRing</c> 用预计算格子表，超过 <c>GenRadial.MaxRadialPatternRadius</c>
        ///    （约 79 格）会每帧报 "not enough squares" 且什么都不画；
        /// 2. 用 <c>DrawLineBetween</c> 自己画圆时，**必须**通过 <c>layer</c> 参数抬到 MetaOverlays 高度，
        ///    否则线段贴在 y≈0，被地形/建筑盖住 —— 视觉上就是「点了没反应」。
        /// </summary>
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!Spawned || Map == null)
            {
                return;
            }
            float range = AttackVerb.EffectiveRange;
            if (range < 90f)
            {
                return; // 基类已经画了
            }
            if (range <= GenRadial.MaxRadialPatternRadius)
            {
                GenDraw.DrawRadiusRing(Position, range);
                return;
            }
            DrawRingWithLines(Position, range);
        }

        /// <summary>用线段画半径环（超出引擎预计算上限时用）。</summary>
        private static void DrawRingWithLines(IntVec3 center, float radius)
        {
            const int segments = 128;
            float layer = AltitudeLayer.MetaOverlays.AltitudeFor();
            Vector3 c = center.ToVector3Shifted();
            c.y = 0f;
            Vector3 prev = c + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                Vector3 cur = c + new Vector3(Mathf.Sin(ang) * radius, 0f, Mathf.Cos(ang) * radius);
                // 3 参重载 = 用 layer 抬高 + 内置白色线材质（与强制目标线同一套渲染）
                GenDraw.DrawLineBetween(prev, cur, layer);
                prev = cur;
            }
        }

        /// <summary>发射瞬间的炮口烟（迫击炮是「轰」一下；速射炮的逐发闪光由 Verb 负责）。</summary>
        private void SpawnLaunchSmoke()
        {
            CompProperties_ManaTurret p = TurretProps;
            if (p == null || p.launchSmokeMote == null || !Spawned || Map == null)
            {
                return;
            }
            IntVec3 cell = currentTargetInt.IsValid ? currentTargetInt.Cell : Position;
            SpawnMoteAtMuzzle(p.launchSmokeMote, p.launchSmokeSize, cell);
        }

        /// <summary>按「朝目标方向」在炮口位置放一个 Mote（供 Verb 与自身共用）。</summary>
        public void SpawnMoteAtMuzzle(ThingDef moteDef, float scale, IntVec3 targetCell)
        {
            if (moteDef == null || !Spawned || Map == null)
            {
                return;
            }
            float angle = (targetCell.ToVector3Shifted() - DrawPos).AngleFlat();
            Vector3 offset = new Vector3(
                Mathf.Sin(angle * Mathf.Deg2Rad) * TurretProps.muzzleForwardOffset,
                0f,
                Mathf.Cos(angle * Mathf.Deg2Rad) * TurretProps.muzzleForwardOffset);
            MoteMaker.MakeStaticMote(DrawPos + offset, Map, moteDef, scale);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!Prefs.DevMode || !Spawned)
            {
                yield break;
            }

            // 真实机制：在射程内召唤敌对目标 → 引擎自己索敌 → 自己预热 → 自己开火
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.TurretSummonTarget".Translate(),
                defaultDesc = "Botania.Dev.TurretSummonTargetDesc".Translate(),
                action = delegate { SummonHostileTarget(); }
            };

            // 真实机制：把附近魔力源注满（走 IManaSource.AddMana）
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.TurretFillMana".Translate(),
                defaultDesc = "Botania.Dev.TurretFillManaDesc".Translate(),
                action = delegate { FillNearbyMana(); }
            };
        }

        /// <summary>召唤一个敌对单位（昆虫优先），让炮塔按真实流程索敌开火。</summary>
        private void SummonHostileTarget()
        {
            if (!Spawned || Map == null)
            {
                return;
            }
            Faction faction = Faction.OfInsects;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Megaspider");
            if (faction == null || kind == null)
            {
                faction = Faction.OfMechanoids;
                kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Scyther");
            }
            if (faction == null || kind == null)
            {
                foreach (PawnKindDef candidate in DefDatabase<PawnKindDef>.AllDefsListForReading)
                {
                    if (candidate?.race?.race == null || !candidate.race.race.Animal)
                    {
                        continue;
                    }
                    faction = Faction.OfInsects;
                    kind = candidate;
                    break;
                }
            }
            if (faction == null || kind == null)
            {
                Messages.Message("Botania.Dev.TurretSummonFailed".Translate(),
                    new LookTargets(this), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 召唤点：射程内且避开最小射程（迫击炮最小射程 29.9 格）
            float range = AttackVerb.EffectiveRange;
            float minRange = AttackVerb.verbProps.minRange;
            float low = minRange + 2f;
            float high = Mathf.Max(low + 1f, range - 1f);
            float wanted = Mathf.Clamp(range * 0.6f, low, high);
            IntVec3 cell = Position + IntVec3.FromVector3(
                new Vector3(Mathf.Sin(Rand.Range(0f, 6.28f)), 0f, Mathf.Cos(Rand.Range(0f, 6.28f))) * wanted);
            if (!cell.InBounds(Map) || !cell.Standable(Map))
            {
                cell = CellFinderLoose.RandomCellWith(
                    c => c.Standable(Map) && c.DistanceTo(Position) >= low && c.DistanceTo(Position) <= high,
                    Map, 2000);
            }
            if (!cell.IsValid || !cell.InBounds(Map))
            {
                Messages.Message("Botania.Dev.TurretSummonFailed".Translate(),
                    new LookTargets(this), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
            if (pawn == null)
            {
                Messages.Message("Botania.Dev.TurretSummonFailed".Translate(),
                    new LookTargets(this), MessageTypeDefOf.RejectInput, false);
                return;
            }
            GenSpawn.Spawn(pawn, cell, Map);
            Messages.Message("Botania.Dev.TurretSummonDone".Translate(pawn.LabelShortCap),
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>把附近最近魔力源注满（真实 AddMana 路径）。</summary>
        private void FillNearbyMana()
        {
            CompManaTurret comp = ManaComp;
            IManaSource source = comp?.FindNearbySource();
            if (source == null)
            {
                Messages.Message("Botania.Dev.TurretFillManaNone".Translate(),
                    new LookTargets(this), MessageTypeDefOf.RejectInput, false);
                return;
            }
            float free = source.FreeSpace;
            if (free <= 0.01f)
            {
                Messages.Message("Botania.Dev.TurretFillManaFull".Translate(),
                    new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            source.AddMana(free);
            Messages.Message("Botania.Dev.TurretFillManaDone".Translate(free.ToString("0")),
                new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
