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
    /// 3. 选中魔力中继塔 → 画它所在的**整张网络**（和自己的蓝图预览一致）；
    /// 4. 不受选中影响的即时反馈也在这里画：播种成功的格子短暂高亮（<see cref="TransientCellHighlight"/>）。
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
            // 播种成功后的短暂绿色格子高亮（约 1.5 秒）：与选中无关，每帧都要过一遍。
            // 内部自带 try/catch + 过期清理，列表为空时立刻返回，不分配。
            TransientCellHighlight.DrawFor(map);
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
                // 中继塔/魔力池的网络范围改由 MapComponent_ManaNetwork 画「边界」，
                // 这里不再画每个节点各自的圆（避免线段交叉重叠）。

                // 外圈：作用范围（吸魔、修复、攻击……）
                // 半径读 ring.Radius 而不是 ring.Props.radius：前者会优先采用同一 Thing 上
                // IDynamicRingRadius 给出的实时值（丰饶之花随研究档位 5/6/7 格）。
                CompRadiusRing ring = thing.TryGetComp<CompRadiusRing>();
                if (ring != null)
                {
                    float radius = ring.Radius;
                    if (radius > 0f)
                    {
                        GenDraw.DrawRadiusRing(pos, radius, BuildingRing);
                    }
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
