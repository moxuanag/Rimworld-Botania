using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力中继塔：把魔力池的供给范围向外延伸。
    ///
    /// 工作方式（"水往低处流"模型，不做网络图）：
    ///  1. 自身带一个小缓冲；
    ///  2. 每 0.5 秒从半径内"比自己魔力多"的源抽一次（池子或上游中继塔），
    ///     抽到的量按 lossPercent 损耗后再入缓冲；
    ///  3. 设备通过 IManaSource 就近找源时也会找到塔，于是塔成了"可被抽取的临时池子"。
    ///
    /// 级联：塔 A 靠池子、塔 B 靠塔 A…… 每多一级损耗一次，因此远程供能是有代价的。
    /// </summary>
    public class CompProperties_ManaRelay : CompProperties
    {
        /// <summary>自身缓冲容量。</summary>
        public float capacity = 3000f;

        /// <summary>抽取半径（格）。</summary>
        public float radius = 6f;

        /// <summary>每秒最多搬运多少魔力（损耗前）。</summary>
        public float manaPerSecond = 10f;

        /// <summary>每经过一跳的损耗比例（0.1 = 10%）。</summary>
        public float lossPercent = 0.1f;

        public CompProperties_ManaRelay()
        {
            compClass = typeof(CompManaRelay);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (capacity <= 0f)
            {
                yield return "capacity must be greater than 0.";
            }
            if (radius <= 0f)
            {
                yield return "radius must be greater than 0.";
            }
            if (manaPerSecond <= 0f)
            {
                yield return "manaPerSecond must be greater than 0.";
            }
            if (lossPercent < 0f || lossPercent >= 1f)
            {
                yield return "lossPercent must be within [0, 1).";
            }
        }
    }

    public class CompManaRelay : ThingComp, IManaSource
    {
        private const int TransferIntervalTicks = 30;

        /// <summary>防止浮点抖动导致来回抽。</summary>
        private const float LevelEpsilon = 1f;

        private float mana;
        private int ticksAccum;

        public CompProperties_ManaRelay Props => (CompProperties_ManaRelay)props;

        public float ManaAmount => mana;

        public float FreeSpace => Math.Max(0f, Props.capacity - mana);

        public float ManaPercent => Props.capacity <= 0f ? 0f : mana / Props.capacity;

        public float AddMana(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            float accepted = Math.Min(amount, FreeSpace);
            if (accepted <= 0f)
            {
                return 0f;
            }
            mana += accepted;
            return accepted;
        }

        public float DrainMana(float amount)
        {
            if (amount <= 0f || mana <= 0f)
            {
                return 0f;
            }
            float drained = Math.Min(amount, mana);
            mana -= drained;
            return drained;
        }

        public override void CompTick()
        {
            base.CompTick();
            // 塔的 tickerType 必须是 Normal，否则这里不会被调用（本项目踩过这个坑）
            if (!parent.Spawned)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < TransferIntervalTicks)
            {
                return;
            }
            ticksAccum = 0;
            PullFromUpstream();
        }

        private void PullFromUpstream()
        {
            if (FreeSpace <= 0.01f)
            {
                return;
            }
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            float perInterval = Props.manaPerSecond * TransferIntervalTicks / 60f;
            float wanted = Math.Min(perInterval, FreeSpace);
            if (wanted <= 0f)
            {
                return;
            }

            // 只从"魔力比自己多"的源抽取：这样两座同级塔不会互抽，
            // 整条链路天然从池子向远离池子的方向流动。
            IManaSource upstream = ManaSourceUtil.FindNearest(
                parent.Position, map, Props.radius, mana + LevelEpsilon, parent);
            if (upstream == null)
            {
                return;
            }

            float got = upstream.DrainMana(wanted);
            if (got <= 0f)
            {
                return;
            }
            AddMana(got * (1f - Props.lossPercent));
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            if (mana <= 0.01f)
            {
                return "Botania.RelayEmpty".Translate();
            }
            return "Botania.RelayStored".Translate(
                mana.ToString("F0"),
                Props.capacity.ToString("F0"),
                (Props.lossPercent * 100f).ToString("F0"));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
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
                defaultLabel = "灌满中继",
                defaultDesc = "开发者工具：把中继缓冲补满。",
                action = () => mana = Props.capacity
            };
            yield return new Command_Action
            {
                defaultLabel = "清空中继",
                defaultDesc = "开发者工具：把中继缓冲清零。",
                action = () => mana = 0f
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mana, "mana", 0f);
        }
    }
}
