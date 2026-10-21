using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    public class CompProperties_ManaRepairer : CompProperties
    {
        /// <summary>修复半径（格）。</summary>
        public float radius = 8f;

        /// <summary>向魔力网取魔的半径。</summary>
        public float manaSearchRadius = 6f;

        /// <summary>多少 tick 一个周期，默认 60（1 秒）。</summary>
        public int ticksPerCycle = 60;

        /// <summary>每个目标每周期最多修复多少点耐久。</summary>
        public float hpPerCycle = 5f;

        /// <summary>每修复 1 点耐久消耗的魔力。</summary>
        public float manaPerHp = 2f;

        /// <summary>一个周期最多同时照顾几个目标（最破的优先）。</summary>
        public int maxTargets = 4;

        /// <summary>修建筑吗。</summary>
        public bool repairBuildings = true;

        /// <summary>修地上的物品（武器/服装/工具）吗。</summary>
        public bool repairItems;

        public CompProperties_ManaRepairer()
        {
            compClass = typeof(CompManaRepairer);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (radius <= 0f)
            {
                yield return "radius must be greater than 0.";
            }
            if (ticksPerCycle <= 0)
            {
                yield return "ticksPerCycle must be greater than 0.";
            }
            if (hpPerCycle <= 0f)
            {
                yield return "hpPerCycle must be greater than 0.";
            }
            if (manaPerHp < 0f)
            {
                yield return "manaPerHp must not be negative.";
            }
            if (maxTargets <= 0)
            {
                yield return "maxTargets must be greater than 0.";
            }
            if (!repairBuildings && !repairItems)
            {
                yield return "neither repairBuildings nor repairItems is enabled, the building would do nothing.";
            }
        }
    }

    /// <summary>
    /// 用魔力修复范围内的耐久。
    /// 同一套逻辑挂两个建筑上：建筑恢复器（repairBuildings）与装备修复器（repairItems）。
    /// 每个周期按「最破的优先」挑目标，按实际抽到的魔力决定修多少——
    /// 魔力不够就修一点，不硬撑；完全没有魔力就不动，也不空转扣费。
    /// </summary>
    public class CompManaRepairer : ThingComp
    {
        private int ticksAccum;

        private bool enabled = true;

        /// <summary>上一周期实际修复的总耐久（只用于检视显示）。</summary>
        private float lastRepaired;

        public CompProperties_ManaRepairer Props => (CompProperties_ManaRepairer)props;

        public bool Enabled => enabled;

        private Map Map => parent.Map;

        private bool CanRun => enabled && parent.Spawned && Map != null;

        public override void CompTick()
        {
            // 关掉时也要走 tick，否则玩家点了开关没反应
            if (!CanRun)
            {
                return;
            }
            ticksAccum++;
            if (ticksAccum < Props.ticksPerCycle)
            {
                return;
            }
            ticksAccum = 0;
            RunCycle();
        }

        private void RunCycle()
        {
            lastRepaired = 0f;

            List<Thing> candidates = FindCandidates();
            if (candidates.Count == 0)
            {
                return;
            }

            // 目标越破越优先，免得火力分散在满血的墙上
            candidates.Sort((a, b) => Frac(a).CompareTo(Frac(b)));

            int count = Mathf.Min(candidates.Count, Props.maxTargets);

            // 先算出「想修多少」，再按实际能抽到的魔力打折
            float wantedHp = 0f;
            for (int i = 0; i < count; i++)
            {
                wantedHp += Mathf.Min(Props.hpPerCycle, candidates[i].MaxHitPoints - candidates[i].HitPoints);
            }
            if (wantedHp <= 0.01f)
            {
                return;
            }

            float wantedMana = wantedHp * Props.manaPerHp;
            float available = TakeMana(wantedMana);
            if (available <= 0.01f)
            {
                return;
            }

            float hpBudget = Props.manaPerHp > 0f ? available / Props.manaPerHp : wantedHp;
            hpBudget = Mathf.Min(hpBudget, wantedHp);

            // 把预算按目标平均分，最后一点零头给第一个
            float perTarget = hpBudget / count;
            for (int i = 0; i < count; i++)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int missing = thing.MaxHitPoints - thing.HitPoints;
                int heal = Mathf.Clamp(Mathf.RoundToInt(perTarget), 0, missing);
                if (heal <= 0)
                {
                    continue;
                }
                thing.HitPoints += heal;
                lastRepaired += heal;
            }
        }

        private static float Frac(Thing thing)
        {
            int max = thing.MaxHitPoints;
            if (max <= 0)
            {
                return 1f;
            }
            return (float)thing.HitPoints / max;
        }

        private List<Thing> FindCandidates()
        {
            List<Thing> result = new List<Thing>();
            Map map = Map;
            if (map == null)
            {
                return result;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.radius, true))
            {
                if (thing == parent || thing.Destroyed)
                {
                    continue;
                }
                if (!thing.def.useHitPoints || thing.MaxHitPoints <= 0)
                {
                    continue;
                }
                if (thing.HitPoints >= thing.MaxHitPoints)
                {
                    continue;
                }
                // 蓝图、框架、正在施工的东西不该被"修"
                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    continue;
                }
                if (thing.def.category == ThingCategory.Building)
                {
                    if (!Props.repairBuildings)
                    {
                        continue;
                    }
                    // 只修自己阵营的（中立/敌对建筑不管）
                    if (thing.Faction != null && thing.Faction != Faction.OfPlayer)
                    {
                        continue;
                    }
                }
                else if (thing.def.category == ThingCategory.Item)
                {
                    if (!Props.repairItems)
                    {
                        continue;
                    }
                }
                else
                {
                    // 人、尸体、植物等一律不碰
                    continue;
                }
                result.Add(thing);
            }
            return result;
        }

        /// <summary>向最近的魔力源抽 mana，返回实际抽到的量。</summary>
        private float TakeMana(float want)
        {
            Map map = Map;
            if (map == null || want <= 0f)
            {
                return 0f;
            }
            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, map, Props.manaSearchRadius, 1f, parent);
            if (source == null)
            {
                return 0f;
            }
            return source.DrainMana(want);
        }

        public override string CompInspectStringExtra()
        {
            if (!enabled)
            {
                return "Botania.RepairerOff".Translate();
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("Botania.RepairerWorking".Translate(
                Props.radius.ToString("F0"),
                (Props.hpPerCycle * Props.maxTargets).ToString("F0"),
                Props.manaPerHp.ToString("F1")));
            if (lastRepaired > 0.01f)
            {
                sb.Append("\n");
                sb.Append("Botania.RepairerLast".Translate(lastRepaired.ToString("F0")));
            }
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!parent.Spawned)
            {
                yield break;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "Botania.RepairerToggle".Translate(),
                defaultDesc = "Botania.RepairerToggleDesc".Translate(),
                isActive = () => enabled,
                toggleAction = () =>
                {
                    enabled = !enabled;
                    ticksAccum = 0;
                    lastRepaired = 0f;
                }
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "manaRepairerEnabled", true);
        }
    }
}
