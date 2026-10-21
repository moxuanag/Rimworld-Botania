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
        public float manaPerHp = 5f;

        /// <summary>默认上限（玩家可在模组设置里改，或直接关掉上限）。</summary>
        public int maxTargets = 4;

        /// <summary>修建筑吗。</summary>
        public bool repairBuildings = true;

        /// <summary>修地上的物品（武器/服装/工具）吗。</summary>
        public bool repairItems;

        /// <summary>开关按钮的图标。</summary>
        public string toggleIcon = "UI/Icons/Botania/Repair";

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
    /// 建筑恢复器（repairBuildings）与装备修复器（repairItems）共用这套逻辑。
    ///
    /// 一个周期的账：
    ///   目标数 = min(范围内待修的东西, 上限)      ← 上限可在模组设置里关掉
    ///   每目标修补 hpPerCycle 点
    ///   总耗魔 = 目标数 × hpPerCycle × manaPerHp   ← 多修一个目标就多一份钱
    /// 魔力不够就按比例少修，抽不到则完全不动。
    /// </summary>
    public class CompManaRepairer : ThingComp
    {
        private int ticksAccum;

        private bool enabled = true;

        /// <summary>上一周期实际修复的总耐久（仅用于检视显示）。</summary>
        private float lastRepaired;

        private int lastTargets;

        private float lastManaSpent;

        public CompProperties_ManaRepairer Props => (CompProperties_ManaRepairer)props;

        public bool Enabled => enabled;

        private Map Map => parent.Map;

        /// <summary>生效的上限：设置里关掉上限就是"多少都修"。</summary>
        private int EffectiveLimit
        {
            get
            {
                BotaniaModSettings settings = BotaniaModSettings.Instance;
                if (settings != null && !settings.limitRepairTargets)
                {
                    return int.MaxValue;
                }
                int fromSettings = settings?.maxRepairTargets ?? Props.maxTargets;
                return Mathf.Max(1, fromSettings);
            }
        }

        public override void CompTick()
        {
            if (!enabled || !parent.Spawned || Map == null)
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
            lastTargets = 0;
            lastManaSpent = 0f;

            List<Thing> candidates = FindCandidates();
            if (candidates.Count == 0)
            {
                return;
            }

            // 越破越优先，免得火力分散在满血的墙上
            candidates.Sort((a, b) => Frac(a).CompareTo(Frac(b)));

            int count = Mathf.Min(candidates.Count, EffectiveLimit);

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

            // 一分钱一分货：抽到多少就修多少
            float hpBudget = Props.manaPerHp > 0f ? available / Props.manaPerHp : wantedHp;
            hpBudget = Mathf.Min(hpBudget, wantedHp);
            if (hpBudget <= 0.01f)
            {
                return;
            }

            lastManaSpent = available;

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
                lastTargets++;
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
                // 尸体防着点：它也是 Item，但修尸体毫无意义
                if (thing is Corpse || thing.def.IsCorpse)
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
                    // 人、植物等一律不碰
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
            int limit = EffectiveLimit;
            string limitText = limit == int.MaxValue
                ? "Botania.RepairerLimitUnlimited".Translate()
                : limit.ToString();
            sb.Append("Botania.RepairerWorking".Translate(
                Props.radius.ToString("F0"),
                limitText,
                Props.hpPerCycle.ToString("F0"),
                Props.manaPerHp.ToString("F1")));
            if (lastRepaired > 0.01f)
            {
                sb.Append("\n");
                sb.Append("Botania.RepairerLast".Translate(
                    lastRepaired.ToString("F0"),
                    lastTargets.ToString(),
                    lastManaSpent.ToString("F0")));
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
                icon = ContentFinder<Texture2D>.Get(Props.toggleIcon, false) ?? BaseContent.BadTex,
                isActive = () => enabled,
                toggleAction = () =>
                {
                    enabled = !enabled;
                    ticksAccum = 0;
                    lastRepaired = 0f;
                    lastTargets = 0;
                    lastManaSpent = 0f;
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
