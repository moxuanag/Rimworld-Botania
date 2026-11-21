using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 开发者工具：生成本模组的武器与护甲。
    ///
    /// 做法参考原版「Spawn item」：
    ///   1. 点工具 -> 弹出物品列表（本模组的武器/护甲）
    ///   2. 选中一项后进入「工具模式」，鼠标左键在地图上点击的位置生成该物品
    ///
    /// 文字全部走 Translate()/DefInjected，便于 Dev In Your Language 之类的翻译模组接管。
    /// </summary>
    public static class DebugActions_BotaniaGear
    {
        private const string PackageId = "gz.botania";

        [DebugAction("Botania", "生成植物魔法装备", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 0)]
        private static void SpawnBotaniaGear()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (ThingDef def in GearDefs())
            {
                ThingDef captured = def;
                options.Add(new DebugMenuOption(captured.LabelCap, DebugMenuOptionMode.Tool,
                    delegate { SpawnAt(captured); }));
            }

            if (options.Count == 0)
            {
                Messages.Message("Botania.DevGear.Empty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(
                options, "Botania.DevGear.Title".Translate()));
        }

        /// <summary>本模组的武器与护甲（按中文名排序）。</summary>
        private static List<ThingDef> GearDefs()
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
                if (!def.IsWeapon && !def.IsApparel)
                {
                    continue;
                }
                result.Add(def);
            }
            result.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            return result;
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
            Messages.Message("Botania.DevGear.Spawned".Translate(def.LabelCap),
                new LookTargets(cell, map), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
