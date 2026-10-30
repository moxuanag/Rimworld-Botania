using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    public class CompProperties_ManaWeapon : CompProperties
    {
        /// <summary>杖内魔力上限。</summary>
        public float maxMana = 500f;

        /// <summary>每次射击消耗的魔力。</summary>
        public float manaPerShot = 50f;

        /// <summary>向魔力网抽取的半径。</summary>
        public float sourceRadius = 6f;

        /// <summary>放在地上时每秒自动吸纳的魔力。</summary>
        public float rechargePerSecond = 25f;

        public CompProperties_ManaWeapon()
        {
            compClass = typeof(CompManaWeapon);
        }

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
            if (manaPerShot < 0f)
            {
                yield return "manaPerShot must not be negative.";
            }
            if (manaPerShot > maxMana)
            {
                yield return "manaPerShot is larger than maxMana, the weapon could never fire.";
            }
        }
    }

    /// <summary>
    /// 魔力武器的储能：杖内自带 500 点魔力，射击扣 50。
    /// 魔力不足时会就近向魔力池/中继塔借；借不到才算空弹。
    /// 装备状态下的 Thing.Map 是空的，所以位置一律走 EquipUtil。
    /// </summary>
    public class CompManaWeapon : ThingComp
    {
        private float mana;

        private int ticksAccum;

        /// <summary>是否已经给过初始魔力。老存档里的法杖魔力是 0，读档时补满一次。</summary>
        private bool initialized;

        public CompProperties_ManaWeapon Props => (CompProperties_ManaWeapon)props;

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
            if (mana >= Props.maxMana - 0.01f)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < 30)
            {
                return;
            }
            ticksAccum = 0;
            PullFromSource(Props.rechargePerSecond * 0.5f);
        }

        /// <summary>从最近的魔力源抽一点，返回实际抽到的量。</summary>
        public float PullFromSource(float want)
        {
            if (want <= 0f)
            {
                return 0f;
            }
            Map map = EquipUtil.GetMap(parent);
            if (map == null)
            {
                return 0f;
            }
            float room = Props.maxMana - mana;
            if (room <= 0.01f)
            {
                return 0f;
            }
            IManaSource source = ManaSourceUtil.FindNearest(
                EquipUtil.GetPosition(parent), map, Props.sourceRadius, 1f);
            if (source == null)
            {
                return 0f;
            }
            float got = source.DrainMana(Mathf.Min(want, room));
            mana += got;
            return got;
        }

        /// <summary>
        /// 扣魔力：先看杖内，不够就现场向附近的魔力网借。
        /// 返回 false 表示确实打不出来这一发。
        /// </summary>
        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
            {
                return true;
            }

            // 杖内余额 + 附近魔力源「共同付款」：
            // 以前是先把魔力补进杖里再扣，受 maxMana 限制，超过容量的法术永远打不出来
            // （比如 600 魔力的转化，基础杖只能装 500）。
            float fromWand = Mathf.Min(mana, amount);
            float needFromSource = amount - fromWand;

            IManaSource source = null;
            if (needFromSource > 0.01f)
            {
                Map map = EquipUtil.GetMap(parent);
                if (map != null)
                {
                    source = ManaSourceUtil.FindNearest(
                        EquipUtil.GetPosition(parent), map, Props.sourceRadius, needFromSource);
                }
                if (source == null || source.ManaAmount + 0.01f < needFromSource)
                {
                    return false;
                }
            }

            mana -= fromWand;
            if (needFromSource > 0.01f)
            {
                float got = source.DrainMana(needFromSource);
                if (got + 0.01f < needFromSource)
                {
                    // 抽到的比预期少：回滚，不要半支付
                    mana += fromWand;
                    source.AddMana(got);
                    return false;
                }
            }
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

        /// <summary>从半径内最近的魔力池/中继塔抽满，返回实际抽到的量。</summary>
        public void Charge()
        {
            if (mana >= Props.maxMana - 0.01f)
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
            Scribe_Values.Look(ref mana, "mana", 0f);
            Scribe_Values.Look(ref initialized, "manaInitialized", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }
    }
}
