using LudeonTK;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 【临时调试工具，验完删掉整个文件】
    /// 开发者模式 → Botania (temp) 下三个动作，用来快速搭启命英的试验场：
    ///   1) 铺 11×11 肉床（以鼠标格为中心）
    ///   2) 放一朵启命英 + 10 枚血肉之种
    ///   3) 打印当前局面（活细胞数、燃料、代数）
    /// </summary>
    public static class DebugToolsLifeOrchid
    {
        [DebugAction("Botania (temp)", "铺 11×11 肉床", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LayFleshBed()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            TerrainDef bed = DefDatabase<TerrainDef>.GetNamed("Botania_FleshBed", false);
            if (bed == null)
            {
                Messages.Message("找不到肉床 TerrainDef（异象 DLC 没开？）", MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 center = UI.MouseCell();
            int half = 5;
            int count = 0;
            for (int dz = -half; dz <= half; dz++)
            {
                for (int dx = -half; dx <= half; dx++)
                {
                    IntVec3 cell = center + new IntVec3(dx, 0, dz);
                    if (cell.InBounds(map))
                    {
                        map.terrainGrid.SetTerrain(cell, bed);
                        count++;
                    }
                }
            }
            Messages.Message("已铺肉床 " + count + " 格", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Botania (temp)", "放启命英 + 种子", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnOrchid()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            ThingDef orchid = DefDatabase<ThingDef>.GetNamed("Botania_LifeOrchid", false);
            ThingDef seed = DefDatabase<ThingDef>.GetNamed("Botania_FleshSeed", false);
            if (orchid == null || seed == null)
            {
                Messages.Message("找不到启命英或血肉之种 Def", MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                cell = map.Center;
            }
            if (!cell.Standable(map))
            {
                cell = CellFinder.StandableCellNear(cell, map, 6f);
            }
            GenSpawn.Spawn(orchid, cell, map);

            // 种子现在就是「活细胞」：直接撒在肉床上，摆一个随机初始图案
            TerrainDef bed = DefDatabase<TerrainDef>.GetNamed("Botania_FleshBed", false);
            int placed = 0;
            System.Random random = new System.Random();
            for (int attempt = 0; attempt < 600 && placed < 24; attempt++)
            {
                IntVec3 target = cell + new IntVec3(random.Next(-5, 6), 0, random.Next(-5, 6));
                if (!target.InBounds(map))
                {
                    continue;
                }
                TerrainDef terrain = target.GetTerrain(map);
                if (bed != null && terrain != bed)
                {
                    continue;
                }
                Thing one = ThingMaker.MakeThing(seed, null);
                GenSpawn.Spawn(one, target, map);
                placed++;
            }
            Messages.Message("已放启命英 + " + placed + " 个细胞（血肉之种）", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Botania (temp)", "召唤血肉心脏（测试用）", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnFleshmassHeart()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            if (!ModsConfig.AnomalyActive)
            {
                Messages.Message("需要异象 DLC", MessageTypeDefOf.RejectInput, false);
                return;
            }
            ThingDef heart = DefDatabase<ThingDef>.GetNamed("FleshmassHeart", false);
            if (heart == null)
            {
                Messages.Message("找不到 FleshmassHeart", MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                cell = map.Center;
            }
            // 3x3 占地：找一块能放下的地方
            if (!GenSpawn.WouldWipeAnythingWith(cell, Rot4.North, heart, map, (Thing t) => true))
            {
                GenSpawn.Spawn(heart, cell, map, Rot4.North);
                Messages.Message("已召唤血肉心脏——选中它看「抽取血肉之种」按钮", MessageTypeDefOf.TaskCompletion, false);
                return;
            }
            Messages.Message("这一格放不下（3×3 需要空地）", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Botania (temp)", "诊断：启命英", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Diagnose()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            int found = 0;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                CompManaLifeOrchid comp = thing.TryGetComp<CompManaLifeOrchid>();
                if (comp == null)
                {
                    continue;
                }
                found++;
                Log.Message("[Botania/诊断] 启命英 @" + thing.Position + " " + comp.DebugLine());
            }
            Log.Message("[Botania/诊断] 启命英数量=" + found + "；AnomalyActive=" + ModsConfig.AnomalyActive);
        }
    }
}
