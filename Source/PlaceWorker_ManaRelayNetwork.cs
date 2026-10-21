using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 放置魔力中继塔时，把它连成一张网的覆盖范围全部画出来：
    /// 自己的半径 → 已建成的上游中继塔 → 魔力池，逐跳把圆环串起来。
    /// 这样摆蓝图时就能看出"这一跳接不接得上"。
    /// </summary>
    public class PlaceWorker_ManaRelayNetwork : PlaceWorker
    {
        private const int MaxHops = 8;

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            float radius = FindRadius(def, 6f);

            // 自己这一环
            GenDraw.DrawRadiusRing(center, radius, new Color(0.45f, 0.80f, 1f, 0.85f));

            // 从新塔往外，一跳一跳找已有的中继塔 / 魔力池，把它们的范围也画出来
            HashSet<IntVec3> visited = new HashSet<IntVec3> { center };
            List<IntVec3> frontier = new List<IntVec3> { center };

            for (int hop = 0; hop < MaxHops; hop++)
            {
                List<IntVec3> next = new List<IntVec3>();
                for (int i = 0; i < frontier.Count; i++)
                {
                    IntVec3 from = frontier[i];
                    foreach (Thing other in GenRadial.RadialDistinctThingsAround(from, map, radius, true))
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
                        // 越远越淡，一眼能看出第几跳
                        float alpha = Mathf.Lerp(0.8f, 0.25f, hop / (float)MaxHops);
                        Color color = other.def.defName == "Botania_ManaPool"
                            ? new Color(0.95f, 0.85f, 0.45f, alpha)
                            : new Color(0.45f, 0.80f, 1f, alpha);
                        GenDraw.DrawRadiusRing(pos, FindRadius(other.def, 6f), color);
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

        private static float FindRadius(ThingDef def, float fallback)
        {
            if (def?.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_ManaRelay relay)
                    {
                        return relay.radius;
                    }
                    if (def.comps[i] is CompProperties_ManaStorage)
                    {
                        return fallback;
                    }
                }
            }
            return fallback;
        }
    }
}
