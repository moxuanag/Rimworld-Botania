using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 放置魔力中继塔（或魔力池）时，把它连成的那张网的覆盖范围全部画出来，
    /// 这样摆蓝图时就能看出"这一跳接不接得上"。
    /// 具体画法见 <see cref="ManaNetworkPreview"/>。
    /// </summary>
    public class PlaceWorker_ManaRelayNetwork : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            ManaNetworkPreview.Draw(map, center, FindRadius(def, 6f));
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
                }
            }
            return fallback;
        }
    }
}
