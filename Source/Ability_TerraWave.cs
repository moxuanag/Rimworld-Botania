using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉刃的剑气：一条 48 格长、**3 格宽**的直线冲击，伤害从 30 递减到 10，
    /// **不耗魔力**，只吃冷却。
    ///
    /// 2026-10-24 玩家改定两件事：
    /// 1. 取消耗魔（装备态没有魔力条可看，只会觉得「按下去没反应」）；
    /// 2. 只对**自己人和自家建筑**免伤，中立/敌对的人、动物、建筑都要吃伤害；
    ///    瞄准时不再显示原版蓝色准星，改画「射程圈 + 剑气扫过的格」（见 `Verb_TerraWave`）。
    ///
    /// 技能由 MapComponent_WandState 根据「手里有没有泰拉刃」授予/收回，
    /// 所以卸下武器后这个技能会自动消失，不需要自己维护 UI。
    /// </summary>
    public class Ability_TerraWave : Ability
    {
        private const float ArmorPen = 0.45f;

        public Ability_TerraWave(Pawn pawn) : base(pawn)
        {
            LinkVerb();
        }

        public Ability_TerraWave(Pawn pawn, AbilityDef def) : base(pawn, def)
        {
            LinkVerb();
        }

        /// <summary>
        /// 引擎不会把 ability 挂到自定义 verb 上（原版是 Psycast 构造函数挂的）。
        /// 不补这一下，`Verb_CastAbility.OnGUI` / `DrawAttachmentExtraLabel` 每帧 NRE，
        /// 表现是**点技能后整个 UI 消失**。
        /// </summary>
        private void LinkVerb()
        {
            if (verb is Verb_CastAbility cast && cast.ability != this)
            {
                cast.ability = this;
            }
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
            LinkVerb();
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

            float range = def?.verbProperties?.range ?? TerraWavePath.MaxCells;
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

        /// <summary>沿原点→目标方向铺一条直线，逐格结算伤害。</summary>
        private void StrikeLine(Pawn caster, Map map, IntVec3 origin, IntVec3 aim)
        {
            ThingDef weaponDef = caster.equipment?.Primary?.def;
            List<WaveCell> cells = TerraWavePath.Build(origin, aim, map);
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i].cell;
                float damage = TerraWavePath.DamageAt(cells[i].step);

                // 表现：一路灰尘 + 电光
                FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 1.2f);
                FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), map, 0.6f);

                List<Thing> things = cell.GetThingList(map);
                for (int k = things.Count - 1; k >= 0; k--)
                {
                    Thing thing = things[k];
                    if (!CanHit(caster, thing))
                    {
                        continue;
                    }
                    thing.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, ArmorPen,
                        -1f, caster, null, weaponDef));
                }
            }
        }

        /// <summary>
        /// 命中判定：**只放过自己人与自家建筑**，中立/敌对的人、动物、建筑全吃伤害。
        ///
        /// 旧写法是「`thing.Faction == caster.Faction` 就跳过」，逻辑上中立动物（Faction 为 null）
        /// 本该命中，但实测有漏；现在改成白名单——只有「玩家阵营的东西」免伤，其余一律结算，
        /// 少一层靠猜的分支。
        /// </summary>
        private static bool CanHit(Pawn caster, Thing thing)
        {
            if (thing == null || thing.Destroyed || thing == caster)
            {
                return false;
            }
            if (thing is Mote || thing is Filth || thing is Projectile)
            {
                return false;
            }
            if (!thing.def.useHitPoints)
            {
                return false;
            }
            // 自家阵营（自己人、自家动物、自家建筑）免伤
            Faction faction = thing.Faction;
            if (faction != null && caster.Faction != null && faction == caster.Faction)
            {
                return false;
            }
            // 只打「活物」和「建筑」：地上的物品、石块不参与结算
            ThingCategory category = thing.def.category;
            if (category != ThingCategory.Pawn && category != ThingCategory.Building)
            {
                return false;
            }
            if (thing is Pawn p && p.Dead)
            {
                return false;
            }
            return true;
        }
    }
}
