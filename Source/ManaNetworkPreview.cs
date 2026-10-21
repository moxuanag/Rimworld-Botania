using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 把魔力中继塔连成的网络「逐跳」画出来：
    /// 自己这一环 → 范围内已建成的上游中继塔 → 魔力池，越远越淡。
    /// 放置蓝图时和左键选中时都用这一套，保证看到的是同一张图。
    /// </summary>
    public static class ManaNetworkPreview
    {
        private const int MaxHops = 8;

        private static readonly Color SelfColor = new Color(0.45f, 0.80f, 1f, 0.85f);
        private static readonly Color PoolColor = new Color(0.95f, 0.85f, 0.45f, 0.85f);

        public static void Draw(Map map, IntVec3 center, float selfRadius)
        {
            if (map == null || !center.IsValid)
            {
                return;
            }

            // 自己这一环
            GenDraw.DrawRadiusRing(center, selfRadius, SelfColor);

            HashSet<IntVec3> visited = new HashSet<IntVec3> { center };
            List<IntVec3> frontier = new List<IntVec3> { center };

            for (int hop = 0; hop < MaxHops; hop++)
            {
                List<IntVec3> next = new List<IntVec3>();
                for (int i = 0; i < frontier.Count; i++)
                {
                    IntVec3 from = frontier[i];
                    foreach (Thing other in GenRadial.RadialDistinctThingsAround(from, map, selfRadius, true))
                    {
                        if (!IsNetworkNode(other))
                        {
                            continue;
                        }
                        IntVec3 pos = other.Position;
                        if (!visited.Add(pos))
                        {
                            continue;
                        }
                        float alpha = Mathf.Lerp(0.8f, 0.25f, hop / (float)MaxHops);
                        Color color = other.def.defName == "Botania_ManaPool"
                            ? new Color(PoolColor.r, PoolColor.g, PoolColor.b, alpha)
                            : new Color(SelfColor.r, SelfColor.g, SelfColor.b, alpha);
                        GenDraw.DrawRadiusRing(pos, RadiusOf(other.def, selfRadius), color);
                        next.Add(pos);
                    }
                }
                if (next.Count == 0)
                {
                    break;
                }
                frontier = next;
            }
        }

        /// <summary>能当网络节点的：中继塔与魔力池。</summary>
        private static bool IsNetworkNode(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                return false;
            }
            if (thing.def.defName == "Botania_ManaPool")
            {
                return true;
            }
            return thing.TryGetComp<CompManaRelay>() != null;
        }

        /// <summary>取这个 Def 自己的供给半径，取不到就用调用方给的默认值。</summary>
        private static float RadiusOf(ThingDef def, float fallback)
        {
            if (def?.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_ManaRelay relay)
                    {
                        return relay.radius;
                    }
                }
            }
            return fallback;
        }
    }
}
