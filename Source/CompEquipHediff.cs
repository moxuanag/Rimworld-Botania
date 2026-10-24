using RimWorld;
using Verse;

namespace Botania
{
    public class CompProperties_EquipHediff : CompProperties
    {
        /// <summary>装备期间挂在穿戴者身上的状态。</summary>
        public HediffDef hediffDef;

        public CompProperties_EquipHediff()
        {
            compClass = typeof(CompEquipHediff);
        }
    }

    /// <summary>
    /// 装备态「属性倍率」的通用做法：装备时给穿戴者挂一个 Hediff，卸下时摘掉。
    ///
    /// 为什么不用 `<equippedStatFactors>`：**引擎的 `Verse.ThingDef` 没有这个字段**
    /// （只有 `equippedStatOffsets` / `equippedAngleOffset` / `equippedDistanceOffset`，
    /// 用 DLL 元数据确认过）。写了不存在的字段不是「忽略」，而是**整条 ThingDef 解析失败**，
    /// 物品直接不会出现在游戏里——2026-10-24 就是这么把碎岩镐写没的。
    ///
    /// 想要乘法倍率只能借 Hediff：`HediffDef.stages[].statFactors` 支持真倍率
    /// （原版 `MetalhorrorSpeedBoost` 就是 `<MoveSpeed>1.2</MoveSpeed>`）。
    ///
    /// 三道保险：
    /// 1. `Notify_Equipped` / `Notify_Unequipped` 正常路径；
    /// 2. `PostSpawnSetup`（含读档后 respawningAfterLoad）补一次，防止读档丢状态；
    /// 3. `CompTick` 每 250 tick 兜底校正（物品必须写 `tickerType Normal` 才会 tick）。
    /// </summary>
    public class CompEquipHediff : ThingComp
    {
        private const int CheckInterval = 250;

        private CompProperties_EquipHediff Props => (CompProperties_EquipHediff)props;

        private Pawn Wearer => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            Apply(pawn);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Remove(pawn);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                return;
            }
            Apply(Wearer);
        }

        public override void CompTick()
        {
            if (!parent.IsHashIntervalTick(CheckInterval))
            {
                return;
            }
            Pawn wearer = Wearer;
            if (wearer != null)
            {
                Apply(wearer);
            }
        }

        private void Apply(Pawn pawn)
        {
            HediffDef def = Props.hediffDef;
            if (pawn?.health?.hediffSet == null || def == null || pawn.Dead)
            {
                return;
            }
            if (pawn.health.hediffSet.GetFirstHediffOfDef(def) == null)
            {
                pawn.health.AddHediff(def, null, null);
            }
        }

        private void Remove(Pawn pawn)
        {
            HediffDef def = Props.hediffDef;
            if (pawn?.health?.hediffSet == null || def == null)
            {
                return;
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
