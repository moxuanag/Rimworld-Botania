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
