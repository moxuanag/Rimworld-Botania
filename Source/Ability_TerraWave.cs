using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 泰拉刃的剑气：一条 48 格长、**3 格宽**的直线冲击，伤害从 30 递减到 10，
    /// 吃 8 秒冷却，**每剑抽 400 魔力**（从刃周围 8 格内的魔力池里整笔扣）。
    ///
    /// 2026-10-24 定的两条：
    /// 1. 只对**自己人和自家建筑**免伤，中立/敌对的人、动物、建筑都要吃伤害；
    ///    瞄准时不再显示原版蓝色准星，改画「射程圈 + 剑气扫过的格」（见 `Verb_TerraWave`）。
    /// 2. 旧版是「不耗魔力」，现在改回付费：价码与半径都写在
    ///    <see cref="CompProperties_TerraBlade"/>（waveManaCost = 400，取魔走所在格的魔力网络）里，
    ///    取魔与扣款走物品付款框架 <see cref="ManaItemCore"/>（= 就近找魔力池 + 凑够才扣）。
    ///
    /// 技能由 MapComponent_WandState 根据「手里有没有泰拉刃」授予/收回，
    /// 所以卸下武器后这个技能会自动消失，不需要自己维护 UI。
    ///
    /// 魔力不足时的表现（三条都是引擎现成路径，不自己画 UI）：
    ///   ① <see cref="CanCast"/> 返回失败理由 → `Command_Ability.DisabledCheck` 把 gizmo 置灰；
    ///   ② 同一条理由进入 `Gizmo.disabledReason` → 悬停按钮时红字显示在 tooltip 里；
    ///   ③ 置灰状态下点一下 → 引擎自己弹 RejectInput 消息（"…：需要 400，可用 X"）。
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

        /// <summary>
        /// 施放前校验：8 格内至少要有一个**够付 400** 的魔力池。
        ///
        /// 为什么写在 CanCast 而不是自己画 gizmo：引擎的 `Command_Ability.DisabledCheck`
        /// 就是 `disabled = ability.GizmoDisabled(out reason)`，而 `Ability.GizmoDisabled`
        /// 内部会 `callvirt get_CanCast()`，把这里返回的理由原样塞进 `Gizmo.disabledReason`。
        /// 所以只要覆盖 CanCast，置灰 / tooltip 红字 / 点击弹 RejectInput 三条全自动成立。
        ///
        /// 顺序很重要：先让 base 判（冷却、充能、倒地、未征召……），base 不通过时
        /// 原样返回它的理由——不能用「魔力不足」盖住「还在冷却」这种真正原因。
        /// </summary>
        public override AcceptanceReport CanCast
        {
            get
            {
                AcceptanceReport baseReport = base.CanCast;
                if (!baseReport.Accepted)
                {
                    return baseReport;
                }
                CompTerraBlade blade = FindBlade();
                // 没拿刃 / 不在图上（商队等）：交给 base 的结论，不额外置灰
                if (blade == null || pawn == null || pawn.Map == null)
                {
                    return baseReport;
                }
                // 这个 getter 每帧都会被 gizmo 绘制问到：异常绝不能冒到 UIRootOnGUI
                // （本仓库为「点技能后整个 UI 消失」返工过一次）。出错就退回 base 的结论。
                try
                {
                    if (blade.CanPayWave(out float available))
                    {
                        return baseReport;
                    }
                    return "Botania.WandNoMana".Translate(blade.WaveManaCost.ToString("F0"),
                        available.ToString("F0"));
                }
                catch (System.Exception e)
                {
                    Log.ErrorOnce("[Botania] Ability_TerraWave.CanCast 出错：" + e, 194712);
                    return baseReport;
                }
            }
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

            // 目标与距离都合法了才付款：打空/超程不白扣魔力。
            // 真扣款（凑够才扣）也在这里再做一次——从按下按钮到热身结束有 0.6 秒，
            // 期间池子可能被别的设备抽走，不能只信按钮亮着。
            if (!TrySpendWaveMana(blade))
            {
                return false;
            }

            StrikeLine(caster, map, origin, aim);

            base.Activate(target, dest);
            return true;
        }

        /// <summary>
        /// 出剑前把 400 魔力整笔扣掉。扣不起就一点不抽、也不出剑，
        /// 并把与 UI 相同的提示翻出来（放在 Activate 里是因为
        /// gizmo 置灰只能挡住「点按钮」，挡不住热身期间变穷）。
        /// </summary>
        private static bool TrySpendWaveMana(CompTerraBlade blade)
        {
            if (blade.TryPayWave())
            {
                return true;
            }
            float cost = blade.WaveManaCost;
            blade.ProbeWaveMana(out float available);
            Messages.Message("Botania.WandNoMana".Translate(cost.ToString("F0"), available.ToString("F0")),
                MessageTypeDefOf.RejectInput, false);
            return false;
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
