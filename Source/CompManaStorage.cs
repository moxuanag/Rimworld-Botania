using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力储存（魔力池）。魔力是独立于原版电力网的资源，只以浮点数存在于此 Comp 中。
    /// </summary>
    public class CompProperties_ManaStorage : CompProperties
    {
        /// <summary>最大魔力储量。</summary>
        public float maxMana = 100000f;

        /// <summary>建成/生成时的初始魔力。</summary>
        public float initialMana = 0f;

        /// <summary>是否接受其他来源（功能花、相邻魔力池）输入魔力。</summary>
        public bool acceptsMana = true;

        /// <summary>是否与相邻魔力池自动均摊魔力。</summary>
        public bool balanceWithAdjacentPools = true;

        public CompProperties_ManaStorage()
        {
            compClass = typeof(CompManaStorage);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (maxMana <= 0f)
            {
                yield return "maxMana must be greater than 0.";
            }
            if (initialMana < 0f)
            {
                yield return "initialMana must not be negative.";
            }
        }
    }

    public class CompManaStorage : ThingComp, IManaSource
    {
        private const float BalanceEpsilon = 1f;

        /// <summary>均摊结算间隔（tick）。250 tick ≈ 4.2 秒。</summary>
        private const int BalanceIntervalTicks = 250;

        private float mana;

        private int balanceCountdown = BalanceIntervalTicks;

        public CompProperties_ManaStorage Props => (CompProperties_ManaStorage)props;

        public float Mana => mana;

        /// <summary>IManaSource：对设备与中继塔而言，池子就是魔力源。</summary>
        public float ManaAmount => mana;

        public float MaxMana => Props.maxMana;

        public float FreeSpace => Math.Max(0f, Props.maxMana - mana);

        public bool IsFull => FreeSpace <= 0.01f;

        public bool AcceptsMana => Props.acceptsMana;

        public float ManaPercent => Props.maxMana <= 0f ? 0f : mana / Props.maxMana;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && mana <= 0f)
            {
                mana = Clamp(Props.initialMana);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mana, "mana", 0f);
        }

        /// <summary>尝试加入魔力，返回实际被接受的数量。</summary>
        public float AddMana(float amount)
        {
            if (!Props.acceptsMana || amount <= 0f)
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

        /// <summary>尝试抽取魔力，返回实际抽到的数量。</summary>
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

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            return "Botania.ManaStored".Translate(mana.ToString("F0"), MaxMana.ToString("F0"));
        }

        /// <summary>开发者工具：快速灌满/清空/加魔力，省得为了测数值去搭一堆花。</summary>
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
                defaultLabel = "灌满魔力",
                defaultDesc = "开发者工具：把魔力补到上限。",
                action = () => mana = Props.maxMana
            };
            yield return new Command_Action
            {
                defaultLabel = "+10000 魔力",
                defaultDesc = "开发者工具：加一万魔力。",
                action = () => AddMana(10000f)
            };
            yield return new Command_Action
            {
                defaultLabel = "清空魔力",
                defaultDesc = "开发者工具：把魔力归零。",
                action = () => mana = 0f
            };
        }

        public override void CompTick()
        {
            base.CompTick();
            // 魔力池的 tickerType 是 Normal，引擎只会调 CompTick，不会调 CompTickRare，
            // 所以均摊必须挂在这里，用计数器攒够 250 tick 再结算（实机踩过：挂在
            // CompTickRare 上导致均摊从来没生效过）。
            if (!Props.balanceWithAdjacentPools || !parent.Spawned)
            {
                return;
            }
            if (--balanceCountdown > 0)
            {
                return;
            }
            balanceCountdown = BalanceIntervalTicks;
            BalanceWithNeighbors();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            // 若日后把 tickerType 改成 Rare，这条路径同样有效
            if (Props.balanceWithAdjacentPools && parent.Spawned)
            {
                BalanceWithNeighbors();
            }
        }

        /// <summary>与相邻（含对角）的魔力池均摊魔力，避免出现一池满一池空的尴尬。</summary>
        private void BalanceWithNeighbors()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            // 用「占用格向外扩一格」找邻居：魔力池是 3x3，按原点算半径会漏掉紧贴的池子
            HashSet<CompManaStorage> seen = new HashSet<CompManaStorage>();
            foreach (IntVec3 cell in parent.OccupiedRect().ExpandedBy(1))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    CompManaStorage other = things[i].TryGetComp<CompManaStorage>();
                    if (other == null || other == this || !other.Props.balanceWithAdjacentPools)
                    {
                        continue;
                    }
                    if (!other.AcceptsMana || other.Mana >= mana - BalanceEpsilon)
                    {
                        continue;
                    }
                    if (!seen.Add(other))
                    {
                        continue;
                    }
                    float wanted = (mana - other.Mana) * 0.5f;
                    float moved = other.AddMana(wanted);
                    if (moved > 0f)
                    {
                        DrainMana(moved);
                    }
                }
            }
        }

        private float Clamp(float value)
        {
            return Math.Clamp(value, 0f, Props.maxMana);
        }
    }
}
