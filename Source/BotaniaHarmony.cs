using System.Collections.Generic;
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
    /// 地图组件不能用 XML 注册（MapComponentDef 不是合法的 Def 类型），
    /// 只能在 Map.FillComponents 之后把自己塞进 map.components。
    /// 启命英的「血肉核心种子计时」靠这个时钟推进。
    /// </summary>
    [HarmonyPatch(typeof(Map), "FillComponents")]
    public static class Patch_MapFillComponents
    {
        public static void Postfix(Map __instance)
        {
            try
            {
                __instance.components.Add(new MapComponent_BotaniaCores(__instance));
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] map component hook failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 苦丽花的取食点：直接监听技能经验（<c>Pawn_SkillTracker.Learn</c>）。
    /// 任何算得上劳动的行为都会涨经验，因此不需要按 job / 账单去猜「哪一刻算完成」——
    /// 之前挂 EndCurrentJob 漏掉账单活、挂 MakeRecipeProducts 又依赖产出路径，
    /// 都不如经验本身稳。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.Learn))]
    public static class Patch_SkillLearned
    {
        public static void Prefix(Pawn_SkillTracker __instance, object[] __args)
        {
            try
            {
                if (__instance == null || __args == null || __args.Length < 2)
                {
                    return;
                }
                SkillDef skillDef = __args[0] as SkillDef;
                float xp = Convert.ToSingle(__args[1]);
                if (skillDef == null || xp <= 0f)
                {
                    return;
                }
                SkillRecord record = __instance.GetSkill(skillDef);
                Pawn pawn = record != null ? record.Pawn : null;
                if (pawn == null)
                {
                    return;
                }
                CompManaBitterBloom.RecordXp(pawn, xp);
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] skill learn hook failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 事件来源的心灵干扰（没有实体，纯剧情事件）不做「全免」，而是把心情惩罚减半——
    /// 否则玩家会以为这朵花没生效、是个 bug。
    /// 心灵干扰的念头是 situational 类型，没有重写 MoodOffset，所以补丁挂在基类上，
    /// 再按 defName 过滤，不会波及其他念头。
    /// </summary>
    [HarmonyPatch(typeof(Thought), nameof(Thought.MoodOffset))]
    public static class Patch_PsychicDroneMood
    {
        public static void Postfix(Thought __instance, ref float __result)
        {
            try
            {
                if (__result >= 0f || __instance == null || __instance.def == null)
                {
                    return;
                }
                if (__instance.def.defName != "PsychicDrone")
                {
                    return;
                }
                if (CompManaClearMindLotus.PartialShield(__instance.pawn))
                {
                    __result *= 0.5f;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] psychic drone mood hook failed: " + ex.Message);
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
                // 先记录「这个小人确实被干扰影响到了」——计费只按这群人算，
                // 性别不符、心灵迟钝（0 敏感）的人不花钱。必须在我们改写结果之前记。
                CompManaClearMindLotus.NotifyAffected(p);
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
