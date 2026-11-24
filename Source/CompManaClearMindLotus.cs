using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 明心莲：用魔力替范围内的小人挡住「心灵干扰」。
    /// 只对冲心灵干扰（PsychicDrone）那一条念头，不做整体心情加成；
    /// 断魔就失效，所以它是个吃钱的功能花，不是白给的心情挂件。
    ///
    /// 框架迁移（0.8.4 S2）：props 继承 <see cref="CompProperties_ManaDevice"/>，comp 继承
    /// <see cref="CompManaDevice"/>；节流、找魔力源、存档键前缀交给基类 /
    /// <see cref="ManaDeviceCore"/>。本文件只保留明心莲独有的账：
    /// 「谁真的被干扰影响到 → 按人数收费 → 付成后压制到 paidUntilTick」，
    /// 以及多朵莲重叠时的归属与交接。
    /// </summary>
    public class CompProperties_ManaClearMindLotus : CompProperties_ManaDevice
    {
        /// <summary>庇护半径（格）。</summary>
        public float radius = 8f;

        /// <summary>每个被庇护者每秒消耗多少魔力。</summary>
        public float manaPerPawnSecond = 20f;

        /// <summary>付一次钱能撑多少 tick（180 = 3 秒，避免每帧反复取魔）。</summary>
        public int paidDurationTicks = 180;

        /// <summary>付过钱的截止 tick 的存档键（带前缀，避免同物件其它组件撞键）。</summary>
        public string PaidUntilSaveKey => saveKeyPrefix + "PaidUntilTick";

        /// <summary>旧版存档键（老组件没有前缀，直接平铺在 Thing 节点下）；只读不写。</summary>
        public string legacyPaidUntilKey = "paidUntilTick";

        public CompProperties_ManaClearMindLotus()
        {
            compClass = typeof(CompManaClearMindLotus);

            // 基类配置：取魔半径 6 格（原字段 poolRadius）、每 60 tick 结算一次。
            // 成功/失败同间隔，保持旧版「固定每 60 tick 看一次」的节奏。
            // manaPerWork 留默认值：明心莲按受影响人数计费（count × manaPerPawnSecond），
            // 不走基类的固定额付款路径（整套周期在 SettleOnce 里覆写了）。
            searchRadius = 6f;
            workIntervalTicks = 60;
            retryIntervalTicks = 60;
            manaTolerance = 0.01f;

            // 存档键前缀；老档里的 paidUntilTick 由 legacyPaidUntilKey 读回。
            saveKeyPrefix = "botaniaClearMindLotus";
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerPawnSecond <= 0f)
            {
                yield return "manaPerPawnSecond must be greater than 0.";
            }
            if (paidDurationTicks <= 0)
            {
                yield return "paidDurationTicks must be greater than 0.";
            }
        }
    }

    public class CompManaClearMindLotus : CompManaDevice
    {
        /// <summary>场上所有明心莲，心灵干扰的补丁直接查这张表。</summary>
        public static readonly List<CompManaClearMindLotus> All = new List<CompManaClearMindLotus>();

        /// <summary>每个小人当前由哪朵莲计费。交接时清旧记录，避免旧花继续收费。</summary>
        private static readonly Dictionary<Pawn, CompManaClearMindLotus> owners =
            new Dictionary<Pawn, CompManaClearMindLotus>();

        /// <summary>被心灵干扰实际影响到的小人 → 最后一次被观测到的 tick。
        /// 只有这些人计费：性别不符、心灵迟钝（0 敏感）的人不花钱。</summary>
        private readonly Dictionary<Pawn, int> affected = new Dictionary<Pawn, int>();

        /// <summary>超过这么久没再观测到，就认为已经不影响了。</summary>
        private const int AffectedMemoryTicks = 600;

        private static readonly Dictionary<Map, bool> droneCache = new Dictionary<Map, bool>();

        private static int droneCacheTick = -999;

        /// <summary>付过钱的截止 tick；在这之前范围内的心里干扰被压住。</summary>
        private int paidUntilTick;

        public CompProperties_ManaClearMindLotus Props => (CompProperties_ManaClearMindLotus)props;

        /// <summary>当前是否在提供庇护（已付费且未过期）。</summary>
        public bool IsPaying => parent != null && parent.Spawned && Find.TickManager.TicksGame < paidUntilTick;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            All.Remove(this);
        }

        /// <summary>
        /// 覆写整套周期：明心莲是「按受影响人数计费、整次付款、付不成退款并交接」，
        /// 与基类的「固定额、凑够才扣」语义不同，因此自己控制付款，只复用基类的
        /// 计时调度（<see cref="ManaDeviceCore.ScheduleNext"/>）、找源
        /// （<see cref="ManaDeviceCore.FindSource"/>）与存档。
        /// </summary>
        protected override bool SettleOnce()
        {
            bool paid = SettleCycle();
            // 成功/失败间隔都是 60 tick（构造里两个都设成 60），与旧版固定节奏一致。
            Core.ScheduleNext(paid);
            return paid;
        }

        // 基类要求实现的两个抽象钩子：整套周期已被 SettleOnce 覆写，这里只做保守默认。
        protected override bool WantsWork()
        {
            return false;
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>一次结算：复核受影响的人、按人数收费、成功则续 paidUntilTick。返回本次是否付成。</summary>
        private bool SettleCycle()
        {
            // 只要记录到「有人确实被干扰影响到」就结算。
            // 不再去查地图条件——那个查询在实机上没拿到东西，白跑一趟。

            int now = Find.TickManager.TicksGame;
            List<Pawn> valid = new List<Pawn>();
            List<Pawn> stale = null;
            foreach (KeyValuePair<Pawn, int> pair in affected)
            {
                Pawn pawn = pair.Key;
                // 结算时复查距离：小人离开范围后不应该继续被收费
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != parent.Map
                    || now - pair.Value > AffectedMemoryTicks
                    || pawn.Position.DistanceTo(parent.Position) > Props.radius)
                {
                    if (stale == null)
                    {
                        stale = new List<Pawn>();
                    }
                    stale.Add(pawn);
                    continue;
                }
                valid.Add(pawn);
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    affected.Remove(stale[i]);
                }
            }
            if (valid.Count <= 0)
            {
                return false;
            }

            float cost = valid.Count * Props.manaPerPawnSecond;
            // 基类的 FindSource 与旧版 ManaSourceUtil.FindNearest 一样会排除空源与不够付的源
            IManaSource source = Core.FindSource(cost);
            if (source == null)
            {
                // 自己付不起时旧版直接返回：不扣钱、也不动归属（下一 tick 再算）
                return false;
            }
            float drained = source.DrainMana(cost);
            if (drained + Props.ManaTolerance < cost)
            {
                // 不够就整次不生效，避免「半价挡半份」的模糊状态；
                // 把钱退回去，并把人让给能供能的其它花
                source.AddMana(drained);
                for (int i = 0; i < valid.Count; i++)
                {
                    affected.Remove(valid[i]);
                    HandOff(valid[i]);
                }
                return false;
            }
            paidUntilTick = Find.TickManager.TicksGame + Props.paidDurationTicks;
            return true;
        }

        /// <summary>心灵干扰正在影响这个小人：记到覆盖他的每朵花头上，作为计费依据。</summary>
        public static void NotifyAffected(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;

            // 多朵莲重叠覆盖同一个人时，只让「最近的那一朵」记账，
            // 否则同一个人会被几朵花各收一份钱。
            CompManaClearMindLotus owner = null;
            float best = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                float distance = lotus.parent.Position.DistanceTo(pawn.Position);
                if (distance > lotus.Props.radius)
                {
                    continue;
                }
                if (distance < best)
                {
                    best = distance;
                    owner = lotus;
                }
            }
            if (owner == null)
            {
                return;
            }
            CompManaClearMindLotus previous;
            if (owners.TryGetValue(pawn, out previous) && previous != null && previous != owner)
            {
                // 归属变了：旧花必须放弃这一笔，否则重叠区来回走会双花同时收钱
                previous.affected.Remove(pawn);
            }
            owners[pawn] = owner;
            owner.affected[pawn] = now;
        }

        /// <summary>这朵花附近有没有至少能付得起这么多魔力的源。</summary>
        private bool CanAfford(float amount)
        {
            if (parent == null || parent.Map == null)
            {
                return false;
            }
            return Core.FindSource(amount) != null;
        }

        /// <summary>把这名小人让给另一朵「确实付得起」的花（更近优先）。</summary>
        private void HandOff(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                return;
            }
            CompManaClearMindLotus best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus == this || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                float distance = lotus.parent.Position.DistanceTo(pawn.Position);
                if (distance > lotus.Props.radius || distance >= bestDistance)
                {
                    continue;
                }
                if (lotus.CanAfford(1f) == false)
                {
                    continue; // 付不起的花不接
                }
                best = lotus;
                bestDistance = distance;
            }
            owners.Remove(pawn);
            if (best != null)
            {
                owners[pawn] = best;
                best.affected[pawn] = Find.TickManager.TicksGame;
            }
        }

        /// <summary>这张图上有没有已经摆出来的心灵干扰器（建筑）。结果缓存两秒，避免每帧扫全图。</summary>
        public static bool HasDroneBuilding(Map map)
        {
            if (map == null)
            {
                return false;
            }
            int now = Find.TickManager.TicksGame;
            if (now - droneCacheTick > 120)
            {
                droneCache.Clear();
                droneCacheTick = now;
            }
            bool cached;
            if (droneCache.TryGetValue(map, out cached))
            {
                return cached;
            }
            bool found = false;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (thing != null && thing.def != null && thing.def.defName == "PsychicDronerShipPart" && thing.Spawned)
                {
                    found = true;
                    break;
                }
            }
            droneCache[map] = found;
            return found;
        }

        /// <summary>这朵花是否正在为某个小人「部分」挡干扰（只看圈内、且只对无实体的事件干扰生效）。</summary>
        public static bool PartialShield(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            if (HasDroneBuilding(pawn.Map))
            {
                // 场上有干扰器建筑：那种是全额挡，不走「减半」这条路
                return false;
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                if (!lotus.IsPaying)
                {
                    continue;
                }
                if (lotus.parent.Position.DistanceTo(pawn.Position) <= lotus.Props.radius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>这个小人是否被某朵正在付费的明心莲罩着。</summary>
        public static bool IsShielded(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || All.Count == 0)
            {
                return false;
            }
            if (!HasDroneBuilding(pawn.Map))
            {
                // 场上没有干扰器建筑 → 不是全免档，走 PartialShield 减半
                return false;
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                CompManaClearMindLotus lotus = All[i];
                if (lotus == null || lotus.parent == null || !lotus.parent.Spawned || lotus.parent.Map != pawn.Map)
                {
                    continue;
                }
                if (!lotus.IsPaying)
                {
                    continue;
                }
                if (lotus.parent.Position.DistanceTo(pawn.Position) <= lotus.Props.radius)
                {
                    return true;
                }
            }
            return false;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
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
                defaultLabel = "Botania.Dev.LotusShieldNearby".Translate(),
                defaultDesc = "Botania.Dev.LotusShieldNearbyDesc".Translate(),
                action = DebugShieldNearbyPawns
            };
        }

        /// <summary>
        /// 开发者工具：让附近小人立刻进入「被心灵干扰影响到」的状态。
        ///
        /// 这里走的是**真实目标收集路径**，不是直接改 paidUntilTick：
        ///   · 范围内每个类人生物都交给 <see cref="NotifyAffected"/>——和
        ///     ThoughtWorker_PsychicDrone 补丁里完全同一个入口，由它做范围判定、
        ///     「最近的花」归属以及重叠区的交接；
        ///   · 然后对实际接管了这些小人的花（可能不止这朵）各跑一次真实结算
        ///     <see cref="SettleOnce"/>：按受影响人数找源、扣魔力，付成才把
        ///     paidUntilTick 往后推；付不起就整次退款并把小人交接给别的花。
        ///
        /// 唯一的「作弊」是跳过海选：把半径内的类人生物直接当成「已经被干扰影响到」。
        /// 心灵干扰念头本身仍由原版事件 / 干扰器建筑提供，本按钮只负责莲的记账与庇护。
        /// </summary>
        private void DebugShieldNearbyPawns()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            List<Pawn> targets = new List<Pawn>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
            {
                Pawn pawn = thing as Pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned || !pawn.RaceProps.Humanlike)
                {
                    continue;
                }
                targets.Add(pawn);
            }
            if (targets.Count <= 0)
            {
                Messages.Message("Botania.Dev.LotusShieldNearbyNone".Translate(),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            HashSet<CompManaClearMindLotus> ownersToSettle = new HashSet<CompManaClearMindLotus>();
            for (int i = 0; i < targets.Count; i++)
            {
                Pawn pawn = targets[i];
                NotifyAffected(pawn);
                CompManaClearMindLotus owner;
                if (owners.TryGetValue(pawn, out owner) && owner != null)
                {
                    ownersToSettle.Add(owner);
                }
            }
            if (ownersToSettle.Count == 0)
            {
                ownersToSettle.Add(this);
            }

            bool paid = false;
            foreach (CompManaClearMindLotus owner in ownersToSettle)
            {
                // 每朵花结自己的账：真实 SettleOnce → SettleCycle → 按人数收费、付成才续 paidUntilTick
                paid |= owner.SettleOnce();
            }
            if (paid)
            {
                Messages.Message("Botania.Dev.LotusShieldNearbyDone".Translate(targets.Count),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("Botania.Dev.LotusShieldNearbyNoMana".Translate(targets.Count),
                    new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            if (!IsPaying)
            {
                return "Botania.ClearMindLotusIdle".Translate();
            }
            if (HasDroneBuilding(parent.Map))
            {
                return "Botania.ClearMindLotusActive".Translate(Props.manaPerPawnSecond.ToString("0"));
            }
            return "Botania.ClearMindLotusWeaken".Translate(Props.manaPerPawnSecond.ToString("0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            // 明心莲真正的持久状态只有「付过钱的截止 tick」。
            // 新键带前缀（同物件上别的组件不会撞），旧档的 paidUntilTick 只读不写。
            int paidUntil = paidUntilTick;
            Scribe_Values.Look(ref paidUntil, Props.PaidUntilSaveKey, 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !Props.legacyPaidUntilKey.NullOrEmpty())
            {
                Scribe_Values.Look(ref paidUntil, Props.legacyPaidUntilKey, paidUntil);
            }
            paidUntilTick = paidUntil;
        }
    }
}
