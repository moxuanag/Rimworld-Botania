using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 武器侧 Gizmo：原版「精神力」那种条，显示在征召按钮旁边。
    /// 引擎给已装备物品的 Gizmo 出口是 CompEquippable.CompGetEquippedGizmosExtra()，
    /// 所以用 CompEquippable 的子类接管，XML 里用 Inherit="False" 整段替换，
    /// 保证武器身上只有一个装备组件。
    /// </summary>
    public class CompProperties_Equippable_Wand : CompProperties
    {
        public CompProperties_Equippable_Wand()
        {
            compClass = typeof(CompEquippable_Wand);
        }
    }

    public class CompEquippable_Wand : CompEquippable
    {
        public override IEnumerable<Gizmo> CompGetEquippedGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetEquippedGizmosExtra())
            {
                yield return gizmo;
            }

            CompManaWeapon weapon = parent?.GetComp<CompManaWeapon>();
            if (weapon == null)
            {
                yield break;
            }

            yield return new Gizmo_ManaBar(weapon);
        }
    }

    /// <summary>
    /// 参照原版精神力条的只读魔力条：宽度紧凑、蓝色、不可点，
    /// 数值随开火实时变化，悬停给出每发消耗与剩余发数。
    /// 数值全部来自手里那根杖的 CompProperties_ManaWeapon，
    /// 所以以后不同的魔杖自然会有不同的总量与消耗。
    /// </summary>
    [StaticConstructorOnStartup]
    public class Gizmo_ManaBar : Gizmo
    {
        private static readonly Color BarColor = new Color(0.36f, 0.62f, 0.95f);
        private static readonly Texture2D BarTex = SolidColorMaterials.NewSolidColorTexture(BarColor);

        private readonly CompManaWeapon weapon;

        public Gizmo_ManaBar(CompManaWeapon weapon)
        {
            this.weapon = weapon;
        }

        /// <summary>和原版状态条一样小，排在技能按钮左侧。</summary>
        public override float Order => -200f;

        public override float GetWidth(float maxWidth) => 100f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);

            float max = Mathf.Max(1f, weapon.Props.maxMana);
            int cur = Mathf.FloorToInt(weapon.Mana);
            float frac = Mathf.Clamp01(weapon.Mana / max);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 6f, rect.width, 22f), "Botania.ManaReadoutLabel".Translate());

            Widgets.FillableBar(new Rect(rect.x + 6f, rect.y + 30f, rect.width - 12f, 14f),
                frac, BarTex, BaseContent.BlackTex, doBorder: true);

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 48f, rect.width, 22f), cur + " / " + Mathf.RoundToInt(max));

            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                int shots = weapon.Props.manaPerShot > 0f
                    ? Mathf.FloorToInt(weapon.Mana / weapon.Props.manaPerShot)
                    : 0;
                TooltipHandler.TipRegion(rect, "Botania.ManaReadoutTip".Translate(
                    weapon.Props.manaPerShot.ToString("F0"), shots));
            }
            return new GizmoResult(GizmoState.Clear);
        }
    }
}
