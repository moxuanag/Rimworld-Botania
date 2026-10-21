using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 建筑的范围显示，统一在这里画。
    /// 1. 选中带 CompProperties_RadiusRing 的建筑 → 画它的作用半径；
    /// 2. 选中魔力中继塔 → 画它所在的**整张网络**（和自己的蓝图预览一致）。
    ///
    /// 法杖技能的范围不在这里画：那个由 Botania.Verb_WandCast 在施法选点时
    /// 跟着鼠标走，平时（没在选点）小人身上不该有任何圈。
    ///
    /// 挂 MapComponent 而不是 Comp.PostDraw，是因为建筑的 Draw 不一定会调到 Comp.PostDraw。
    /// </summary>
    public class MapComponent_Rings : MapComponent
    {
        private static readonly Color BuildingRing = new Color(0.92f, 0.88f, 0.55f, 0.75f);

        public MapComponent_Rings(Map map) : base(map)
        {
        }

        public override void MapComponentDraw()
        {
            DrawBuildingRings();
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
            return relay?.Props?.radius ?? 8f;
        }
    }
}
