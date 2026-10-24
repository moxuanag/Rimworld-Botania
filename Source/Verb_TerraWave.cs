using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 剑气的瞄准预览：**技能射程圈 + 剑气实际会扫到的 12 格**，不画原版那个蓝色十字准星。
    ///
    /// 玩家反馈（2026-10-24）：点技能后出现的蓝色准星没有意义——
    /// 真正需要知道的是「我能打到多远」和「这一剑会扫过哪几格」。
    ///
    /// 三条铁律（都是踩过的坑）：
    /// 1. 重写 `DrawHighlight`，**绝不调 `base`**：父类依赖 ability 状态，一抛异常整层 GUI 挂掉；
    /// 2. 不要在 AbilityDef 里只写 verbClass 就完事——引擎不会把 ability 挂到 verb 上，
    ///    由 `Ability_TerraWave` 构造时补 `cast.ability = this`；
    /// 3. 整个绘制包 try/catch + `Log.ErrorOnce`，绝不让异常冒到 GUI 层。
    /// </summary>
    public class Verb_TerraWave : Verb_CastAbility
    {
        private static readonly Color RangeColor = new Color(0.92f, 0.88f, 0.55f, 0.55f);
        private static readonly Color WaveColor = new Color(0.55f, 0.88f, 0.95f, 0.9f);
        private static readonly Color AimColor = new Color(0.95f, 0.78f, 0.35f, 0.9f);

        public override void DrawHighlight(LocalTargetInfo target)
        {
            try
            {
                Pawn caster = CasterPawn;
                if (caster == null || !caster.Spawned || caster.Map == null)
                {
                    return;
                }
                Map map = caster.Map;
                float range = verbProps?.range ?? TerraWavePath.MaxCells;

                // 1. 技能射程
                GenDraw.DrawRadiusRing(caster.Position, range, RangeColor);

                if (!target.IsValid)
                {
                    return;
                }

                // 2. 剑气会扫到的格
                List<IntVec3> cells = TerraWavePath.Cells(caster.Position, target.Cell, map);
                if (cells.Count > 0)
                {
                    GenDraw.DrawFieldEdges(cells, WaveColor);
                    // 3. 落点单独标一下（不用原版蓝色准星）
                    IntVec3 last = cells[cells.Count - 1];
                    GenDraw.DrawFieldEdges(new List<IntVec3> { last }, AimColor);
                }
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[Botania] Verb_TerraWave.DrawHighlight 出错：" + e, 194711);
            }
        }
    }
}
