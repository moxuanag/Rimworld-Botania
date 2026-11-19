using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚护盾 Hediff 的显示扩展：把「护盾 300 / 300」写进健康面板的条目后面，
    /// 例如「盖亚护盾 (300 / 300)」。数值来自 Botania.Pawn_GaiaBoss，这里只读。
    /// </summary>
    public class HediffCompProperties_GaiaShieldDisplay : HediffCompProperties
    {
        public HediffCompProperties_GaiaShieldDisplay()
        {
            compClass = typeof(HediffComp_GaiaShieldDisplay);
        }
    }

    public class HediffComp_GaiaShieldDisplay : HediffComp
    {
        public override string CompLabelInBracketsExtra
        {
            get
            {
                Pawn_GaiaBoss boss = parent?.pawn as Pawn_GaiaBoss;
                if (boss == null)
                {
                    return null;
                }
                return "Botania.GaiaShieldBracket".Translate(
                    Mathf.RoundToInt(boss.Shield).ToString(),
                    Mathf.RoundToInt(boss.ShieldMax).ToString());
            }
        }
    }
}
