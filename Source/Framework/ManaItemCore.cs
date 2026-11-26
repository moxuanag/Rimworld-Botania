using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「持有 / 挥动时才付款」的物品公共配置。
    ///
    /// 框架思路与 <see cref="CompProperties_ManaDevice"/> 对称：
    ///   · 设备（建筑）有 tick 周期，按固定间隔结算，所以节流键是「下一次结算 tick」；
    ///   · 物品（武器 / 工具 / 装备）没有自己的生活周期，只在**玩家真的用了一下**时付款
    ///     —— 开一枪、放一个法术、挥一刀；所以节流键是「下一次可用 tick」。
    /// 迁移前每个物品各自抄一遍：找最近的魔力池/中继塔、凑够才扣、冷却、存档键、提示文案，这里统一。
    ///
    /// 为什么装备要单独一套：装备在身上时 <c>Thing.Map</c> / <c>Thing.Position</c> 都是空的，
    /// 找魔力源必须顺着 ParentHolder 找到持有者的位置（见 <see cref="EquipUtil"/>），
    /// 建筑框架直接用 parent.Position 会永远找不到源。
    ///
    /// 用法（组合，不强制物品 Comp 的父类）：
    /// <code>
    /// private ManaItemCore core;
    /// private ManaItemCore Core =&gt; core ??= new ManaItemCore(this, Props);
    /// // 用之前： if (!Core.Ready) return false;
    /// // 付款：   if (!Core.TryPay(cost)) { 提示; return false; }
    /// // 成功后： Core.NotifyUsed();          // 进入冷却（cooldownTicks = 0 时等于不节流）
    /// // 存档：   public override void PostExposeData() { base.PostExposeData(); Core.ExposeData(); }
    /// </code>
    ///
    /// XML 字段（新名字）：
    ///   searchRadius     找魔力源（魔力池 / 中继塔）的半径，格。默认 8。
    ///   manaPerUse       每次使用的固定费用；0 表示「由物品按实际用量自己算」。
    ///   cooldownTicks    两次使用之间的冷却（tick）；0 = 不节流。
    ///                    **不要**在已经有原版/技能冷却的物品上再设这个值（双重节流，见 AGENTS.md §5）。
    ///   manaTolerance    浮点容差，默认 0.01。
    ///   inspectWorkingKey / inspectNoManaKey
    ///                    可选提示文案 Keyed 键；留空则 <see cref="ManaItemCore.InspectString"/> 返回 null
    ///                    （不占用 Keyed，也不改变物品原有 inspect 文本）。
    ///   saveKeyPrefix    存档键前缀。本引擎存档把组件字段**平铺在 Thing 节点下**，
    ///                    同一件东西上多个组件的键会互相覆盖，所以前缀必须唯一。
    ///   legacyHasManaKey / legacyNextUseKey
    ///                    迁移前的旧存档键：只读不写，老档能续上旧状态。
    /// </summary>
    public class CompProperties_ManaItem : CompProperties
    {
        /// <summary>
        /// 旧口径「就近找魔力池」的半径（格）。消费侧已统一改走**物品所在格的魔力网络**
        /// （见 <see cref="ManaItemCore.FindSource"/>），本字段**不再参与找源**；
        /// 保留只为 XML / 存档兼容（法杖 6、泰拉刃 8），删掉会让现有 XML 报「字段不存在」。
        /// </summary>
        public float searchRadius = 8f;

        /// <summary>
        /// 每次使用的固定费用。0 表示本物品不用固定费用，由子类按实际用量调用
        /// <see cref="ManaItemCore.TryPay(float)"/>（例如法杖按「株」「个」计费）。
        /// </summary>
        public float manaPerUse = 20f;

        /// <summary>
        /// 两次使用之间的冷却（tick）。0 = 不节流（默认；很多物品的冷却由 verb / AbilityDef 管）。
        /// 注意：它与 <see cref="ManaItemCore.Ready"/> 是**唯一**的节流点，
        /// 子类不得再叠一个自己的 tick 计数（AGENTS.md §5「节流只放一处」）。
        /// </summary>
        public int cooldownTicks = 0;

        /// <summary>浮点容差：低于此值视为「刚好够」，避免 19.999 判成付不起。</summary>
        public float manaTolerance = 0.01f;

        /// <summary>「有魔力可用」提示文案的 Keyed 键；null 表示不输出（保持物品原有 inspect）。</summary>
        public string inspectWorkingKey;

        /// <summary>「附近没有可用魔力」提示文案的 Keyed 键；null 表示不输出。</summary>
        public string inspectNoManaKey;

        /// <summary>存档键前缀。必须唯一（存档字段平铺在 Thing 节点下）。</summary>
        public string saveKeyPrefix = "botaniaManaItem";

        /// <summary>旧版存档键（兼容用）：迁移前用过的「是否有魔力」字段名。只读。</summary>
        public string legacyHasManaKey;

        /// <summary>旧版存档键（兼容用）：迁移前用过的「下一次可用 tick」字段名。只读。</summary>
        public string legacyNextUseKey;

        public CompProperties_ManaItem()
        {
            // 刻意不设 compClass：本类不单独出现在 XML 里。
            // 具体物品的 props 子类必须在自己的构造里把 compClass 指向自己的 Comp，
            // 并顺手把 saveKeyPrefix 改成该物品专属的前缀。
        }

        public string HasManaSaveKey => saveKeyPrefix + "HasMana";
        public string NextUseSaveKey => saveKeyPrefix + "NextUseTick";

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (searchRadius < 0f)
            {
                yield return "searchRadius must not be negative.";
            }
            if (manaPerUse < 0f)
            {
                yield return "manaPerUse must not be negative.";
            }
            if (cooldownTicks < 0)
            {
                yield return "cooldownTicks must not be negative.";
            }
            if (manaTolerance < 0f)
            {
                yield return "manaTolerance must not be negative.";
            }
            if (saveKeyPrefix.NullOrEmpty())
            {
                yield return "saveKeyPrefix must not be empty (save fields are flat per Thing).";
            }
        }
    }

    /// <summary>
    /// 物品付款核心：找魔力源（**所在格的魔力网络 → 物品自身储量 → 没有**）→ 凑够才扣 → 冷却节流 → 存档。
    /// 刻意不是 ThingComp，方便组合进任何父类（武器、工具、装备都可以）。
    ///
    /// 铁律（AGENTS.md §5）：
    ///   · 节流只有 <see cref="Ready"/> 一处（单层：TicksGame &gt;= nextUseTick），
    ///     调用方不得再叠加 IsHashIntervalTick 或自建计数；
    ///   · 不写别的组件的状态（付款只走 IManaSource 的公开接口 DrainMana / AddMana）；
    ///   · 存档键带前缀，旧键只读兼容。
    /// </summary>
    public class ManaItemCore
    {
        private readonly ThingComp owner;
        private readonly CompProperties_ManaItem props;
        private int nextUseTick;

        public ManaItemCore(ThingComp owner, CompProperties_ManaItem props)
        {
            this.owner = owner;
            this.props = props;
        }

        public CompProperties_ManaItem Props => props;

        /// <summary>上一次付款是否凑得齐（提示文案 / 状态显示用）。</summary>
        public bool HasMana { get; private set; } = true;

        /// <summary>下一次可用的时刻（只用于调试/诊断，改动请走 <see cref="NotifyUsed"/>）。</summary>
        public int NextUseTick => nextUseTick;

        private Thing Parent => owner?.parent;

        /// <summary>装备在身上时 parent.Map 是空的，必须顺着 ParentHolder 找持有者的地图。</summary>
        private Map ItemMap => EquipUtil.GetMap(Parent);

        /// <summary>装备在身上时 parent.Position 是空的，必须顺着 ParentHolder 找持有者的位置。</summary>
        private IntVec3 ItemPosition => EquipUtil.GetPosition(Parent);

        /// <summary>
        /// 现在是否允许使用。**唯一的节流点**：只做一次整数比较，开销可忽略。
        /// 子类不要在别处再写一个 tick 计数（相位不同会导致永远等不到可用时刻）。
        /// </summary>
        public bool Ready
        {
            get
            {
                // 与 ManaDeviceCore.ShouldWorkNow 同一写法：单层整数比较，不叠 IsHashIntervalTick。
                return Find.TickManager.TicksGame >= nextUseTick;
            }
        }

        /// <summary>
        /// 一次使用成功后调用，安排下一次可用时刻。
        /// 只有**成功**才调它（失败不算使用，不能进冷却——技能那边也是这个语义）。
        /// </summary>
        public void NotifyUsed()
        {
            nextUseTick = Find.TickManager.TicksGame + Math.Max(0, props.cooldownTicks);
        }

        /// <summary>开发调试：把下一次可用时刻清零（相当于「清除冷却」）。</summary>
        public void DebugSkipCooldown()
        {
            nextUseTick = 0;
        }

        /// <summary>
        /// 尝试支付 amount 点魔力。**凑够才扣**——魔力不足时一点不抽，也不改任何源，
        /// 避免把池子抽干却干不成活（旧版 Fluxfield / 修复器踩过这个坑）。
        /// 找不到源、或抽到一半不够时返回 false（已抽的部分会退回）。
        /// </summary>
        public bool TryPay(float amount)
        {
            if (amount <= 0f)
            {
                HasMana = true;
                return true;
            }
            Map map = ItemMap;
            if (map == null)
            {
                HasMana = false;
                return false;
            }
            IManaSource source = FindSource(amount);
            if (source == null || source.ManaAmount + props.manaTolerance < amount)
            {
                HasMana = false;
                return false;
            }
            float drained = source.DrainMana(amount);
            if (drained + props.manaTolerance < amount)
            {
                // 抽到的比预期少：退回，不做半支付
                source.AddMana(drained);
                HasMana = false;
                return false;
            }
            HasMana = true;
            return true;
        }

        /// <summary>按 <see cref="manaPerUse"/> 支付的便捷重载（固定费用物品用）。</summary>
        public bool TryPay()
        {
            return TryPay(props.manaPerUse);
        }

        /// <summary>
        /// 按「有多少抽多少」支付：最多抽 amount，返回实际抽到的量。
        /// 与 <see cref="TryPay(float)"/> 不同，允许部分支付（按实际效果计费的物品用）。
        /// </summary>
        public float TryPayUpTo(float amount)
        {
            if (amount <= 0f)
            {
                HasMana = true;
                return 0f;
            }
            IManaSource source = FindSource(0f);
            if (source == null)
            {
                HasMana = false;
                return 0f;
            }
            float take = Math.Min(amount, source.ManaAmount);
            if (take <= 0f)
            {
                HasMana = false;
                return 0f;
            }
            float drained = source.DrainMana(take);
            HasMana = drained > 0f;
            return drained;
        }

        /// <summary>
        /// 找可支付的魔力源（**不扣款**）。给需要自己控制付款节奏的物品用
        /// （例如先看有多少、再按实际用量扣、多抽的还要退）。
        ///
        /// 三级回退（消费口径与设备一致）：
        ///   1. **魔力网络** —— 物品（持有者）所在格的网络分量，中继塔延伸到的池子也算数；
        ///   2. **物品自身储量** —— 只有自带储能的物品才有（目前只有法杖 <see cref="CompManaWeapon"/>），
        ///      在「没有网络 / 网络里的魔力不够这笔」时才兜底；
        ///   3. 两者都没有 → null（真的付不出）。
        ///
        /// <paramref name="needAtLeast"/> 含等于：刚好够用也算可用。
        /// </summary>
        public IManaSource FindSource(float needAtLeast = 0f)
        {
            IManaSource network = FindNetworkSource(needAtLeast);
            if (network != null)
            {
                return network;
            }
            return FindOwnStorageSource(needAtLeast);
        }

        /// <summary>
        /// 只找**魔力网络**里的源（第一级，不看物品自身储量）。
        /// 不在网络内、或网络里的魔力不够 <paramref name="needAtLeast"/> 时返回 null。
        /// </summary>
        private IManaSource FindNetworkSource(float needAtLeast)
        {
            Map map = ItemMap;
            if (map == null)
            {
                return null;
            }
            IManaSource source = map.GetComponent<MapComponent_ManaNetwork>()?.SourceAt(ItemPosition);
            if (source == null)
            {
                return null;
            }
            return source.ManaAmount + props.manaTolerance >= needAtLeast ? source : null;
        }

        /// <summary>
        /// 找**物品自身储量**的源（第二级）。没有自带储能的物品（泰拉刃、设备）永远返回 null；
        /// 自带储能不够 <paramref name="needAtLeast"/> 时也返回 null（凑够才扣）。
        /// </summary>
        private IManaSource FindOwnStorageSource(float needAtLeast)
        {
            CompManaWeapon weapon = Parent?.TryGetComp<CompManaWeapon>();
            if (weapon == null)
            {
                return null;
            }
            return weapon.Mana + props.manaTolerance >= needAtLeast ? new OwnStorageManaSource(weapon) : null;
        }

        /// <summary>
        /// 这个源是不是「物品自身储量」的适配器。
        /// 付款方用它避开「自抽自充」：先扣过储能、又让找源回退到同一份储能 = 同一笔魔力算两遍。
        /// </summary>
        public bool IsOwnStorageSource(IManaSource source)
        {
            return source is OwnStorageManaSource;
        }

        /// <summary>
        /// 「物品自身储量」的轻量适配器：把 <see cref="CompManaWeapon"/> 的杖内余额包装成
        /// <see cref="IManaSource"/>，让 TryPay / PullFromSource 不用为「网络」和「杖内」写两套代码。
        /// 不缓存余额，每次查询现读（余额随时在变，闸门只有 CompManaWeapon 一处）。
        /// </summary>
        private sealed class OwnStorageManaSource : IManaSource
        {
            private readonly CompManaWeapon weapon;

            public OwnStorageManaSource(CompManaWeapon weapon)
            {
                this.weapon = weapon;
            }

            public float ManaAmount => weapon.Mana;

            public float FreeSpace => weapon.MaxMana - weapon.Mana;

            public float AddMana(float amount)
            {
                // CompManaWeapon.AddMana 是 void 且自带 maxMana 上限，所以用余额差算「实际接收量」。
                float before = weapon.Mana;
                weapon.AddMana(amount);
                return weapon.Mana - before;
            }

            public float DrainMana(float amount)
            {
                return weapon.DrainMana(amount);
            }
        }

        /// <summary>
        /// 可支付的魔力余量（提示文案里的「可用」用）：网络优先，其次物品自身储量。
        /// 两处都没有魔力时返回 0。
        /// </summary>
        public float NearbyMana()
        {
            IManaSource source = FindSource(0f);
            return source?.ManaAmount ?? 0f;
        }

        /// <summary>
        /// 通用提示文案。没配置 inspect key 时返回 null（**不会**改变物品原有 inspect 文本）。
        /// </summary>
        public string InspectString()
        {
            if (ItemMap == null)
            {
                return null;
            }
            string key = HasMana ? props.inspectWorkingKey : props.inspectNoManaKey;
            return key.NullOrEmpty() ? null : key.Translate();
        }

        /// <summary>
        /// 存档：新键带前缀；旧键只在读档时读一次，**不回写**（回写会和新键重复占坑）。
        /// </summary>
        public void ExposeData()
        {
            bool hasMana = HasMana;
            int next = nextUseTick;
            Scribe_Values.Look(ref hasMana, props.HasManaSaveKey, true);
            Scribe_Values.Look(ref next, props.NextUseSaveKey, 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (!props.legacyHasManaKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref hasMana, props.legacyHasManaKey, hasMana);
                }
                if (!props.legacyNextUseKey.NullOrEmpty())
                {
                    Scribe_Values.Look(ref next, props.legacyNextUseKey, next);
                }
            }
            HasMana = hasMana;
            nextUseTick = next;
        }
    }
}
