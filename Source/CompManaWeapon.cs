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

        public CompProperties_ManaWeapon Props => (CompProperties_ManaWeapon)props;

        public float Mana => mana;

        public float MaxMana => Props.maxMana;

        public override void CompTick()
        {
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
            if (mana + 0.01f < amount)
            {
                PullFromSource(amount - mana);
            }
            if (mana + 0.01f < amount)
            {
                return false;
            }
            mana -= amount;
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
            yield return new Command_Action
            {
                // 按钮上直接带存量，被拿着的时候也能一眼看到还剩几发
                defaultLabel = "Botania.WandChargeLabel".Translate()
                    + " (" + mana.ToString("F0") + "/" + Props.maxMana.ToString("F0") + ")",
                defaultDesc = "Botania.WandChargeDesc".Translate(Props.maxMana.ToString("F0")),
                icon = parent.def.uiIcon,
                action = Charge
            };
        }

        private void Charge()
        {
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
        }
    }
}
