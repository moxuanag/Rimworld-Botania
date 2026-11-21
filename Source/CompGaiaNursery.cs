using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 世界之种苗圃：把范围内的泰拉钢装备「培育」成盖亚装备。
    ///
    /// 用最低成本的方式实现装备转化：
    ///  · 地上的装备：直接替换成目标物品（保留位置）
    ///  · 穿在殖民者身上的装备：脱下来销毁，再穿上新的
    /// 转化清单写在 Def 里（conversions），以后加新装备不用改代码。
    /// </summary>
    public class CompProperties_GaiaNursery : CompProperties
    {
        /// <summary>转化范围半径（格）。</summary>
        public float radius = 8f;

        /// <summary>转化间隔（tick）。300 = 5 秒转化一件。</summary>
        public int intervalTicks = 300;

        /// <summary>转化清单：from -> to。</summary>
        public List<NurseryConversion> conversions = new List<NurseryConversion>();

        public CompProperties_GaiaNursery()
        {
            compClass = typeof(CompGaiaNursery);
        }
    }

    public class NurseryConversion
    {
        public string from;
        public string to;
    }

    public class CompGaiaNursery : ThingComp
    {
        private int nextConvertTick = -1;

        private CompProperties_GaiaNursery Props => (CompProperties_GaiaNursery)props;

        public float Radius => Props.radius;

        public override void CompTick()
        {
            base.CompTick();
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (nextConvertTick < 0)
            {
                nextConvertTick = now + Props.intervalTicks;
                return;
            }
            if (now < nextConvertTick)
            {
                return;
            }
            nextConvertTick = now + Props.intervalTicks;

            if (TryConvertWornApparel(map))
            {
                return;
            }
            TryConvertGroundItem(map);
        }

        /// <summary>优先转化穿在殖民者身上的（玩家更在意这个）。</summary>
        private bool TryConvertWornApparel(Map map)
        {
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.apparel == null)
                {
                    continue;
                }
                if (!InRange(pawn.Position))
                {
                    continue;
                }
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel apparel = worn[j];
                    if (apparel == null)
                    {
                        continue;
                    }
                    string toDef = TargetFor(apparel.def.defName);
                    if (toDef.NullOrEmpty())
                    {
                        continue;
                    }
                    ThingDef to = DefDatabase<ThingDef>.GetNamedSilentFail(toDef);
                    if (to == null)
                    {
                        Log.Warning("[Botania] 苗圃找不到转化目标: " + toDef);
                        continue;
                    }

                    QualityCategory quality = QualityCategory.Normal;
                    bool hasQuality = apparel.TryGetQuality(out quality);

                    pawn.apparel.Remove(apparel);
                    apparel.Destroy(DestroyMode.Vanish);

                    Apparel newApparel = (Apparel)ThingMaker.MakeThing(to);
                    if (hasQuality && newApparel.TryGetComp<CompQuality>() != null)
                    {
                        newApparel.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Colony);
                    }
                    pawn.apparel.Wear(newApparel, false);

                    Messages.Message("Botania.GaiaNursery.ConvertedWorn".Translate(
                            apparel.def.label, to.label, pawn.LabelShort),
                        new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                    return true;
                }
            }
            return false;
        }

        /// <summary>地上/仓库里的装备。</summary>
        private void TryConvertGroundItem(Map map)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned || thing.def == null)
                    {
                        continue;
                    }
                    string toDef = TargetFor(thing.def.defName);
                    if (toDef.NullOrEmpty())
                    {
                        continue;
                    }
                    ThingDef to = DefDatabase<ThingDef>.GetNamedSilentFail(toDef);
                    if (to == null)
                    {
                        continue;
                    }

                    QualityCategory quality = QualityCategory.Normal;
                    bool hasQuality = thing.TryGetQuality(out quality);

                    IntVec3 pos = thing.Position;
                    Map m = thing.Map;
                    thing.Destroy(DestroyMode.Vanish);

                    Thing newThing = ThingMaker.MakeThing(to);
                    if (hasQuality && newThing.TryGetComp<CompQuality>() != null)
                    {
                        newThing.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Colony);
                    }
                    GenSpawn.Spawn(newThing, pos, m);

                    Messages.Message("Botania.GaiaNursery.ConvertedGround".Translate(
                            to.label),
                        new LookTargets(pos, m), MessageTypeDefOf.PositiveEvent, false);
                    return;
                }
            }
        }

        private bool InRange(IntVec3 cell)
        {
            return cell.DistanceTo(parent.Position) <= Props.radius;
        }

        private string TargetFor(string defName)
        {
            if (Props.conversions == null)
            {
                return null;
            }
            for (int i = 0; i < Props.conversions.Count; i++)
            {
                NurseryConversion c = Props.conversions[i];
                if (c != null && c.from == defName)
                {
                    return c.to;
                }
            }
            return null;
        }

        public override string CompInspectStringExtra()
        {
            return "Botania.GaiaNursery.Inspect".Translate(Props.radius.ToString("0"));
        }
    }
}
