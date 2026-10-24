using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉刃的剑气：一条 12 格的直线冲击，伤害从 30 递减到 10，**不耗魔力**，只吃冷却。
    ///
    /// 2026-10-24 玩家定：原先扣「附近魔力源」400 魔力，但装备态没有魔力条可以看，
    /// 玩家只会觉得「按下去没反应」。与其加一条要额外解释的 UI，不如照原作 Terra Blade
    /// 的手感——挥出去就有，只受 8 秒冷却限制。
    ///
    /// 结构照现有的 Ability_WandSpell（原版灵能套路）：
    /// abilityClass 指向本类，verbProperties 用原版 Verb_CastAbility，
    /// **不自定义 Verb**（自定义 Verb 拿不到 ability 引用，会让 OnGUI 每帧 NRE）。
    ///
    /// 冷却 8 秒写在 AbilityDef 的 cooldownTicksRange（480）。
    /// 技能由 MapComponent_WandState 根据「手里有没有泰拉刃」授予/收回，
    /// 所以卸下武器后这个技能会自动消失，不需要自己维护 UI。
    /// </summary>
    public class Ability_TerraWave : Ability
    {
        private const int MaxCells = 12;
        private const float StartDamage = 30f;
        private const float MinDamage = 10f;
        private const float ArmorPen = 0.45f;

        public Ability_TerraWave(Pawn pawn) : base(pawn)
        {
        }

        public Ability_TerraWave(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
        }

        private CompTerraBlade FindBlade()
        {
            Pawn caster = pawn;
            if (caster == null || caster.equipment == null)
            {
                return null;
            }
            return caster.equipment.Primary?.GetComp<CompTerraBlade>();
        }

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = pawn;
            CompTerraBlade blade = FindBlade();
            if (caster == null || blade == null || caster.Map == null)
            {
                return false;
            }
            if (!target.IsValid)
            {
                return false;
            }

            Map map = caster.Map;
            IntVec3 origin = caster.Position;
            IntVec3 aim = target.Cell;
            if (!aim.InBounds(map))
            {
                return false;
            }

            float range = def?.verbProperties?.range ?? MaxCells;
            float dist = origin.DistanceTo(aim);
            if (dist < 0.6f)
            {
                Messages.Message("Botania.TerraWaveNoTarget".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (dist > range)
            {
                Messages.Message("Botania.TerraWaveOutOfRange".Translate(range.ToString("F0")),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            StrikeLine(caster, map, origin, aim);

            base.Activate(target, dest);
            return true;
        }

        /// <summary>沿原点→目标方向铺一条 12 格直线，逐格结算伤害。</summary>
        private void StrikeLine(Pawn caster, Map map, IntVec3 origin, IntVec3 aim)
        {
            Vector3 dir = (aim.ToVector3Shifted() - origin.ToVector3Shifted()).normalized;
            HashSet<IntVec3> done = new HashSet<IntVec3>();
            ThingDef weaponDef = caster.equipment?.Primary?.def;

            for (int step = 1; step <= MaxCells; step++)
            {
                IntVec3 cell = origin + new IntVec3(
                    Mathf.RoundToInt(dir.x * step), 0, Mathf.RoundToInt(dir.z * step));
                if (!cell.InBounds(map) || !done.Add(cell))
                {
                    continue;
                }
                float t = (step - 1) / (float)(MaxCells - 1);
                float damage = Mathf.Lerp(StartDamage, MinDamage, t);

                // 表现：一路灰尘 + 电光
                FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 1.2f);
                FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), map, 0.6f);

                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || thing == caster)
                    {
                        continue;
                    }
                    if (thing is Mote)
                    {
                        continue;
                    }
                    if (!thing.def.useHitPoints)
                    {
                        continue;
                    }
                    // 自家建筑与友军不吃伤害：这是补刀技能，不是误伤制造机
                    if (thing.Faction != null && thing.Faction == caster.Faction)
                    {
                        continue;
                    }
                    thing.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, ArmorPen,
                        -1f, caster, null, weaponDef));
                }
            }
        }
    }
}
