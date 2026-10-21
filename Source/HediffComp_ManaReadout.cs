using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「魔力储备」状态的 Gizmo：持杖时在技能栏最前面显示一根只读魔力条，
    /// 参照 CE 的弹药显示——只有数字和进度条，不可点。
    /// </summary>
    public class HediffComp_ManaReadout : HediffComp
    {
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.equipment == null)
            {
                yield break;
            }
            CompManaWeapon weapon = pawn.equipment.Primary?.GetComp<CompManaWeapon>();
            if (weapon == null)
            {
                yield break;
            }

            int cur = Mathf.FloorToInt(weapon.Mana);
            int max = Mathf.RoundToInt(weapon.Props.maxMana);

            yield return new Gizmo_ManaReadout
            {
                Current = cur,
                Max = max,
                Shots = weapon.Props.manaPerShot > 0f
                    ? Mathf.FloorToInt(weapon.Mana / weapon.Props.manaPerShot)
                    : 0
            };
        }
    }

    /// <summary>只读魔力条。</summary>
    public class Gizmo_ManaReadout : Gizmo
    {
        public int Current;
        public int Max;
        public int Shots;

        public override float Order => -100f;

        public override float GetWidth(float maxWidth) => 130f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);

            float frac = Max > 0 ? Mathf.Clamp01((float)Current / Max) : 0f;
            Rect bar = new Rect(rect.x + 6f, rect.y + 48f, rect.width - 12f, 12f);
            Widgets.FillableBar(bar, frac, SolidColorMaterials.NewSolidColorTexture(new Color(0.42f, 0.72f, 0.98f)), BaseContent.BlackTex, doBorder: true);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 22f), "Botania.ManaReadoutLabel".Translate());

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 24f, rect.width - 12f, 22f), Current + " / " + Max);

            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect, "Botania.ManaReadoutTip".Translate(Shots));
            }
            return new GizmoResult(GizmoState.Clear);
        }
    }
}
