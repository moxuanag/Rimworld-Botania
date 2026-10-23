using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>打补丁的入口：启动时一次性挂上所有 Harmony 补丁。</summary>
    [StaticConstructorOnStartup]
    public static class BotaniaHarmony
    {
        static BotaniaHarmony()
        {
            try
            {
                new HarmonyLib.Harmony("gz.botania").PatchAll();
            }
            catch (Exception ex)
            {
                Log.Error("[Botania] Harmony patch failed: " + ex);
            }
        }
    }

    /// <summary>
    /// 电暴花的取能点：把短路事件放电的瓦日数分给附近的电暴花。
    ///
    /// 挂两处，避免事件入口换实现时漏掉：
    ///   1) <c>ShortCircuitUtility.DrainBatteriesAndCauseExplosion</c> —— 真正把电池抽干的地方，参数里直接有 PowerNet；
    ///   2) <c>ShortCircuitUtility.DoShortCircuit</c> —— 事件入口，作为兜底。
    /// 同一次事件两处都会命中，用一个「同 culprit + 同 tick」的去重挡住重复吸取。
    /// </summary>
    public static class ShortCircuitAbsorb
    {
        private const float AbsorbRadius = CompManaStormBloom.AbsorbRadius;

        private static Building lastCulprit;
        private static int lastTick = -999;

        /// <summary>把一次短路的能量分给附近的电暴花，返回实际分出去的瓦日数。</summary>
        public static float Distribute(Building culprit, float wattDays)
        {
            if (culprit == null || culprit.Map == null || wattDays <= 0f)
            {
                return 0f;
            }

            int tick = Find.TickManager.TicksGame;
            if (culprit == lastCulprit && tick - lastTick <= 5)
            {
                return 0f;
            }
            lastCulprit = culprit;
            lastTick = tick;

            float remaining = wattDays;
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(culprit.Position, culprit.Map, AbsorbRadius, true))
            {
                CompManaStormBloom bloom = thing.TryGetComp<CompManaStormBloom>();
                if (bloom == null)
                {
                    continue;
                }
                float used = bloom.AbsorbEnergy(remaining);
                remaining -= used;
                if (remaining <= 0f)
                {
                    break;
                }
            }
            return wattDays - remaining;
        }
    }

    /// <summary>真正抽干电池的地方：参数里直接带 PowerNet，最可靠。</summary>
    [HarmonyPatch(typeof(ShortCircuitUtility), "DrainBatteriesAndCauseExplosion")]
    public static class Patch_DrainBatteriesAndCauseExplosion
    {
        public static void Prefix(object[] __args, out float __state)
        {
            __state = 0f;
            try
            {
                PowerNet net = __args != null && __args.Length > 0 ? __args[0] as PowerNet : null;
                if (net != null)
                {
                    __state = net.CurrentStoredEnergy();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] short circuit prefix failed: " + ex.Message);
            }
        }

        public static void Postfix(object[] __args, float __state)
        {
            try
            {
                Building culprit = __args != null && __args.Length > 1 ? __args[1] as Building : null;
                ShortCircuitAbsorb.Distribute(culprit, __state);
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] short circuit postfix failed: " + ex.Message);
            }
        }
    }

    /// <summary>事件入口兜底：有些路径可能直接走这里。</summary>
    [HarmonyPatch(typeof(ShortCircuitUtility), nameof(ShortCircuitUtility.DoShortCircuit))]
    public static class Patch_DoShortCircuit
    {
        public static void Prefix(Building culprit, out float __state)
        {
            __state = 0f;
            try
            {
                if (culprit == null || culprit.Map == null)
                {
                    return;
                }
                CompPower power = culprit.GetComp<CompPower>();
                if (power != null && power.PowerNet != null)
                {
                    __state = power.PowerNet.CurrentStoredEnergy();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] DoShortCircuit prefix failed: " + ex.Message);
            }
        }

        public static void Postfix(Building culprit, float __state)
        {
            try
            {
                ShortCircuitAbsorb.Distribute(culprit, __state);
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] DoShortCircuit postfix failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 苦丽花的取食点：小人每完成一件「真正的工作」（有 workGiverDef 的作业，
    /// 走路、等待这类不算），范围内的苦丽花各收一份魔力。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_JobCompleted
    {
        public static void Prefix(Pawn_JobTracker __instance, out Job __state)
        {
            __state = __instance != null ? __instance.curJob : null;
        }

        public static void Postfix(Pawn_JobTracker __instance, Job __state, JobCondition condition)
        {
            try
            {
                if (condition != JobCondition.Succeeded || __state == null)
                {
                    return;
                }
                // 「完成一件工作」的判定：
                //  1) 有 workGiverDef —— 采矿、搬运、种植这类由 WorkGiver 派下来的活；
                //  2) 有 bill —— 工作台/屠宰这类账单活，实测它们的 workGiverDef 可能是 null，
                //     只认 workGiverDef 会漏掉「做手工」这种最典型的工作；
                //  3) defName 兜底 DoBill。
                bool isWork = __state.workGiverDef != null
                              || __state.bill != null
                              || (__state.def != null && __state.def.defName == "DoBill");
                if (!isWork)
                {
                    return;
                }
                // pawn 字段是私有的，用 Traverse 取，别去改访问修饰符
                Pawn pawn = Traverse.Create(__instance).Field<Pawn>("pawn").Value;
                CompManaBitterBloom.OnWorkCompleted(pawn);
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] job completed hook failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 明心莲的挡罩：心灵干扰是一条 situational thought，
    /// 每帧都会重算，所以只能从源头把结果改掉——只压这一条，不碰别的情绪。
    /// </summary>
    [HarmonyPatch(typeof(ThoughtWorker_PsychicDrone), "CurrentStateInternal")]
    public static class Patch_PsychicDrone
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            try
            {
                if (!__result.Active)
                {
                    return;
                }
                if (CompManaClearMindLotus.IsShielded(p))
                {
                    __result = ThoughtState.Inactive;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] psychic drone hook failed: " + ex.Message);
            }
        }
    }
}
