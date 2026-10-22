using System;
using HarmonyLib;
using RimWorld;
using Verse;

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
    /// 电暴花的数据来源：电网短路（Zzztt）时会 drain 掉电池里的能量并炸一下。
    /// 我们在短路发生前后各看一眼：前面记下电网上存了多少瓦日，后面按比例分给附近的花。
    /// </summary>
    [HarmonyPatch(typeof(ShortCircuitUtility), nameof(ShortCircuitUtility.DoShortCircuit))]
    public static class Patch_ShortCircuit
    {
        /// <summary>短路前的电网储能（瓦日）。</summary>
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
                if (power == null || power.PowerNet == null)
                {
                    return;
                }
                __state = power.PowerNet.CurrentStoredEnergy();
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] short circuit prefix failed: " + ex.Message);
                __state = 0f;
            }
        }

        /// <summary>短路后把能量分给半径内的电暴花，花自己的缓存是上限。</summary>
        public static void Postfix(Building culprit, float __state)
        {
            try
            {
                if (culprit == null || culprit.Map == null || __state <= 0f)
                {
                    return;
                }

                float remainingWattDays = __state;
                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(culprit.Position, culprit.Map, 15f, true))
                {
                    CompManaStormBloom bloom = thing.TryGetComp<CompManaStormBloom>();
                    if (bloom == null)
                    {
                        continue;
                    }
                    float used = bloom.AbsorbEnergy(remainingWattDays);
                    remainingWattDays -= used;
                    if (remainingWattDays <= 0f)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Botania] short circuit postfix failed: " + ex.Message);
            }
        }
    }
}
