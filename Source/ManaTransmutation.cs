using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 材料转化的公共逻辑：白雏菊（自动）和森林法杖（手动）共用同一张规则表，
    /// 保证"把木头变活木"从哪条路走都是同一个结果。
    /// </summary>
    public static class ManaTransmutation
    {
        public static ManaConversionRule FindRule(List<ManaConversionRule> rules, ThingDef def)
        {
            if (rules == null || def == null)
            {
                return null;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                ManaConversionRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }
                if (rule.fromDef != null && def == rule.fromDef)
                {
                    return rule;
                }
                if (rule.fromDef == null && rule.fromCategory != null && def.IsWithinCategory(rule.fromCategory))
                {
                    return rule;
                }
            }
            return null;
        }

        /// <summary>
        /// 把这个物品堆转化掉。调用方负责先扣魔力。
        /// </summary>
        public static bool TryConvert(Thing input, ManaConversionRule rule)
        {
            if (input == null || rule == null || rule.toDef == null || input.Map == null)
            {
                return false;
            }
            int count = Math.Min(rule.count, input.stackCount);
            if (count <= 0)
            {
                return false;
            }

            IntVec3 cell = input.PositionHeld;
            Map map = input.Map;
            input.SplitOff(count).Destroy(DestroyMode.Vanish);

            Thing output = ThingMaker.MakeThing(rule.toDef, null);
            output.stackCount = count;
            GenPlace.TryPlaceThing(output, cell, map, ThingPlaceMode.Near);
            return true;
        }
    }
}
