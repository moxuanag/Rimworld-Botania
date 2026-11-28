using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 地图级的「树已被认领」记录：同一天里，同一棵树只会被取木一次。
    ///
    /// 为什么需要它：丰饶之花数树时是「半径内有多少成熟树就掉多少份原木」。
    /// 当玩家放了第二朵、第三朵花，它们的半径很容易叠在一起，
    /// 同一棵树就会被反复数到、反复出木。这里用树的 thingIDNumber 做认领，
    /// 认领成功的那一朵才发原木。
    ///
    /// 换日自动清空（<see cref="GenDate.TicksPerDay"/> 一天一个编号），
    /// 所以「每夜每棵树只算一次」天然成立，不需要每朵花各自记树。
    ///
    /// 引擎会反射实例化所有 MapComponent 子类（Map.FillComponents），
    /// 调用方直接 <c>map.GetComponent&lt;MapComponent_TreeClaims&gt;()</c> 即可。
    /// </summary>
    public class MapComponent_TreeClaims : MapComponent
    {
        private HashSet<int> claimed = new HashSet<int>();

        /// <summary>最近一次清理时所在的天（绝对天数），-1 表示还没算过。</summary>
        private int day = -1;

        public MapComponent_TreeClaims(Map map) : base(map)
        {
        }

        /// <summary>今天有没有认领过这棵树（只查，不认领）。</summary>
        public bool IsClaimedToday(int thingId)
        {
            RefreshDay();
            return claimed.Contains(thingId);
        }

        /// <summary>
        /// 认领一棵树：今天第一次认领返回 true，已经被认领过返回 false。
        /// 换日时自动清空全部认领。
        /// </summary>
        public bool TryClaim(int thingId)
        {
            RefreshDay();
            return claimed.Add(thingId);
        }

        /// <summary>跨日就清空。天数按绝对 tick 整除一天得到，与地图/游戏时间无关。</summary>
        private void RefreshDay()
        {
            int today = GenTicks.TicksAbs / GenDate.TicksPerDay;
            if (today == day)
            {
                return;
            }
            if (claimed == null)
            {
                claimed = new HashSet<int>();
            }
            else
            {
                claimed.Clear();
            }
            day = today;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref claimed, "botaniaTreeClaims", LookMode.Value);
            Scribe_Values.Look(ref day, "botaniaTreeClaimsDay", -1);
            if (claimed == null)
            {
                claimed = new HashSet<int>();
            }
        }
    }
}
