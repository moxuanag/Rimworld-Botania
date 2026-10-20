using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 携带森林法杖时挂在小人身上的「魔力储备」。
    /// 装备上的 Comp 按钮这一版不显示，所以把魔力做成一个会随杖内存量变化的 hediff：
    /// 健康面板里能直接看到数值和底部那条进度条，换掉法杖会自动消失。
    /// </summary>
    public class Hediff_WandMana : HediffWithComps
    {
        private const float MinSeverity = 0.01f;

        public override string LabelBase
        {
            get
            {
                float max = def != null ? def.maxSeverity : 500f;
                return "Botania.WandManaHediff".Translate(Severity.ToString("F0"), max.ToString("F0"));
            }
        }

        public override string TipStringExtra => "Botania.WandManaHediffTip".Translate();

        public override void Tick()
        {
            base.Tick();
            Pawn holder = pawn;
            if (holder == null || holder.Dead)
            {
                return;
            }
            CompManaWeapon weapon = FindWeapon(holder);
            if (weapon == null)
            {
                holder.health.RemoveHediff(this);
                return;
            }
            // 0 会让 hediff 被当作"已痊愈"，所以留一点底
            Severity = Mathf.Max(MinSeverity, weapon.Mana);
        }

        public static CompManaWeapon FindWeapon(Pawn pawn)
        {
            if (pawn == null || pawn.equipment == null)
            {
                return null;
            }
            ThingWithComps primary = pawn.equipment.Primary;
            if (primary == null)
            {
                return null;
            }
            return primary.GetComp<CompManaWeapon>();
        }
    }
}
