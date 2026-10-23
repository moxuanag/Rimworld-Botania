using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 【临时调试工具，验完删掉整个文件】
    ///
    /// 开发者模式下的两个动作，用来在实机里造出两种心灵干扰：
    ///   1) 召唤心灵干扰器（PsychicDronerShipPart）—— 有实体的那种，测「全额挡住」；
    ///   2) 触发心灵低语事件（PsychicDrone）—— 无实体的事件，测「惩罚减半」。
    ///
    /// 位置：开发者模式 → 顶部某一栏的「Botania (temp)」分类。
    /// 删除方式：删掉本文件并重新编译即可，不涉及任何 Def。
    /// </summary>
    public static class DebugToolsPsychic
    {
        [DebugAction("Botania (temp)", "召唤心灵干扰器", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnPsychicDroner()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            ThingDef def = DefDatabase<ThingDef>.GetNamed("PsychicDronerShipPart", false);
            if (def == null)
            {
                Log.Warning("[Botania] 找不到 ThingDef PsychicDronerShipPart。");
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
            GenSpawn.Spawn(def, cell, map);
            Messages.Message("已召唤心灵干扰器（测试用）", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Botania (temp)", "诊断：心灵干扰", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Diagnose()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            Log.Message("[Botania/诊断] ==== 开始 ====");
            Log.Message("[Botania/诊断] 补丁命中次数(PatchHits)=" + CompManaClearMindLotus.PatchHits);
            Log.Message("[Botania/诊断] 场上是否有干扰器建筑=" + CompManaClearMindLotus.HasDroneBuilding(map));

            // 地图条件
            try
            {
                var conditions = map.gameConditionManager.ActiveConditions;
                Log.Message("[Botania/诊断] 活动条件数=" + (conditions == null ? -1 : conditions.Count));
                if (conditions != null)
                {
                    for (int i = 0; i < conditions.Count; i++)
                    {
                        GameCondition c = conditions[i];
                        string causer = c.conditionCauser == null ? "null" : c.conditionCauser.def.defName;
                        Log.Message("[Botania/诊断]   条件 " + (c.def == null ? "?" : c.def.defName) + " causer=" + causer);
                    }
                }
                GameCondition drone = map.gameConditionManager.GetActiveCondition(GameConditionDefOf.PsychicDrone);
                Log.Message("[Botania/诊断] GetActiveCondition(PsychicDrone)=" + (drone == null ? "null" : drone.def.defName));
            }
            catch (System.Exception ex)
            {
                Log.Warning("[Botania/诊断] 读取条件失败: " + ex.Message);
            }

            // 花
            Log.Message("[Botania/诊断] 明心莲数量=" + CompManaClearMindLotus.All.Count);
            for (int i = 0; i < CompManaClearMindLotus.All.Count; i++)
            {
                CompManaClearMindLotus lotus = CompManaClearMindLotus.All[i];
                if (lotus == null || lotus.parent == null)
                {
                    continue;
                }
                Log.Message(string.Format(
                    "[Botania/诊断]   莲#{0} 位置={1} 计费中={2} 受影响人数={3}",
                    i, lotus.parent.Position, lotus.IsPaying, lotus.AffectedCount));
            }

            // 小人
            var pawns = map.mapPawns.FreeColonistsAndPrisoners;
            List<Thought> thoughts = new List<Thought>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.needs == null || pawn.needs.mood == null)
                {
                    continue;
                }
                thoughts.Clear();
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
                int droneCount = 0;
                for (int t = 0; t < thoughts.Count; t++)
                {
                    if (thoughts[t].def != null && thoughts[t].def.defName == "PsychicDrone")
                    {
                        droneCount++;
                    }
                }
                Log.Message(string.Format(
                    "[Botania/诊断]   小人 {0} 性别={1} 敏感={2:0.##} 干扰念头数={3}",
                    pawn.LabelShortCap, pawn.gender, pawn.GetStatValue(StatDefOf.PsychicSensitivity), droneCount));
            }
            Log.Message("[Botania/诊断] ==== 结束 ====");
        }

        [DebugAction("Botania (temp)", "触发心灵低语事件", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FirePsychicDrone()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("PsychicDrone", false);
            if (def == null || def.Worker == null)
            {
                Log.Warning("[Botania] 找不到 IncidentDef PsychicDrone。");
                return;
            }

            IncidentParms parms = new IncidentParms();
            parms.target = map;
            parms.forced = true;
            parms.points = StorytellerUtility.DefaultThreatPointsNow(map);
            bool fired = def.Worker.TryExecute(parms);
            Messages.Message(
                fired ? "已触发心灵低语事件（测试用）" : "心灵低语事件没能触发（可能被互斥条件挡住）",
                MessageTypeDefOf.TaskCompletion,
                false);
        }
    }
}
