using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 把「一个连通网络分量」包装成 IManaSource，并对抽取做「离原点最近优先」排序。
    /// 这样设备侧的 <c>Core.FindSource(...).DrainMana(...)</c> 不用改任何调用点。
    /// </summary>
    public class ManaNetworkSource : IManaSource
    {
        private readonly ManaNetworkGroup group;
        private readonly IntVec3 origin;

        public ManaNetworkSource(ManaNetworkGroup group, IntVec3 origin)
        {
            this.group = group;
            this.origin = origin;
        }

        public ManaNetworkGroup Group => group;

        public IntVec3 Origin => origin;

        public float ManaAmount => group.ManaAmount;

        public float FreeSpace => group.FreeSpace;

        public float AddMana(float amount)
        {
            return group.AddMana(amount);
        }

        public float DrainMana(float amount)
        {
            return group.DrainManaFrom(amount, origin);
        }
    }

    /// <summary>
    /// 魔力网络的地图级宿主。
    ///
    /// 职责：
    ///   1. 收集本图的网络节点（魔力池 = 种子，中继塔 = 扩展）；
    ///   2. 按「覆盖圆相交」计算连通分量（并集查集），只有**含至少一个魔力池**的分量才算有效网络；
    ///   3. 提供查询：某格是否在网络内、从该格取/送魔力；
    ///   4. 选中网络节点时画**整片网络的边界**（只画边界，内部无线条，效果类似逆重飞船区域）。
    ///
    /// 重建时机：建筑增删打 dirty + 每 250 tick 保底，避免每帧全图扫描。
    /// </summary>
    public class MapComponent_ManaNetwork : MapComponent
    {
        private const int RebuildIntervalTicks = 250;
        private const int MaxOutlineCells = 8000;

        private static readonly Color OutlineColor = new Color(0.55f, 0.85f, 1f, 0.55f);
        private static readonly Color IsolatedColor = new Color(0.95f, 0.45f, 0.4f, 0.85f);

        private readonly List<ManaNetworkNode> nodes = new List<ManaNetworkNode>();
        private readonly List<ManaNetworkGroup> groups = new List<ManaNetworkGroup>();
        private bool dirty = true;
        private int rebuildCountdown = RebuildIntervalTicks;

        public MapComponent_ManaNetwork(Map map) : base(map)
        {
        }

        public IReadOnlyList<ManaNetworkNode> Nodes => nodes;

        /// <summary>建筑增删 / 读档后调用，下一次 tick 重建。</summary>
        public void MarkDirty()
        {
            dirty = true;
        }

        public override void FinalizeInit()
        {
            dirty = true;
        }

        public override void MapComponentTick()
        {
            if (--rebuildCountdown <= 0)
            {
                rebuildCountdown = RebuildIntervalTicks;
                dirty = true;
            }
            if (dirty)
            {
                Rebuild();
            }
        }

        // ---------------- 重建 ----------------

        private void Rebuild()
        {
            dirty = false;
            nodes.Clear();
            groups.Clear();

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    continue;
                }
                IManaNetworkNode node = thing.TryGetComp<CompManaStorage>() as IManaNetworkNode
                                        ?? thing.TryGetComp<CompManaRelay>() as IManaNetworkNode;
                if (node == null)
                {
                    continue;
                }
                nodes.Add(new ManaNetworkNode
                {
                    thing = thing,
                    node = node,
                    cell = thing.Position,
                    radius = node.NetworkRadius,
                    isPool = node.IsPoolNode,
                    connected = false,
                    component = -1,
                });
            }

            int count = nodes.Count;
            if (count == 0)
            {
                return;
            }

            // 并查集：覆盖圆相交的节点属于同一分量
            int[] parent = new int[count];
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float reach = nodes[i].radius + nodes[j].radius;
                    if (nodes[i].cell.DistanceTo(nodes[j].cell) <= reach + 0.01f)
                    {
                        UnionRoots(parent, i, j);
                    }
                }
            }

            // 每个分量是否含魔力池
            Dictionary<int, bool> hasPool = new Dictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                int root = FindRoot(parent, i);
                bool isPool = nodes[i].isPool;
                if (!hasPool.TryGetValue(root, out bool existing) || (!existing && isPool))
                {
                    hasPool[root] = existing || isPool;
                }
            }

            // 组装分量对象
            Dictionary<int, ManaNetworkGroup> byRoot = new Dictionary<int, ManaNetworkGroup>();
            for (int i = 0; i < count; i++)
            {
                int root = FindRoot(parent, i);
                bool alive = hasPool.TryGetValue(root, out bool flag) && flag;
                nodes[i].connected = alive;
                if (!alive)
                {
                    continue;
                }
                if (!byRoot.TryGetValue(root, out ManaNetworkGroup group))
                {
                    group = new ManaNetworkGroup();
                    byRoot[root] = group;
                    groups.Add(group);
                }
                group.Add(nodes[i]);
            }

            // 回填分量编号（给预览/调试用）
            for (int i = 0; i < groups.Count; i++)
            {
                foreach (ManaNetworkNode n in groups[i].Nodes)
                {
                    n.component = i;
                }
            }
        }

        private static int FindRoot(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void UnionRoots(int[] parent, int a, int b)
        {
            int ra = FindRoot(parent, a);
            int rb = FindRoot(parent, b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        // ---------------- 查询 ----------------

        /// <summary>某格所在的网络分量（取覆盖它的最近节点所属分量），没有则 null。</summary>
        public ManaNetworkGroup GroupAt(IntVec3 cell)
        {
            ManaNetworkNode best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                ManaNetworkNode n = nodes[i];
                if (!n.connected)
                {
                    continue;
                }
                float dist = n.cell.DistanceTo(cell);
                if (dist > n.radius || dist >= bestDist)
                {
                    continue;
                }
                best = n;
                bestDist = dist;
            }
            if (best == null || best.component < 0 || best.component >= groups.Count)
            {
                return null;
            }
            return groups[best.component];
        }

        /// <summary>把某格包装成可用的魔力源；不在网络内返回 null。</summary>
        public IManaSource SourceAt(IntVec3 cell)
        {
            ManaNetworkGroup group = GroupAt(cell);
            return group == null ? null : new ManaNetworkSource(group, cell);
        }

        public bool IsInsideNetwork(IntVec3 cell)
        {
            return GroupAt(cell) != null;
        }

        /// <summary>该节点是否已连入有效网络（给中继塔的提示用）。</summary>
        public bool IsNodeConnected(Thing thing)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].thing == thing)
                {
                    return nodes[i].connected;
                }
            }
            return false;
        }

        /// <summary>某节点所在网络的全部覆盖格（用于画边界）。</summary>
        public List<IntVec3> CoverageCellsOf(Thing thing)
        {
            ManaNetworkGroup group = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].thing == thing && nodes[i].connected)
                {
                    group = groups[nodes[i].component];
                    break;
                }
            }
            if (group == null)
            {
                return null;
            }
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            foreach (ManaNetworkNode n in group.Nodes)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(n.cell, n.radius, true))
                {
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    cells.Add(c);
                    if (cells.Count > MaxOutlineCells)
                    {
                        return new List<IntVec3>(cells);
                    }
                }
            }
            return new List<IntVec3>(cells);
        }

        // ---------------- 绘制 ----------------

        public override void MapComponentDraw()
        {
            if (Find.Selector == null || Find.Selector.SelectedObjects == null)
            {
                return;
            }
            List<object> selected = Find.Selector.SelectedObjects;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!(selected[i] is Thing thing))
                {
                    continue;
                }
                if (!(thing.TryGetComp<CompManaStorage>() is IManaNetworkNode)
                    && !(thing.TryGetComp<CompManaRelay>() is IManaNetworkNode))
                {
                    continue;
                }
                DrawForNode(thing);
            }
        }

        private void DrawForNode(Thing thing)
        {
            bool connected = IsNodeConnected(thing);
            if (!connected)
            {
                // 未连上的中继塔：只画自己的圈并标红，说明「我能覆盖到这里，但还没接上」
                foreach (ManaNetworkNode n in nodes)
                {
                    if (n.thing == thing)
                    {
                        GenDraw.DrawRadiusRing(n.cell, n.radius, IsolatedColor);
                        break;
                    }
                }
                return;
            }

            List<IntVec3> cells = CoverageCellsOf(thing);
            if (cells != null && cells.Count > 0)
            {
                // 只画边界（内部无线条），效果类似逆重飞船区域
                GenDraw.DrawFieldEdges(cells, OutlineColor);
            }
        }
    }
}
