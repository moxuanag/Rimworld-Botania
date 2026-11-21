using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 开发者工具：生成本模组添加的武器与护甲。
    ///
    /// 入口在开发者菜单的「Botania / 植物魔法」页 下，
    /// 文字全部走 Translate()，便于 Dev In Your Language 之类的翻译模组接管。
    /// </summary>
    public static class DebugActions_BotaniaGear
    {
        [DebugAction("Botania", "生成植物魔法装备", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 0)]
        private static void OpenGearSpawner()
        {
            Find.WindowStack.Add(new Dialog_DevSpawnBotaniaGear());
        }
    }

    /// <summary>
    /// 装备生成窗口：列出本模组的全部武器与护甲，点一下就在鼠标位置生成。
    /// </summary>
    public class Dialog_DevSpawnBotaniaGear : Window
    {
        private const string PackageId = "gz.botania";

        private Vector2 scroll;
        private readonly List<ThingDef> defs = new List<ThingDef>();

        public override Vector2 InitialSize => new Vector2(560f, 640f);

        public Dialog_DevSpawnBotaniaGear()
        {
            doCloseX = true;
            doCloseButton = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def == null || def.modContentPack == null)
                {
                    continue;
                }
                if (!string.Equals(def.modContentPack.PackageId, PackageId, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!def.IsWeapon && !def.IsApparel)
                {
                    continue;
                }
                defs.Add(def);
            }
            defs.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, System.StringComparison.CurrentCulture));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Botania.DevGear.Title".Translate());
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 34f, inRect.width, 22f), "Botania.DevGear.Hint".Translate());

            Rect outRect = new Rect(0f, 60f, inRect.width, inRect.height - 60f);
            float rowHeight = 30f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, defs.Count * rowHeight + 8f);

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 4f;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                Rect row = new Rect(0f, y, viewRect.width, rowHeight - 2f);

                if (i % 2 == 0)
                {
                    Widgets.DrawLightHighlight(row);
                }

                string kind = def.IsApparel
                    ? "Botania.DevGear.Apparel".Translate()
                    : "Botania.DevGear.Weapon".Translate();

                Rect labelRect = new Rect(row.x + 6f, row.y, row.width - 150f, row.height);
                Widgets.Label(labelRect, def.LabelCap);

                Rect kindRect = new Rect(row.xMax - 140f, row.y, 130f, row.height);
                GUI.color = new Color(1f, 1f, 1f, 0.65f);
                Widgets.Label(kindRect, kind);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row))
                {
                    SpawnOne(def);
                }

                y += rowHeight;
            }
            Widgets.EndScrollView();

            if (defs.Count == 0)
            {
                Widgets.Label(new Rect(0f, outRect.y + 8f, outRect.width, 24f),
                    "Botania.DevGear.Empty".Translate());
            }
        }

        private static void SpawnOne(ThingDef def)
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
