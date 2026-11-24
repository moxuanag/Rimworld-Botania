using System.Collections.Generic;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「穿戴时给佩戴者挂 Hediff / 周期性效果」类装备的公共配置。
    ///
    /// 与 <see cref="CompProperties_ManaDevice"/> 同一套约定：
    /// - 具体装备的 CompProperties 继承本类（XML 里的 Class 名不用改，继承链上的字段照常读）；
    /// - 存档键必须带 <see cref="saveKeyPrefix"/>（本引擎把组件字段平铺在 Thing 节点下，
    ///   同一件东西上多个组件若用同名键会互相覆盖）；
    /// - 迁移时把老键填进 legacy* 字段，基类**只读不写**，老存档里的状态仍能读回。
    ///
    /// 直接用法（不需要写 C#）：XML 组件写
    /// <c>&lt;li Class="Botania.CompProperties_EquipHediffBase"&gt;&lt;hediffDef&gt;...&lt;/hediffDef&gt;&lt;/li&gt;</c>；
    /// 需要周期效果时再写一个 30 行以内的子类覆写 <c>OnRefreshed</c>。
    /// </summary>
    public class CompProperties_EquipHediffBase : CompProperties
    {
        /// <summary>佩戴期间挂在穿戴者身上的 Hediff。为 null 时只做周期回调（见 OnRefreshed），不挂状态。</summary>
        public HediffDef hediffDef;

        /// <summary>
        /// true（默认）：只在「正被佩戴」期间保持 Hediff，卸下即摘除。
        /// false：卸下时保留 Hediff（一次性 / 永久加成类装备），但佩戴期间照常刷新。
        /// </summary>
        public bool applyOnlyWhenEquipped = true;

        /// <summary>佩戴期间的巡检 / 刷新间隔（tick）。默认 250，与原 CompEquipHediff 的 CheckInterval 一致。</summary>
        public int refreshIntervalTicks = 250;

        /// <summary>
        /// 存档键前缀。本引擎的存档把组件字段平铺在 Thing 节点下（没有按类分组），
        /// 所以键必须够独特，避免同一件东西上多个组件撞键。
        /// </summary>
        public string saveKeyPrefix = "botaniaEquipHediff";

        /// <summary>旧版存档键（只读兼容）：老组件用过的「是否已挂 Hediff」字段名。为空则不读。</summary>
        public string legacyHasHediffKey;

        /// <summary>旧版存档键（只读兼容）：老组件用过的「下次刷新时刻」字段名。为空则不读。</summary>
        public string legacyNextRefreshKey;

        public CompProperties_EquipHediffBase()
        {
            compClass = typeof(CompEquipHediffBase);
        }

        public string HasHediffSaveKey => saveKeyPrefix + "HasHediff";
        public string NextRefreshSaveKey => saveKeyPrefix + "NextRefreshTick";

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (refreshIntervalTicks <= 0)
            {
                yield return "refreshIntervalTicks must be greater than 0.";
            }
            if (saveKeyPrefix.NullOrEmpty())
            {
                yield return "saveKeyPrefix must not be empty (save fields are flat per Thing).";
            }
        }
    }

    /// <summary>
    /// 「穿戴时给佩戴者挂 Hediff / 周期性效果」的通用基类。
    /// 负责：佩戴检测 → 加 / 摘 Hediff → 定期刷新 → 存档。
    ///
    /// 生命周期（与原 CompEquipHediff 逐点一致）：
    /// 1. <see cref="Notify_Equipped"/>：装备上身，立刻补挂 Hediff（原实现同一路径）；
    /// 2. <see cref="Notify_Unequipped"/>：卸下即摘；
    /// 3. <see cref="PostSpawnSetup"/>（仅读档后 respawningAfterLoad）：补挂一次，防止读档丢状态；
    /// 4. <see cref="CompTick"/>：到点巡检，子类可覆写 <see cref="OnRefreshed"/> 做周期效果。
    ///
    /// **节流只有一处**：<c>Find.TickManager.TicksGame &gt;= nextRefreshTick</c>。
    /// 不要再叠 <c>IsHashIntervalTick</c>——两层节流相位不同会让刷新永远等不到
    /// （0.8.4 精灵灯事故，AGENTS.md §5）。也不要在 PostSpawnSetup 里结算别的组件。
    ///
    /// 物品 Def 必须写 <c>&lt;tickerType&gt;Normal&lt;/tickerType&gt;</c>，否则 CompTick 不跑；
    /// 佩戴期间真正可靠的是 Notify_Equipped / Notify_Unequipped / PostSpawnSetup 三条路径，
    /// tick 只是兜底巡检与周期效果入口（与原实现同一假设）。
    /// </summary>
    public class CompEquipHediffBase : ThingComp
    {
        private bool hasHediff;
        private int nextRefreshTick;

        protected CompProperties_EquipHediffBase EquipProps => (CompProperties_EquipHediffBase)props;

        /// <summary>
        /// 要挂的 Hediff。默认读配置；子类可按品质 / 材质等动态返回。
        /// 返回 null 表示不挂状态，只走周期回调。
        /// </summary>
        protected virtual HediffDef HediffToApply => EquipProps.hediffDef;

        /// <summary>当前佩戴者：武器走 Pawn_EquipmentTracker，护甲走 Pawn_ApparelTracker；没在身上返回 null。</summary>
        public Pawn Wearer => EquipUtil.GetHolder(parent);

        /// <summary>最近一次装备 / 读档 / 巡检后，佩戴者身上是否存在目标 Hediff（存档里也记这一位）。</summary>
        public bool HediffApplied => hasHediff;

        /// <summary>装备上身的正常路径：立刻补挂（幂等）。</summary>
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn == null)
            {
                return;
            }
            EnsureHediff(pawn);
            ScheduleNext();
        }

        /// <summary>卸下路径：摘掉 Hediff；<c>applyOnlyWhenEquipped = false</c> 时保留。</summary>
        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (!EquipProps.applyOnlyWhenEquipped)
            {
                return;
            }
            RemoveHediff(pawn);
        }

        /// <summary>
        /// 读档后补挂一次（原实现同样只在 respawningAfterLoad 时做）。
        /// 刻意不在这里做周期结算：此时其它组件可能还没初始化完（AGENTS.md §5）。
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                return;
            }
            Pawn wearer = Wearer;
            if (wearer != null)
            {
                EnsureHediff(wearer);
            }
        }

        /// <summary>到点巡检：确保佩戴者身上有 Hediff，然后交给子类的周期钩子。节流只在这一处。</summary>
        public override void CompTick()
        {
            base.CompTick();
            int now = Find.TickManager.TicksGame;
            if (now < nextRefreshTick)
            {
                return;
            }
            ScheduleNextFrom(now);
            Pawn wearer = Wearer;
            if (wearer == null)
            {
                return;
            }
            EnsureHediff(wearer);
            OnRefreshed(wearer);
        }

        /// <summary>
        /// 周期效果钩子：每 <c>refreshIntervalTicks</c> 调用一次，调用前已确保 Hediff 存在。
        /// 默认什么都不做；需要「佩戴期间每 N tick 回血 / 扣魔力 / 发状态」的装备覆写它。
        /// </summary>
        protected virtual void OnRefreshed(Pawn wearer)
        {
        }

        /// <summary>确保佩戴者身上有目标 Hediff（已存在则不重复添加）。</summary>
        protected void EnsureHediff(Pawn pawn)
        {
            HediffDef def = HediffToApply;
            if (pawn?.health?.hediffSet == null || def == null || pawn.Dead)
            {
                return;
            }
            if (pawn.health.hediffSet.GetFirstHediffOfDef(def) == null)
            {
                pawn.health.AddHediff(def, null, null);
            }
            hasHediff = true;
        }

        /// <summary>摘掉佩戴者身上的目标 Hediff（原实现按 Def 摘第一个，不区分是谁挂的）。</summary>
        protected void RemoveHediff(Pawn pawn)
        {
            HediffDef def = HediffToApply;
            if (pawn?.health?.hediffSet == null || def == null)
            {
                return;
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
            hasHediff = false;
        }

        private void ScheduleNext()
        {
            ScheduleNextFrom(Find.TickManager.TicksGame);
        }

        private void ScheduleNextFrom(int now)
        {
            int interval = EquipProps.refreshIntervalTicks;
            if (interval <= 0)
            {
                // 配置错误由 ConfigErrors 报；这里兜底，避免每 tick 都巡检。
                interval = 250;
            }
            nextRefreshTick = now + interval;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            bool applied = hasHediff;
            int next = nextRefreshTick;
            Scribe_Values.Look(ref applied, EquipProps.HasHediffSaveKey, false);
            Scribe_Values.Look(ref next, EquipProps.NextRefreshSaveKey, 0);
            // 旧版存档兼容：老键只读不写（回写会和新键重复占坑）
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (!EquipProps.legacyHasHediffKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref applied, EquipProps.legacyHasHediffKey, applied);
                }
                if (!EquipProps.legacyNextRefreshKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref next, EquipProps.legacyNextRefreshKey, next);
                }
            }
            hasHediff = applied;
            nextRefreshTick = next;
        }
    }
}
