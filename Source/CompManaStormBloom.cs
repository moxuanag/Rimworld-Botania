using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 电暴花：监听电网短路（Zzztt），把电池爆炸释放的能量按比例转成魔力存进自己的小缓存。
    /// 缓存刻意做得小——它是「捡漏」，不是稳定电源。
    ///
    /// 框架迁移（0.8.4 S1.5）：找池子 / 缓冲 / 结算节流 / 存档 / 提示文案交给 <see cref="CompManaProducer"/>，
    /// 本文件只保留「瓦日能量 → 魔力」的换算与短路吸收半径。
    /// </summary>
    public class CompProperties_ManaStormBloom : CompProperties_ManaProducer
    {
        /// <summary>花自己的缓存上限（魔力）。</summary>
        public float bufferCapacity = 2400f;

        /// <summary>1 瓦日电池能量换多少魔力。</summary>
        public float manaPerWattDay = 200f;

        public CompProperties_ManaStormBloom()
        {
            compClass = typeof(CompManaStormBloom);

            // 基类配置：只喂魔力池、每秒结算一次、旧存档里叫 manaBuffer
            radius = 6f;
            settleIntervalTicks = 60;
            feedRelays = false;
            saveKeyPrefix = "botaniaStormBloom";
            legacyBufferKey = "manaBuffer";
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (bufferCapacity <= 0f)
            {
                yield return "bufferCapacity must be greater than 0.";
            }
            if (manaPerWattDay <= 0f)
            {
                yield return "manaPerWattDay must be greater than 0.";
            }
        }
    }

    public class CompManaStormBloom : CompManaProducer
    {
        /// <summary>
        /// 短路发生时，这个半径内的花才会分到能量。
        /// 改这里必须同步改 Def 上的 <c>CompProperties_RadiusRing</c>（放置预览与选中环靠它画）。
        /// </summary>
        public const float AbsorbRadius = 15f;

        /// <summary>
        /// 原版 <c>ShortCircuitUtility.DoShortCircuit</c> 的放电门槛：
        /// 只有 <c>StoredEnergy &gt; 20</c> 的电池才会被抽干，否则它只会试着点火。
        /// 调试按钮用同一阈值挑电池，保证触发的是「真实放电」而不是「点火」。
        /// </summary>
        private const float RealDischargeBatteryThreshold = 20f;

        public CompProperties_ManaStormBloom Props => (CompProperties_ManaStormBloom)props;

        /// <summary>吃掉若干瓦日的能量，返回实际吃下的瓦日数。</summary>
        public float AbsorbEnergy(float wattDays)
        {
            if (wattDays <= 0f || FreeBufferSpace <= 0f)
            {
                return 0f;
            }
            float usedMana = base.AbsorbIntoBuffer(wattDays * Props.manaPerWattDay);
            return usedMana / Props.manaPerWattDay;
        }

        /// <summary>电暴花不自己产能，只靠吸收短路能量进缓存。</summary>
        protected override bool WantsProduce()
        {
            return false;
        }

        /// <summary>
        /// 开发调试按钮（仅开发者模式）：在吸收半径内触发一次<b>真实</b>的电网短路。
        ///
        /// 走的是与 <c>IncidentWorker_ShortCircuit</c> 完全相同的引擎入口
        /// <c>ShortCircuitUtility.DoShortCircuit(culprit)</c>：由引擎自己抽干电池、按真实储能
        /// 计算并引爆爆炸，<see cref="BotaniaHarmony"/> 的两个 Harmony 补丁再从 <c>PowerNet</c>
        /// 的真实储能换算出瓦日分给电暴花。
        ///
        /// 因此本按钮<b>不</b>直接调用 <see cref="AbsorbEnergy"/>：瓦日数完全来自附近电池的
        /// 真实电量（<c>PowerNet.CurrentStoredEnergy()</c>），吸收也只在补丁的 Postfix 里发生。
        /// 注意：这是真实短路——会抽干电池并造成火焰/爆炸伤害，请用小电网测试。
        /// </summary>
        private void DebugTriggerRealShortCircuit()
        {
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }
            PowerNet net;
            Building culprit = FindShortCircuitCulprit(map, out net);
            if (culprit == null || net == null)
            {
                Messages.Message(
                    "Botania.Dev.StormShortCircuitNoBattery".Translate(AbsorbRadius.ToString("0")),
                    new LookTargets(parent),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }
            // 放电前的真实储能（= Harmony 前缀从 PowerNet 抓到的同一个值，也就是瓦日数）
            float wattDays = net.CurrentStoredEnergy();
            float bufferBefore = Buffer;
            // ← 真实事件入口：抽干电池 + 爆炸；吸收由 BotaniaHarmony 的前缀/后缀完成
            ShortCircuitUtility.DoShortCircuit(culprit);
            float gained = Math.Max(0f, Buffer - bufferBefore);
            Messages.Message(
                "Botania.Dev.StormShortCircuitDone".Translate(wattDays.ToString("0"), gained.ToString("0")),
                new LookTargets(parent),
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        /// <summary>
        /// 找「真实的短路 culprit」：优先选吸收半径内、同电网上会被原版短路抽干的电线
        /// （原版事件的 culprit 一定是 <c>PowerConduit</c>）；没有电线时退回半径内的带电电池本身
        /// （<c>DoShortCircuit</c> 只要求 Building 有可用的 <c>CompPower</c>，参数语义不变）。
        /// 选中后 <paramref name="net"/> 就是 Harmony 补丁读取真实储能的那个电网。
        /// </summary>
        private Building FindShortCircuitCulprit(Map map, out PowerNet net)
        {
            net = null;
            Building nearestConduit = null;
            int bestDistance = int.MaxValue;
            Building chargedBattery = null;
            // RadialDistinctThingsAround 本来就由近到远，第一个命中的就是最近的
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, AbsorbRadius, true))
            {
                Building building = thing as Building;
                if (building == null)
                {
                    continue;
                }
                CompPower power = building.PowerComp;
                if (power == null || power.PowerNet == null)
                {
                    continue;
                }
                if (chargedBattery == null)
                {
                    CompPowerBattery battery = building.TryGetComp<CompPowerBattery>();
                    if (battery != null && battery.StoredEnergy > RealDischargeBatteryThreshold)
                    {
                        chargedBattery = building;
                    }
                }
                if (building.def != ThingDefOf.PowerConduit || !NetWillDischarge(power.PowerNet))
                {
                    continue;
                }
                int distance = parent.Position.DistanceToSquared(building.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearestConduit = building;
                }
            }
            if (nearestConduit != null)
            {
                net = nearestConduit.PowerComp.PowerNet;
                return nearestConduit;
            }
            if (chargedBattery != null)
            {
                net = chargedBattery.PowerComp.PowerNet;
                return chargedBattery;
            }
            return null;
        }

        /// <summary>与 <c>ShortCircuitUtility.DoShortCircuit</c> 内部判断完全一致：电网里有电池储能 &gt; 20 才会放电。</summary>
        private static bool NetWillDischarge(PowerNet net)
        {
            if (net == null)
            {
                return false;
            }
            List<CompPowerBattery> batteries = net.batteryComps;
            for (int i = 0; i < batteries.Count; i++)
            {
                if (batteries[i] != null && batteries[i].StoredEnergy > RealDischargeBatteryThreshold)
                {
                    return true;
                }
            }
            return false;
        }

        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (!Prefs.DevMode)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.StormShortCircuit".Translate(),
                defaultDesc = "Botania.Dev.StormShortCircuitDesc".Translate(),
                action = DebugTriggerRealShortCircuit
            };
        }

        /// <summary>提示文案沿用旧版：缓存 / 上限 / 吸收半径。</summary>
        protected override string ExtraInspectString()
        {
            return "Botania.StormBloomStored".Translate(
                Buffer.ToString("0"),
                Props.bufferCapacity.ToString("0"),
                AbsorbRadius.ToString("0"));
        }
    }
}
