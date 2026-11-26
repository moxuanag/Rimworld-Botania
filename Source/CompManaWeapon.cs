using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力武器的配置。找源半径 / 每次使用费用 / 存档键前缀已并入物品付款框架
    /// <see cref="CompProperties_ManaItem"/>，本类只保留武器自己的两个参数：
    /// 杖内储能上限与「躺在地上时」的自充速率。
    ///
    /// 旧字段改名（XML 必须同步改，否则加载报「字段不存在」）：
    ///   · <c>&lt;sourceRadius&gt;</c> → <c>&lt;searchRadius&gt;</c>（基类字段，数值不变）
    ///   · <c>&lt;manaPerShot&gt;</c>  → <c>&lt;manaPerUse&gt;</c>（基类字段，数值不变）
    /// </summary>
    public class CompProperties_ManaWeapon : CompProperties_ManaItem
    {
        /// <summary>杖内魔力上限。</summary>
        public float maxMana = 500f;

        /// <summary>放在地上时每秒自动吸纳的魔力。</summary>
        public float rechargePerSecond = 25f;

        /// <summary>
        /// 兼容属性：<c>Verb_ManaBolt</c> / <c>Gizmo_ManaBar</c> 还在读
        /// <c>Props.manaPerShot</c>，实际数值统一落到基类
        /// <see cref="CompProperties_ManaItem.manaPerUse"/>（等那两个文件迁完可删）。
        /// </summary>
        public float manaPerShot => manaPerUse;

        public CompProperties_ManaWeapon()
        {
            compClass = typeof(CompManaWeapon);
            // 旧字段的默认值（XML 会覆盖）：sourceRadius=6、manaPerShot=50。
            searchRadius = 6f;
            manaPerUse = 50f;
            // 本引擎存档把组件字段平铺在 Thing 节点下，键必须够独特。
            saveKeyPrefix = "botaniaManaWeapon";
            // 充能节奏：旧实现是「每 30 tick 吸一口」。节流只由 ManaItemCore 一处负责。
            // 这里复用物品框架的 cooldown 作为**充能间隔**；开火节奏仍由武器 verb 的
            // RangedWeapon_Cooldown 管，TrySpend 不查 Ready，所以不会出现双重节流（AGENTS.md §5）。
            cooldownTicks = RechargeIntervalTicks;
        }

        /// <summary>放在地上时每 30 tick（0.5 秒）结算一次充能。</summary>
        public const int RechargeIntervalTicks = 30;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (maxMana <= 0f)
            {
                yield return "maxMana must be greater than 0.";
            }
            if (manaPerUse < 0f)
            {
                yield return "manaPerUse must not be negative.";
            }
            if (manaPerUse > maxMana)
            {
                yield return "manaPerUse is larger than maxMana, the weapon could never fire.";
            }
        }
    }

    /// <summary>
    /// 魔力武器的储能：杖内自带 maxMana 点魔力，射击扣 manaPerUse。
    /// 魔力不足时先向**持有者所在格的魔力网络**借；连不上网络（或网络里不够）才用杖内储量兜底。
    /// 装备状态下的 Thing.Map 是空的，所以位置一律走 <see cref="EquipUtil"/>
    /// （<see cref="ManaItemCore"/> 内部已处理，本类不再自己绕 ParentHolder）。
    ///
    /// 框架迁移：找源 / 凑够才扣 / 充能节流 / 存档交给 <see cref="ManaItemCore"/>；
    /// 本类只保留「杖内余额」这个物品独有的账：先扣杖内，不够的部分再问核心要。
    /// </summary>
    public class CompManaWeapon : ThingComp
    {
        /// <summary>浮点容差，与旧实现里的 0.01f 字面量一致。</summary>
        private const float ManaEpsilon = 0.01f;

        private const string ManaSaveKey = "botaniaManaWeaponMana";
        private const string InitializedSaveKey = "botaniaManaWeaponInitialized";

        private float mana;

        /// <summary>是否已经给过初始魔力。老存档里的法杖魔力是 0，读档时补满一次。</summary>
        private bool initialized;

        private ManaItemCore core;

        public CompProperties_ManaWeapon Props => (CompProperties_ManaWeapon)props;

        /// <summary>物品付款框架：找魔力源 / 凑够才扣 / 充能节流 / 存档。</summary>
        private ManaItemCore Core => core ??= new ManaItemCore(this, Props);

        public float Mana
        {
            get
            {
                EnsureInitialized();
                return mana;
            }
        }

        public float MaxMana => Props.maxMana;

        /// <summary>
        /// 刚造出来就是满的，不然小人拿到手里就是一根空杖（被拿着不会自充）。
        /// ThingComp 没有 PostMake 可重写，所以用「读到时懒初始化」：
        /// 新建的杖 initialized 为 false、mana 为 0，第一次读取就补满。
        /// 读旧档也一样走这条路，老存档里那些 0 魔力的杖会自动补满一次。
        /// </summary>
        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;
            if (mana <= 0.01f)
            {
                mana = Props.maxMana;
            }
        }

        public override void CompTick()
        {
            EnsureInitialized();
            // 只有当法杖躺在魔力池旁边（而不是被拿着）时才慢慢吸魔
            if (!parent.Spawned)
            {
                return;
            }
            // 节流只有 Core 一处（Ready / NotifyUsed，间隔 = Props.cooldownTicks）：
            // 不再自建 tick 计数，也不叠 IsHashIntervalTick（AGENTS.md §5）。
            if (!Core.Ready)
            {
                return;
            }
            Core.NotifyUsed();
            if (mana >= Props.maxMana - ManaEpsilon)
            {
                return;
            }
            PullFromSource(Props.rechargePerSecond * 0.5f);
        }

        /// <summary>
        /// 从**持有者所在格的魔力网络**抽一点，返回实际抽到的量；网络不可用时不动（见下面的自抽保护）。
        /// </summary>
        public float PullFromSource(float want)
        {
            if (want <= 0f)
            {
                return 0f;
            }
            float room = Props.maxMana - mana;
            if (room <= ManaEpsilon)
            {
                return 0f;
            }
            // 找源交给核心（装备态会自动顺着 ParentHolder 找持有者位置）。
            // 门槛 1f 保持旧行为：不足 1 点的源视为空源不去吸。
            // 注意：核心的 FindSource 是「网络优先 → 杖内储量兜底」，而这里的目的就是把魔力
            // **补进杖里**，所以命中杖自身储量时必须跳过，否则等于自抽自充（白抽、还多跑一遍循环）。
            IManaSource source = Core.FindSource(1f);
            if (source == null || Core.IsOwnStorageSource(source))
            {
                // 没有网络（或网络里不足 1 点）时不充能：杖内储量不能给自己充电。
                return 0f;
            }
            float got = source.DrainMana(Mathf.Min(want, room));
            mana += got;
            return got;
        }

        /// <summary>
        /// 扣魔力：付钱顺序 = **魔力网络优先，杖内储量兜底**。
        /// 网络付不起、或没有网络时才动杖内余额。
        /// 返回 false 表示确实打不出来这一发。
        /// </summary>
        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
            {
                return true;
            }

            // 1) 先问魔力网络要全款。TryPay 本身就是「凑够才扣、不够一点不抽」，失败时
            //    不会动任何源（网络付不起就一点不抽，杖内也还没动）。
            if (Core.TryPay(amount))
            {
                return true;
            }

            // 2) 网络不够 / 没连上网络 → **杖内储量兜底**，仍然「凑够才扣」：
            //    杖内不够这一发时一点不扣，返回 false。
            if (mana + ManaEpsilon < amount)
            {
                return false;
            }
            DrainMana(amount);
            return true;
        }

        public void AddMana(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            mana = Mathf.Min(Props.maxMana, mana + amount);
        }

        /// <summary>
        /// 从杖内余额扣 <paramref name="amount"/>，返回**实际扣到**的量（不超过当前 <see cref="Mana"/>）。
        /// 给 <see cref="ManaItemCore"/> 的「自身储量适配器」用：没有网络时，
        /// 杖内储量要能被当作一个普通 IManaSource 被 TryPay / PullFromSource 使用。
        /// 唯一写 mana 的扣减口就这一处（TrySpend 的杖内兜底也走它），不另开第二条账。
        /// </summary>
        public float DrainMana(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            float take = Mathf.Min(Mana, amount);
            if (take <= 0f)
            {
                return 0f;
            }
            mana -= take;
            return take;
        }

        public override string CompInspectStringExtra()
        {
            return "Botania.WandMana".Translate(mana.ToString("F0"), Props.maxMana.ToString("F0"));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            // 法杖躺在地上被选中时的「充能」按钮（拿在小人手里时由 CompEquippable_Wand 提供）
            yield return MakeChargeGizmo();
        }

        /// <summary>
        /// 「从魔力池充能」按钮。放在这里是为了让「地上的法杖」和
        /// 「小人手里的法杖」共用同一个按钮与同一套提示。
        /// </summary>
        public Gizmo MakeChargeGizmo()
        {
            return new Command_Action
            {
                defaultLabel = "Botania.WandChargeLabel".Translate()
                    + " (" + mana.ToString("F0") + "/" + Props.maxMana.ToString("F0") + ")",
                defaultDesc = "Botania.WandChargeDesc".Translate(Props.maxMana.ToString("F0")),
                icon = parent.def.uiIcon,
                action = Charge
            };
        }

        /// <summary>从**所在格的魔力网络**抽满，返回实际抽到的量（没连上网络就是 0）。</summary>
        public void Charge()
        {
            if (mana >= Props.maxMana - ManaEpsilon)
            {
                Messages.Message("Botania.WandAlreadyFull".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            float got = PullFromSource(Props.maxMana - mana);
            if (got <= 0.01f)
            {
                Messages.Message("Botania.WandNoSource".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message(
                "Botania.WandCharged".Translate(mana.ToString("F0"), Props.maxMana.ToString("F0")),
                MessageTypeDefOf.PositiveEvent, false);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // 充能节流状态（下一次可用 tick）与「上次付款是否成功」由核心负责，
            // 键 = botaniaManaWeaponHasMana / botaniaManaWeaponNextUseTick。
            Core.ExposeData();
            // 杖内余额是物品独有的账，键同样带前缀（本引擎存档平铺在 Thing 节点下）。
            Scribe_Values.Look(ref mana, ManaSaveKey, 0f);
            Scribe_Values.Look(ref initialized, InitializedSaveKey, false);
            // 旧档键只读不写：老版本用的是无前缀的 mana / manaInitialized。
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref mana, "mana", mana);
                Scribe_Values.Look(ref initialized, "manaInitialized", initialized);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }
    }
}
