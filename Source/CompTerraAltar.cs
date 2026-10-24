using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉凝聚台（3×3 主体 + 四角基座）。
    ///
    /// 规矩：四个角的基座**缺一个就不工作**——这是文档第一部分 §12 定下的「结构感」，
    /// 平面上少一角，玩家一眼就能看出来自己没搭完。
    ///
    /// 材料从台面上取（玩家把魔力钢锭 / 精灵石英丢在台子上即可），
    /// 魔力从 8 格内最近的魔力源一次性扣 20,000。
    /// 先扣材料与魔力，再走 2,400 tick 的凝聚进度，完成时在台心生成一枚泰拉钢锭。
    /// </summary>
    public class CompProperties_TerraAltar : CompProperties
    {
        public float manaPerIngot = 20000f;
        public float searchRadius = 8f;
        public int ticksPerIngot = 2400;

        public ThingDef ingredientA;
        public int countA = 1;
        public ThingDef ingredientB;
        public int countB = 1;
        public ThingDef product;
        public ThingDef pedestalDef;

        public CompProperties_TerraAltar()
        {
            compClass = typeof(CompTerraAltar);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string err in base.ConfigErrors(parentDef))
            {
                yield return err;
            }
            if (product == null)
            {
                yield return "product 未设置";
            }
            if (pedestalDef == null)
            {
                yield return "pedestalDef 未设置";
            }
            if (ingredientA == null || ingredientB == null)
            {
                yield return "ingredientA / ingredientB 未设置";
            }
        }
    }

    public class CompTerraAltar : ThingComp
    {
        private int progress;
        private bool running;

        private Map Map => parent.Map;

        public CompProperties_TerraAltar Props => (CompProperties_TerraAltar)props;

        /// <summary>四个对角基座所在的格子（几何见 TerraAltarCorners，放置预览用的是同一份）。</summary>
        public List<IntVec3> PedestalCells()
        {
            return TerraAltarCorners.Of(parent.OccupiedRect());
        }

        /// <summary>这一格上有没有基座。</summary>
        public bool HasPedestalAt(IntVec3 cell)
        {
            Map map = Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def == Props.pedestalDef && !things[i].Destroyed)
                {
                    return true;
                }
            }
            return false;
        }

        public int MissingPedestals
        {
            get
            {
                Map map = Map;
                if (map == null)
                {
                    return 4;
                }
                int missing = 0;
                List<IntVec3> cells = PedestalCells();
                for (int i = 0; i < cells.Count; i++)
                {
                    IntVec3 cell = cells[i];
                    if (!cell.InBounds(map))
                    {
                        missing++;
                        continue;
                    }
                    if (!HasPedestalAt(cell))
                    {
                        missing++;
                    }
                }
                return missing;
            }
        }

        /// <summary>台面（3×3 自身格）上某材料的总数。</summary>
        private int CountOnPlatform(ThingDef def)
        {
            if (def == null)
            {
                return 0;
            }
            Map map = Map;
            if (map == null)
            {
                return 0;
            }
            int total = 0;
            foreach (IntVec3 cell in parent.OccupiedRect())
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def == def && !things[i].Destroyed)
                    {
                        total += things[i].stackCount;
                    }
                }
            }
            return total;
        }

        private void ConsumeFromPlatform(ThingDef def, int count)
        {
            Map map = Map;
            if (map == null || def == null || count <= 0)
            {
                return;
            }
            int left = count;
            foreach (IntVec3 cell in parent.OccupiedRect())
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0 && left > 0; i--)
                {
                    Thing t = things[i];
                    if (t.def != def || t.Destroyed)
                    {
                        continue;
                    }
                    int take = left < t.stackCount ? left : t.stackCount;
                    left -= take;
                    if (take >= t.stackCount)
                    {
                        t.Destroy();
                    }
                    else
                    {
                        t.stackCount -= take;
                    }
                }
                if (left <= 0)
                {
                    break;
                }
            }
        }

        public override void CompTick()
        {
            if (!parent.IsHashIntervalTick(60))
            {
                return;
            }
            Map map = Map;
            if (map == null)
            {
                return;
            }
            if (running)
            {
                progress += 60;
                if (progress >= Props.ticksPerIngot)
                {
                    FinishIngot();
                }
                return;
            }
            TryStart();
        }

        private void TryStart()
        {
            if (MissingPedestals > 0)
            {
                return;
            }
            if (CountOnPlatform(Props.ingredientA) < Props.countA)
            {
                return;
            }
            if (CountOnPlatform(Props.ingredientB) < Props.countB)
            {
                return;
            }
            IManaSource source = ManaSourceUtil.FindNearest(parent.Position, Map,
                Props.searchRadius, Props.manaPerIngot - 0.5f, parent);
            if (source == null)
            {
                return;
            }
            source.DrainMana(Props.manaPerIngot);
            ConsumeFromPlatform(Props.ingredientA, Props.countA);
            ConsumeFromPlatform(Props.ingredientB, Props.countB);
            running = true;
            progress = 0;
        }

        private void FinishIngot()
        {
            running = false;
            progress = 0;
            Map map = Map;
            if (map == null || Props.product == null)
            {
                return;
            }
            IntVec3 center = parent.OccupiedRect().CenterCell;
            if (!center.InBounds(map))
            {
                center = parent.Position;
            }
            Thing made = ThingMaker.MakeThing(Props.product);
            GenPlace.TryPlaceThing(made, center, map, ThingPlaceMode.Near);
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            if (running)
            {
                float pct = Props.ticksPerIngot <= 0 ? 1f
                    : progress / (float)Props.ticksPerIngot;
                sb.Append("Botania.TerraAltarWorking".Translate(pct.ToStringPercent()));
                return sb.ToString();
            }
            int missing = MissingPedestals;
            if (missing > 0)
            {
                sb.Append("Botania.TerraAltarNoPedestals".Translate(missing));
                return sb.ToString();
            }
            if (CountOnPlatform(Props.ingredientA) < Props.countA ||
                CountOnPlatform(Props.ingredientB) < Props.countB)
            {
                sb.Append("Botania.TerraAltarNeedInputs".Translate(
                    Props.ingredientA.label, Props.countA,
                    Props.ingredientB.label, Props.countB));
                return sb.ToString();
            }
            IManaSource source = ManaSourceUtil.FindNearest(parent.Position, Map,
                Props.searchRadius, Props.manaPerIngot - 0.5f, parent);
            if (source == null)
            {
                sb.Append("Botania.TerraAltarNoMana".Translate(
                    Props.manaPerIngot.ToString("F0"), Props.searchRadius.ToString("F0")));
                return sb.ToString();
            }
            sb.Append("Botania.TerraAltarReady".Translate());
            return sb.ToString();
        }

        /// <summary>这一格上有没有「基座的蓝图」（还没盖起来也算占位）。</summary>
        private bool HasPedestalBlueprintAt(IntVec3 cell)
        {
            Map map = Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint bp && bp.def.entityDefToBuild == Props.pedestalDef)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 还「没安排上」几个角：既没有实心基座、也没有基座蓝图。
        /// 和 MissingPedestals 的区别：蓝图不算「能干活」（生产判定必须实心），
        /// 但按钮该不该变灰看的是这一条——否则放完蓝图按钮还亮着，点了什么也不发生。
        /// </summary>
        private int MissingOrUnplanned()
        {
            int n = 0;
            List<IntVec3> cells = PedestalCells();
            for (int i = 0; i < cells.Count; i++)
            {
                if (!HasPedestalAt(cells[i]) && !HasPedestalBlueprintAt(cells[i]))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// 选中凝聚台时的「补齐四角基座」按钮：直接在对角格放蓝图，
        /// 和原版「点建筑就给你把附属件摆好」的体验一致。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            Command_Action place = new Command_Action
            {
                defaultLabel = "Botania.TerraAltarPlacePedestals".Translate(),
                defaultDesc = "Botania.TerraAltarPlacePedestalsDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Botania_PlacePedestals"),
                action = PlaceMissingPedestals
            };
            if (MissingOrUnplanned() == 0)
            {
                place.Disable("Botania.TerraAltarPedestalsComplete".Translate());
            }
            yield return place;
        }

        private void PlaceMissingPedestals()
        {
            Map map = Map;
            if (map == null || Props.pedestalDef == null)
            {
                return;
            }
            int placed = 0;
            int blocked = 0;
            List<IntVec3> cells = PedestalCells();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(map))
                {
                    blocked++;
                    continue;
                }
                if (HasPedestalAt(cell) || HasPedestalBlueprintAt(cell))
                {
                    continue;
                }
                AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                    Props.pedestalDef, cell, Rot4.North, map, false, null, null);
                if (!report.Accepted)
                {
                    blocked++;
                    continue;
                }
                GenConstruct.PlaceBlueprintForBuild(
                    Props.pedestalDef, cell, map, Rot4.North, Faction.OfPlayer, null);
                placed++;
            }
            if (placed > 0)
            {
                Messages.Message("Botania.TerraAltarPlacedPedestals".Translate(placed),
                    parent, MessageTypeDefOf.TaskCompletion, false);
            }
            if (blocked > 0)
            {
                Messages.Message("Botania.TerraAltarPedestalsBlocked".Translate(blocked),
                    parent, MessageTypeDefOf.RejectInput, false);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref progress, "botaniaTerraAltarProgress", 0);
            Scribe_Values.Look(ref running, "botaniaTerraAltarRunning", false);
        }
    }
}
