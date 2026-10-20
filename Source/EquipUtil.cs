using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 装备类物品（武器/衣服）被穿戴时，Thing.Map / Thing.Position 都是空的，
    /// 必须顺着 ParentHolder 找到持有者。几个 Comp 都要用，抽出来避免各写一份。
    /// </summary>
    public static class EquipUtil
    {
        public static Pawn GetHolder(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            if (thing.ParentHolder is Pawn_EquipmentTracker equipment)
            {
                return equipment.pawn;
            }
            if (thing.ParentHolder is Pawn_ApparelTracker apparel)
            {
                return apparel.pawn;
            }
            return null;
        }

        public static Map GetMap(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            if (thing.Map != null)
            {
                return thing.Map;
            }
            return GetHolder(thing)?.Map;
        }

        public static IntVec3 GetPosition(Thing thing)
        {
            if (thing == null)
            {
                return IntVec3.Invalid;
            }
            Pawn holder = GetHolder(thing);
            if (holder != null)
            {
                return holder.Position;
            }
            return thing.Position;
        }
    }
}
