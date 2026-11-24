using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力中继塔（重构后）：**纯网络扩展建筑**。
    ///
    /// 用户定案（2026-11-24）：
    ///   · 不再储魔、不再"拉"魔力、没有损耗 —— 它只是一个把魔力网络向外延伸的节点；
    ///   · 判定：它覆盖圆内只要与「已连入有效网络的节点」相交，就算连上并扩展网络；
    ///     否则提示「无可用魔力网络」，覆盖圈标红（见 MapComponent_ManaNetwork）。
    ///
    /// 老存档兼容：旧版塔里有 `mana` 字段，读档时把它一次性转入最近的魔力池，不丢失。
    /// </summary>
    public class CompProperties_ManaRelay : CompProperties
    {
        /// <summary>网络扩展半径（格）。</summary>
        public float radius = 11f;

        public CompProperties_ManaRelay()
        {
            compClass = typeof(CompManaRelay);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (radius <= 0f)
            {
                yield return "radius must be greater than 0.";
            }
        }
    }

    public class CompManaRelay : ThingComp, IManaNetworkNode
    {
        /// <summary>旧版本塔内残留的魔力（读档时迁移进最近魔力池）。</summary>
        private float legacyMana;

        public CompProperties_ManaRelay Props => (CompProperties_ManaRelay)props;

        // ---------------- 魔力网络节点 ----------------

        public IntVec3 NodeCell => parent.Position;

        public float NetworkRadius => Props.radius;

        /// <summary>中继塔不是魔力池，只是扩展节点。</summary>
        public bool IsPoolNode => false;

        public IManaSource PoolSource => null;

        // ---------------- 生命周期 ----------------

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MarkNetworkDirty();
            if (legacyMana > 0.01f && parent.Spawned && parent.Map != null)
            {
                MigrateLegacyMana();
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MapComponent_ManaNetwork>()?.MarkDirty();
        }

        private void MarkNetworkDirty()
        {
            if (parent != null && parent.Spawned && parent.Map != null)
            {
                parent.Map.GetComponent<MapComponent_ManaNetwork>()?.MarkDirty();
            }
        }

        /// <summary>把旧版塔里存的魔力搬进最近的魔力池（网络重构的一次性迁移）。</summary>
        private void MigrateLegacyMana()
        {
            Map map = parent.Map;
            CompManaStorage best = null;
            float bestDist = float.MaxValue;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                CompManaStorage pool = thing?.TryGetComp<CompManaStorage>();
                if (pool == null || !pool.AcceptsMana || pool.IsFull)
                {
                    continue;
                }
                float dist = thing.Position.DistanceTo(parent.Position);
                if (dist < bestDist)
                {
                    best = pool;
                    bestDist = dist;
                }
            }
            if (best == null)
            {
                BotaniaDebug.Log("[中继塔] 旧存档魔力无处迁移（附近没有可用魔力池），已丢弃 " + legacyMana.ToString("F0"));
                legacyMana = 0f;
                return;
            }
            float accepted = best.AddMana(legacyMana);
            BotaniaDebug.Log("[中继塔] 旧存档魔力 " + legacyMana.ToString("F0") + " 迁移至最近魔力池，实际接收 " + accepted.ToString("F0"));
            legacyMana = Mathf.Max(0f, legacyMana - accepted);
            MarkNetworkDirty();
        }

        // ---------------- 显示 ----------------

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            MapComponent_ManaNetwork net = parent.Map.GetComponent<MapComponent_ManaNetwork>();
            bool connected = net != null && net.IsNodeConnected(parent);
            return connected
                ? "Botania.RelayLinked".Translate(Props.radius.ToString("F0"))
                : "Botania.RelayNoNetwork".Translate(Props.radius.ToString("F0"));
        }

        /// <summary>开发调试：切换前先看自己覆盖到的网络格数，方便定位「为什么没连上」。</summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (!Prefs.DevMode)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Botania.Dev.RelayReport".Translate(),
                defaultDesc = "Botania.Dev.RelayReportDesc".Translate(),
                action = delegate
                {
                    MapComponent_ManaNetwork net = parent.Map?.GetComponent<MapComponent_ManaNetwork>();
                    bool connected = net != null && net.IsNodeConnected(parent);
                    Messages.Message(
                        "Botania.Dev.RelayReportResult".Translate(
                            connected ? "Botania.RelayLinkedShort".Translate() : "Botania.RelayNoNetworkShort".Translate(),
                            Props.radius.ToString("F0")),
                        new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
                }
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // 沿用旧键 mana：读回旧存档的存量，SpawnSetup 时迁移；新版不再写入
            Scribe_Values.Look(ref legacyMana, "mana", 0f);
        }
    }
}
