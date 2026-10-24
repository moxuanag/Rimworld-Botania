using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「播种」设计器：在肉床上点一格 → 消耗 1 枚血肉之种 → 在该格放一个活细胞（种子）。
    /// 玩家就是用这个把起始图案一格一格摆出来的。
    /// 只在装了异象 DLC 时可见（肉床本身就来自 DLC 补丁）。
    /// </summary>
    public class Designator_SowFleshCell : Designator
    {
        public Designator_SowFleshCell()
        {
            defaultLabel = "Botania.SowCell".Translate();
            defaultDesc = "Botania.SowCellDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("Things/Item/Botania/FleshSeed", false);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Claim;
            useMouseIcon = true;
        }

        public override bool Visible
        {
            get { return ModsConfig.AnomalyActive; }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            if (!loc.InBounds(Map))
            {
                return false;
            }
            TerrainDef terrain = loc.GetTerrain(Map);
            if (terrain == null || terrain.defName != CompManaLifeOrchid.BedDefName)
            {
                return "Botania.SowCellNeedBed".Translate();
            }
            if (HasSeedAt(loc))
            {
                return "Botania.SowCellOccupied".Translate();
            }
            if (FindSeed() == null)
            {
                return "Botania.SowCellNoSeed".Translate();
            }
            return true;
        }

        /// <summary>支持拖拽成片播种：拖过的每格各消耗一枚种子。</summary>
        public override void DesignateMultiCell(System.Collections.Generic.IEnumerable<IntVec3> cells)
        {
            bool any = false;
            foreach (IntVec3 cell in cells)
            {
                if (!CanDesignateCell(cell).Accepted)
                {
                    continue;
                }
                Thing seed = TakeOneSeed();
                if (seed == null)
                {
                    break;
                }
                GenSpawn.Spawn(seed, cell, Map);
                any = true;
            }
            if (!any)
            {
                return;
            }
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            Thing seed = TakeOneSeed();
            if (seed == null)
            {
                return;
            }
            GenSpawn.Spawn(seed, c, Map);
        }

        private Thing FirstSeedOf(ThingDef def)
        {
            System.Collections.Generic.List<Thing> list = Map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Spawned)
                {
                    return list[i];
                }
            }
            return null;
        }

        private bool HasSeedAt(IntVec3 cell)
        {
            System.Collections.Generic.List<Thing> things = cell.GetThingList(Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def != null && things[i].def.defName == CompManaLifeOrchid.SeedDefName)
                {
                    return true;
                }
            }
            return false;
        }

        private Thing FindSeed()
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(CompManaLifeOrchid.SeedDefName, false);
            if (def == null)
            {
                return null;
            }
            return FirstSeedOf(def);
        }

        private Thing TakeOneSeed()
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(CompManaLifeOrchid.SeedDefName, false);
            if (def == null)
            {
                return null;
            }
            Thing source = FirstSeedOf(def);
            if (source == null)
            {
                return null;
            }
            if (source.stackCount <= 1)
            {
                source.DeSpawn();
                return source;
            }
            return source.SplitOff(1);
        }
    }
}
