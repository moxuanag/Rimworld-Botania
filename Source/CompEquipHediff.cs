using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉钢镐等「装备期间给穿戴者挂 Hediff」物品的 XML 入口。
    ///
    /// 字段与逻辑全部下沉到 <see cref="CompProperties_EquipHediffBase"/> /
    /// <see cref="CompEquipHediffBase"/>；保留这个旧类名只为不动现有 Defs
    /// （XML 里写的是 <c>Botania.CompProperties_EquipHediff</c>，不能改名）。
    /// 新装备可以直接用 <c>CompProperties_EquipHediffBase</c>，连 C# 都不用写。
    ///
    /// 为什么不用 <c>&lt;equippedStatFactors&gt;</c>：引擎的 <c>Verse.ThingDef</c> 没有这个字段
    /// （只有 equippedStatOffsets / equippedAngleOffset / equippedDistanceOffset，用 DLL 元数据确认过）。
    /// 写了不存在的字段不是「忽略」，而是**整条 ThingDef 解析失败**，物品直接不会出现在游戏里
    /// ——2026-10-24 就是这么把碎岩镐写没的。
    ///
    /// 想要乘法倍率只能借 Hediff：<c>HediffDef.stages[].statFactors</c> 支持真倍率
    /// （原版 <c>MetalhorrorSpeedBoost</c> 就是 <c>&lt;MoveSpeed&gt;1.2&lt;/MoveSpeed&gt;</c>）。
    /// </summary>
    public class CompProperties_EquipHediff : CompProperties_EquipHediffBase
    {
        public CompProperties_EquipHediff()
        {
            compClass = typeof(CompEquipHediff);

            // 显式钉住迁移前的行为：原实现 CheckInterval = 250，
            // 且旧组件没有任何存档字段（所以 legacy*Key 保持为空）。
            // 以后改基类默认值不会影响这件老装备。
            applyOnlyWhenEquipped = true;
            refreshIntervalTicks = 250;
            saveKeyPrefix = "botaniaEquipHediff";
        }
    }

    /// <summary>
    /// 旧实现的类名保留：XML 通过 <see cref="CompProperties_EquipHediff"/>.compClass 指向这里，
    /// 实际行为见 <see cref="CompEquipHediffBase"/>（装备时挂 / 卸下时摘 / 读档补挂 / 定期巡检）。
    /// </summary>
    public class CompEquipHediff : CompEquipHediffBase
    {
    }
}
