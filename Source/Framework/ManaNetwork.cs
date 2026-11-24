using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力网络的节点（魔力池 / 中继塔）。
    ///
    /// 设计（用户 2026-11-24 定案）：
    ///   · 网络范围 = 所有「已连接节点」覆盖圆的并集，这是**唯一**一套范围，
    ///     设备不再各有各的搜索半径。
    ///   · 魔力池是种子节点（永远算已连接），半径 R池。
    ///   · 中继塔是纯扩展节点，只有与某个已连接节点的圆**相交**才算连上，半径 R塔。
    ///   · 同一网络内的魔力池视为一个共享大池。
    /// </summary>
    public interface IManaNetworkNode
    {
        /// <summary>节点所在格。</summary>
        IntVec3 NodeCell { get; }

        /// <summary>该节点的覆盖半径（格）。</summary>
        float NetworkRadius { get; }

        /// <summary>true = 魔力池（种子 + 可存取）；false = 中继塔（纯扩展）。</summary>
        bool IsPoolNode { get; }

        /// <summary>魔力池返回自己的 IManaSource；中继塔返回 null。</summary>
        IManaSource PoolSource { get; }
    }

    /// <summary>网络里的一个节点实例（每帧重建，不存档）。</summary>
    public class ManaNetworkNode
    {
        public Thing thing;
        public IManaNetworkNode node;
        public IntVec3 cell;
        public float radius;
        public bool isPool;
        public bool connected;
        public int component;
    }

    /// <summary>
    /// 一个「连通的网络分量」：component 相同的已连接节点集合。
    /// 对设备来说它就是一个共享大池，因此实现 IManaSource，设备侧 API 完全不用改。
    /// </summary>
    public class ManaNetworkGroup : IManaSource
    {
        private readonly List<ManaNetworkNode> nodes = new List<ManaNetworkNode>();

        public void Add(ManaNetworkNode n)
        {
            nodes.Add(n);
        }

        public IReadOnlyList<ManaNetworkNode> Nodes => nodes;

        /// <summary>该分量里的所有魔力池节点。</summary>
        public IEnumerable<ManaNetworkNode> Pools()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].isPool && nodes[i].node.PoolSource != null)
                {
                    yield return nodes[i];
                }
            }
        }

        public float ManaAmount
        {
            get
            {
                float sum = 0f;
                foreach (ManaNetworkNode n in Pools())
                {
                    sum += n.node.PoolSource.ManaAmount;
                }
                return sum;
            }
        }

        public float FreeSpace
        {
            get
            {
                float sum = 0f;
                foreach (ManaNetworkNode n in Pools())
                {
                    sum += n.node.PoolSource.FreeSpace;
                }
                return sum;
            }
        }

        /// <summary>从本网络抽取魔力：按距离由近到远依次扣（避免抖动）。</summary>
        public float DrainMana(float amount)
        {
            return DrainManaFrom(amount, null);
        }

        /// <summary>指定原点时按「离原点最近」排序，否则按节点顺序。</summary>
        public float DrainManaFrom(float amount, IntVec3? origin)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            List<ManaNetworkNode> pools = new List<ManaNetworkNode>(Pools());
            if (origin.HasValue)
            {
                IntVec3 o = origin.Value;
                pools.Sort((a, b) => a.cell.DistanceToSquared(o).CompareTo(b.cell.DistanceToSquared(o)));
            }
            float drained = 0f;
            for (int i = 0; i < pools.Count && drained < amount; i++)
            {
                float need = amount - drained;
                drained += pools[i].node.PoolSource.DrainMana(need);
            }
            return drained;
        }

        /// <summary>把魔力送进本网络：按距离由近到远找有空位的池子。</summary>
        public float AddMana(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }
            float added = 0f;
            List<ManaNetworkNode> pools = new List<ManaNetworkNode>(Pools());
            for (int i = 0; i < pools.Count && added < amount; i++)
            {
                if (!IsPoolAccepting(pools[i]))
                {
                    continue;
                }
                added += pools[i].node.PoolSource.AddMana(amount - added);
            }
            return added;
        }

        public static bool IsPoolAccepting(ManaNetworkNode node)
        {
            CompManaStorage pool = node.thing?.TryGetComp<CompManaStorage>();
            return pool != null && pool.AcceptsMana && !pool.IsFull;
        }
    }
}
