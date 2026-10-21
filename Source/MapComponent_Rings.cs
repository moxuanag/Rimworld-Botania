using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 鼠标悬停某个法杖技能按钮时，记下"此刻该画什么范围"，
    /// 由 MapComponent_Rings 在 Draw 阶段真正画出来。
    /// 这样范围只在悬停时出现，不会一直糊在地图上。
    /// </summary>
    public static class WandRangePreview
    {
        public static float CastRange;
        public static float EffectRadius;
        public static int ExpiresAtTick = -1;

        public static bool Active => ExpiresAtTick >= Find.TickManager.TicksGame;

        public static void Show(float castRange, float effectRadius)
        {
            CastRange = castRange;
            EffectRadius = effectRadius;
            ExpiresAtTick = Find.TickManager.TicksGame + 1;
        }

        public static void Clear()
        {
            ExpiresAtTick = -1;
        }
    }

    /// <summary>法杖技能的按钮：外观与原版灵能一致，只多一个悬停显示范围。</summary>
    public class Command_AbilityWand : Command_Ability
    {
        /// <summary>本按钮上一帧的屏幕矩形，用来判断鼠标到底在不在按钮上。</summary>
        private Rect lastRect = Rect.zero;

        public Command_AbilityWand(Ability ability, Pawn pawn) : base(ability, pawn)
        {
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            lastRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }

        public override void GizmoUpdateOnMouseover()
        {
            base.GizmoUpdateOnMouseover();
            // 这一版引擎对整排按钮都会调这个回调，所以必须自己判断鼠标是否真的在按钮上，
            // 否则圈会一直挂在地图上。
            if (!(ability is Ability_WandSpell spell) || !Mouse.IsOver(lastRect))
            {
                WandRangePreview.Clear();
                return;
            }
            WandRangePreview.Show(spell.CastRange, spell.EffectRadius);
        }
    }

    /// <summary>
    /// 统一的假想范围显示：
    /// 1. 选中带 CompProperties_RadiusRing 的建筑 → 一直画它的作用半径；
    /// 2. 鼠标悬停法杖技能按钮 → 画施法距离（青）与法术影响半径（绿）。
    /// 挂在 MapComponent 上而不是 Comp.PostDraw，是因为建筑的 Draw 不一定会调到 Comp.PostDraw。
    /// </summary>
    public class MapComponent_Rings : MapComponent
    {
        private static readonly Color BuildingRing = new Color(0.92f, 0.88f, 0.55f, 0.75f);
        private static readonly Color CastRing = new Color(0.45f, 0.80f, 1f, 0.55f);
        private static readonly Color EffectRing = new Color(0.42f, 0.95f, 0.58f, 0.70f);

        public MapComponent_Rings(Map map) : base(map)
        {
        }

        public override void MapComponentDraw()
        {
            DrawBuildingRings();
            DrawWandPreview();
        }

        private void DrawBuildingRings()
        {
            List<object> selected = Find.Selector.SelectedObjects;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!(selected[i] is Thing thing))
                {
                    continue;
                }
                if (thing.Destroyed || thing.Map != map)
                {
                    continue;
                }
                IntVec3 pos = thing.Position;
                if (!pos.IsValid)
                {
                    continue;
                }

                // 中继塔：选中时画整张网络，而不是只有自己这一环
                if (thing.TryGetComp<CompManaRelay>() != null)
                {
                    ManaNetworkPreview.Draw(map, pos, RelayRadius(thing));
                    continue;
                }

                CompRadiusRing ring = thing.TryGetComp<CompRadiusRing>();
                if (ring == null)
                {
                    continue;
                }
                GenDraw.DrawRadiusRing(pos, ring.Props.radius, BuildingRing);
            }
        }

        private static float RelayRadius(Thing thing)
        {
            CompManaRelay relay = thing.TryGetComp<CompManaRelay>();
            float radius = relay?.Props?.radius ?? 6f;
            return radius;
        }

        private void DrawWandPreview()
        {
            if (!WandRangePreview.Active)
            {
                return;
            }
            if (!(Find.Selector.FirstSelectedObject is Pawn pawn) || pawn.Map != map)
            {
                return;
            }
            if (pawn.equipment?.Primary?.GetComp<CompWand>() == null)
            {
                return;
            }
            GenDraw.DrawRadiusRing(pawn.Position, WandRangePreview.CastRange, CastRing);
            if (WandRangePreview.EffectRadius > 0.5f)
            {
                GenDraw.DrawRadiusRing(pawn.Position, WandRangePreview.EffectRadius, EffectRing);
            }
        }
    }
}
