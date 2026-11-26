using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉刃的「剑气付款件」：让 MapComponent_WandState 认出「手里拿的是泰拉刃」从而授予/收回
    /// 剑气技能，同时给剑气提供**费用与取魔口**。
    ///
    /// 为什么不复用 CompManaWeapon：刃内没有魔力条（剑气不烧刀里的储能），
    /// 所以刃本身不该出现「充能」按钮；但剑气要付钱，付款逻辑仍然走物品付款框架
    /// <see cref="ManaItemCore"/>（找最近的魔力池 → 凑够才扣 → 提示文案），
    /// 和森林法杖的两个法术同一套语义，不重写一遍遍历。
    ///
    /// XML（`Defs/ThingDefs_Items/Items_Botania_Terrasteel.xml`）只写一个字段：
    /// <c>&lt;waveManaCost&gt;400&lt;/waveManaCost&gt;</c>；找源半径沿用基类的
    /// 取魔改为「持有者所在格的魔力网络」，不再用 <c>searchRadius</c> 的最近池口径。
    /// </summary>
    public class CompProperties_TerraBlade : CompProperties_ManaItem
    {
        /// <summary>一道剑气要抽多少魔力（XML 里写 400）。</summary>
        public float waveManaCost = 400f;

        public CompProperties_TerraBlade()
        {
            compClass = typeof(CompTerraBlade);
            // 本引擎存档把组件字段平铺在 Thing 节点下，键必须够独特
            saveKeyPrefix = "botaniaTerraBlade";
            // 费用由 waveManaCost 现算，不读基类的固定费用 manaPerUse
            manaPerUse = 0f;
            // 冷却由 AbilityDef 的 cooldownTicksRange(480) 管，这里保持 0，避免两层节流叠加
            cooldownTicks = 0;
            // 找魔力源半径：基类默认就是 8，与剑气需求一致，XML 不必再写一遍
            searchRadius = 8f;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (waveManaCost < 0f)
            {
                yield return "waveManaCost must not be negative.";
            }
        }
    }

    public class CompTerraBlade : ThingComp
    {
        private ManaItemCore core;

        // 每 tick 只做一次辐射扫描：CanCast 在 gizmo 每帧绘制时都会被问一次，
        // 而 8 格 ≈ 289 格。缓存只在同一 tick 内有效，魔力变动最迟 1 tick 后反映到 UI。
        private int manaCheckTick = -1;
        private bool manaCheckCanPay;
        private float manaCheckAvailable;

        public CompProperties_TerraBlade Props => (CompProperties_TerraBlade)props;

        private ManaItemCore Core => core ??= new ManaItemCore(this, Props);

        /// <summary>一道剑气的魔力价码（400）。</summary>
        public float WaveManaCost => Props.waveManaCost;

        /// <summary>
        /// 剑气取魔用的魔力源：**持有者所在格的魔力网络**（与其它耗魔设备同一口径）。
        /// 中继塔把网络延伸出去时，剑气也能用那份共享魔力；
        /// 不再走 searchRadius 的「最近的池」老口径。
        /// </summary>
        private IManaSource WaveSource()
        {
            Map map = EquipUtil.GetMap(parent);
            return map?.GetComponent<MapComponent_ManaNetwork>()?.SourceAt(EquipUtil.GetPosition(parent));
        }

        /// <summary>
        /// 现查（不走缓存）：现在付不付得起一道剑气，<paramref name="available"/> 回填「最近一个够付的源」
        /// 的余量；一个够付的都没有时，回填最近一个非空源的余量（给提示里的「可用」用，可能为 0）。
        /// 只查询、不扣款——扣款走 <see cref="TryPayWave"/>。
        /// 给「付款失败那一条提示」用：那条数字必须现算，不能拿 UI 缓存糊弄玩家。
        /// </summary>
        public bool ProbeWaveMana(out float available)
        {
            float cost = WaveManaCost;
            if (cost <= 0f)
            {
                available = 0f;
                return true;
            }
            // 取所在格的魔力网络源（不是「最近的池」）：中继塔延伸到的池子也算数。
            IManaSource source = WaveSource();
            available = source?.ManaAmount ?? 0f;
            return source != null && available + Props.manaTolerance >= cost;
        }

        /// <summary>
        /// UI 版 <see cref="ProbeWaveMana"/>：CanCast 在 gizmo 每帧绘制时都会被问到一次，
        /// 而 8 格 ≈ 289 格，所以同一 tick 内复用上一次结果；魔力变动最迟 1 tick 后反映到按钮上。
        /// </summary>
        public bool CanPayWave(out float available)
        {
            int now = Find.TickManager?.TicksGame ?? -1;
            if (now < 0 || manaCheckTick != now)
            {
                manaCheckCanPay = ProbeWaveMana(out manaCheckAvailable);
                manaCheckTick = now;
            }
            available = manaCheckAvailable;
            return manaCheckCanPay;
        }

        /// <summary>
        /// 整笔扣掉一道剑气的魔力。**凑够才扣**：不够时一点不抽（不会把池子抽干却出不了剑）。
        /// 付得起返回 true，调用方这时才允许真出剑。
        /// </summary>
        public bool TryPayWave()
        {
            float cost = WaveManaCost;
            if (cost <= 0f)
            {
                return true;
            }
            IManaSource source = WaveSource();
            if (source == null || source.ManaAmount + Props.manaTolerance < cost)
            {
                return false;
            }
            float drained = source.DrainMana(cost);
            if (drained + Props.manaTolerance < cost)
            {
                // 抽到的比预期少：退回，不做半支付
                source.AddMana(drained);
                return false;
            }
            return true;
        }

        // 刻意不写 PostExposeData / Core.ExposeData：本组件没有任何需要持久化的自有状态
        // ——冷却由 AbilityDef 管、HasMana 每次都按当前魔力现算。写了只会给存档里每个泰拉刃
        // 多两个永远不参与判定的平铺键（botaniaTerraBladeHasMana / …NextUseTick）。
    }
}
