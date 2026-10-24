using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 建筑的范围显示，统一在这里画。
    /// 1. 选中带 CompProperties_RadiusRing 的建筑 → 画它的作用半径（外圈，黄）；
    /// 2. 选中带 CompProperties_SecondaryRing 的建筑 → 再画一圈贴身的小范围（内圈，绿）；
    ///    白雏菊就是这种：外圈 6 格吸魔，内圈 1.5 格是它实际转化的那 8 格；
    /// 3. 选中魔力中继塔 → 画它所在的**整张网络**（和自己的蓝图预览一致）。
    ///
    /// 法杖技能的范围不在这里画：那个由 Botania.Verb_WandCast 在施法选点时
    /// 跟着鼠标走，平时（没在选点）小人身上不该有任何圈。
    ///
    /// 挂 MapComponent 而不是 Comp.PostDraw，是因为建筑的 Draw 不一定会调到 Comp.PostDraw。
    /// </summary>
    public class MapComponent_Rings : MapComponent
    {
        private static readonly Color BuildingRing = new Color(0.92f, 0.88f, 0.55f, 0.75f);
        private static readonly Color InnerRing = new Color(0.55f, 0.92f, 0.62f, 0.85f);
        private static readonly Color CornerHave = new Color(0.55f, 0.92f, 0.62f, 0.85f);
        private static readonly Color CornerNeed = new Color(0.95f, 0.78f, 0.35f, 0.9f);

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

                // 外圈：作用范围（吸魔、修复、攻击……）
                CompRadiusRing ring = thing.TryGetComp<CompRadiusRing>();
                if (ring != null && ring.Props.radius > 0f)
                {
                    GenDraw.DrawRadiusRing(pos, ring.Props.radius, BuildingRing);
                }

                // 凝台蓝图：还没盖起来，四个角一律按「缺」画
                if (thing is Blueprint blueprint &&
                    blueprint.def.entityDefToBuild?.defName == "Botania_TerrasteelAltar")
                {
                    List<IntVec3> wanted = new List<IntVec3>();
                    CellRect rect = thing.OccupiedRect();
                    List<IntVec3> all = TerraAltarCorners.Of(rect);
                    for (int c = 0; c < all.Count; c++)
                    {
                        if (all[c].InBounds(map))
                        {
                            wanted.Add(all[c]);
                        }
                    }
                    if (wanted.Count > 0)
                    {
                        GenDraw.DrawFieldEdges(wanted, CornerNeed);
                    }
                    continue;
                }

                // 凝台：把四个基座的位置标出来——放了基座的绿、缺的黄
                CompTerraAltar altar = thing.TryGetComp<CompTerraAltar>();
                if (altar != null)
                {
                    List<IntVec3> have = new List<IntVec3>();
                    List<IntVec3> need = new List<IntVec3>();
                    List<IntVec3> cells = altar.PedestalCells();
                    for (int c = 0; c < cells.Count; c++)
                    {
                        IntVec3 cell = cells[c];
                        if (!cell.InBounds(map))
                        {
                            continue;
                        }
                        if (altar.HasPedestalAt(cell))
                        {
                            have.Add(cell);
                        }
                        else
                        {
                            need.Add(cell);
                        }
                    }
                    if (have.Count > 0)
                    {
                        GenDraw.DrawFieldEdges(have, CornerHave);
                    }
                    if (need.Count > 0)
                    {
                        GenDraw.DrawFieldEdges(need, CornerNeed);
                    }
                }

                // 内圈：贴身小范围（白雏菊的转化格）
                CompSecondaryRing inner = thing.TryGetComp<CompSecondaryRing>();
                if (inner != null && inner.Props.radius > 0f)
                {
                    GenDraw.DrawRadiusRing(pos, inner.Props.radius, InnerRing);
                }
            }
        }

        private static float RelayRadius(Thing thing)
        {
            CompManaRelay relay = thing.TryGetComp<CompManaRelay>();
            return relay?.Props?.radius ?? 8f;
        }
    }
}
