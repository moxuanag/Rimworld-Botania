using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 开发者工具：生成本模组的建筑（产魔花 / 功能花 / 设备 / 祭坛等），
    /// 以及「一键测试场景」——直接把建筑和一个**满魔力**的魔力池摆在一起，
    /// 省掉手动画魔力池、调魔力的时间。
    ///
    /// 分类依据是组件类型（不是手写清单），以后新增同类内容会自动出现在对应分类里：
    ///   · 产魔：带 <see cref="CompManaProducer"/> 的建筑
    ///   · 耗魔：带 <see cref="CompManaDevice"/> 的建筑
    ///   · 其它：本模组剩余的可建造物（魔力池 / 祭坛 / 门户 / 苗圃…）
    ///
    /// 文字全部走 Translate()，方便 Dev In Your Language 之类的翻译模组接管。
    /// </summary>
    public static class DebugActions_BotaniaBuildings
    {
        private const string PackageId = "gz.botania";
        private const string ManaPoolDefName = "Botania_ManaPool";
        private const float TestPoolMana = 100000f;

        // ---------- 1) 普通生成 ----------

        [DebugAction("Botania", "生成植物魔法建筑", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 1)]
        private static void SpawnBotaniaBuilding()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (ThingDef def in BuildingDefs())
            {
                ThingDef captured = def;
                options.Add(new DebugMenuOption(
                    CategoryPrefix(captured) + captured.LabelCap,
                    DebugMenuOptionMode.Tool,
                    delegate { SpawnAt(captured); }));
            }

            if (options.Count == 0)
            {
                Messages.Message("Botania.DevBld.Empty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(
                options, "Botania.DevBld.Title".Translate()));
        }

        // ---------- 2) 一键测试场景：建筑 + 满魔力池 ----------

        [DebugAction("Botania", "生成测试场景（建筑+满魔力池）", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 2)]
        private static void SpawnTestScene()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (ThingDef def in BuildingDefs())
            {
                // 魔力池自己不需要再配一个池子
                if (def.HasComp(typeof(CompManaStorage)))
                {
                    continue;
                }
                ThingDef captured = def;
                options.Add(new DebugMenuOption(
                    CategoryPrefix(captured) + captured.LabelCap,
                    DebugMenuOptionMode.Tool,
                    delegate { SpawnSceneAt(captured); }));
            }

            if (options.Count == 0)
            {
                Messages.Message("Botania.DevBld.Empty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(
                options, "Botania.DevBld.SceneTitle".Translate()));
        }

        // ---------- 3) 魔力池：注满 / 抽干 ----------

        [DebugAction("Botania", "魔力池：注满", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 3)]
        private static void FillManaPool(Pawn pawn)
        {
            CompManaStorage pool = PoolAtMouse();
            if (pool == null)
            {
                Messages.Message("Botania.DevBld.PoolNotFound".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            pool.AddMana(pool.MaxMana);
            Messages.Message("Botania.DevBld.PoolFilled".Translate(pool.Mana.ToString("0")),
                new LookTargets(pool.parent), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Botania", "魔力池：抽干", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 4)]
        private static void DrainManaPool(Pawn pawn)
        {
            CompManaStorage pool = PoolAtMouse();
            if (pool == null)
            {
                Messages.Message("Botania.DevBld.PoolNotFound".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            pool.DrainMana(pool.MaxMana);
            Messages.Message("Botania.DevBld.PoolDrained".Translate(pool.Mana.ToString("0")),
                new LookTargets(pool.parent), MessageTypeDefOf.NeutralEvent, false);
        }

        // ---------- 内部 ----------

        /// <summary>本模组所有可建造的 ThingDef（按分类 + 中文名排序）。</summary>
        private static List<ThingDef> BuildingDefs()
        {
            List<ThingDef> result = new List<ThingDef>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def == null || def.modContentPack == null)
                {
                    continue;
                }
                if (!string.Equals(def.modContentPack.PackageId, PackageId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (def.category != ThingCategory.Building)
                {
                    continue;
                }
                // 只列真正有 building 数据的建筑
                if (def.building == null)
                {
                    continue;
                }
                result.Add(def);
            }
            result.Sort((a, b) =>
            {
                int cmp = string.Compare(CategoryPrefix(a), CategoryPrefix(b), StringComparison.CurrentCulture);
                if (cmp != 0)
                {
                    return cmp;
                }
                return string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture);
            });
            return result;
        }

        private static string CategoryPrefix(ThingDef def)
        {
            if (def.HasComp(typeof(CompManaProducer)))
            {
                return "[" + "Botania.DevBld.Cat.Produce".Translate() + "] ";
            }
            if (def.HasComp(typeof(CompManaDevice)))
            {
                return "[" + "Botania.DevBld.Cat.Consume".Translate() + "] ";
            }
            return "[" + "Botania.DevBld.Cat.Other".Translate() + "] ";
        }

        private static void SpawnAt(ThingDef def)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            if (thing == null)
            {
                return;
            }
            GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            Messages.Message("Botania.DevBld.Spawned".Translate(def.LabelCap),
                new LookTargets(cell, map), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>生成建筑，并在旁边放一个满魔力的魔力池（2 格内优先）。</summary>
        private static void SpawnSceneAt(ThingDef def)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                return;
            }

            Thing building = ThingMaker.MakeThing(def);
            if (building == null || !GenPlace.TryPlaceThing(building, cell, map, ThingPlaceMode.Near))
            {
                Messages.Message("Botania.DevBld.Spawned".Translate(def.LabelCap),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            ThingDef poolDef = DefDatabase<ThingDef>.GetNamedSilentFail(ManaPoolDefName);
            if (poolDef != null)
            {
                // 就近找一格能放的，放魔力池并注满
                foreach (IntVec3 c in GenAdjFast.AdjacentCells8Way(building.Position))
                {
                    if (!c.InBounds(map) || !c.Walkable(map) || c.GetFirstThing<Thing>(map) != null)
                    {
                        continue;
                    }
                    Thing poolThing = ThingMaker.MakeThing(poolDef);
                    if (!GenPlace.TryPlaceThing(poolThing, c, map, ThingPlaceMode.Near))
                    {
                        continue;
                    }
                    CompManaStorage pool = poolThing.TryGetComp<CompManaStorage>();
                    if (pool != null)
                    {
                        pool.AddMana(pool.MaxMana);
                    }
                    break;
                }
            }

            Messages.Message("Botania.DevBld.SceneSpawned".Translate(def.LabelCap),
                new LookTargets(building), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>鼠标所在的魔力池（点不到就取最近的）。</summary>
        private static CompManaStorage PoolAtMouse()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return null;
            }
            IntVec3 cell = UI.MouseCell();
            if (cell.InBounds(map))
            {
                Thing direct = cell.GetFirstThing<Thing>(map);
                CompManaStorage pool = direct?.TryGetComp<CompManaStorage>();
                if (pool != null)
                {
                    return pool;
                }
            }
            // 退路：找最近的魔力池
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                CompManaStorage pool = thing.TryGetComp<CompManaStorage>();
                if (pool != null)
                {
                    return pool;
                }
            }
            return null;
        }
    }
}
