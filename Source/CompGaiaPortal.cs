using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚传送门配置。
    /// 波次用原版机械族占位（默认 Mech_Scyther），等盖亚造物做出来再换 wavePawnKind。
    /// </summary>
    public class CompProperties_GaiaPortal : CompProperties
    {
        /// <summary>每波刷的种族。占位：原版机械镰刀手。</summary>
        public string wavePawnKind = "Mech_Scyther";

        /// <summary>每波数量。</summary>
        public List<int> waveSizes = new List<int> { 6, 8, 10 };

        /// <summary>波与波之间的间隔（tick）。90 秒 = 5400。</summary>
        public int waveIntervalTicks = 5400;

        /// <summary>传送门出现后第一波的延迟。</summary>
        public int firstWaveDelayTicks = 600;

        /// <summary>投降后冷却（tick）。1 天 = 60000。</summary>
        public int surrenderCooldownTicks = 60000;

        /// <summary>刷怪半径。</summary>
        public float spawnRadius = 8f;

        public CompProperties_GaiaPortal()
        {
            compClass = typeof(CompGaiaPortal);
        }
    }

    /// <summary>
    /// 盖亚传送门：
    ///  · 依次放出 3 波敌人（波与波之间熄火，玩家可以修墙布防）
    ///  · 全部清完后进入「开启」状态（Boss 未实装，先给提示）
    ///  · 提供「停止仪式（投降）」按钮：把场上小怪化为光收回，进入 1 天冷却
    ///  · 传送门本身不可被攻击、不可被破坏
    /// </summary>
    public class CompGaiaPortal : ThingComp
    {
        private int wavesDone;
        private int nextWaveTick = -1;
        private bool completed;
        private bool surrendered;
        private List<Pawn> spawnedPawns = new List<Pawn>();

        private CompProperties_GaiaPortal Props => (CompProperties_GaiaPortal)props;

        private Map Map => parent.Map;

        public bool Surrendered => surrendered;

        public bool Completed => completed;

        public int WavesDone => wavesDone;

        public int TotalWaves => Props.waveSizes == null ? 0 : Props.waveSizes.Count;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (nextWaveTick < 0 && !completed && !surrendered)
            {
                nextWaveTick = Find.TickManager.TicksGame + Props.firstWaveDelayTicks;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (surrendered || completed)
            {
                return;
            }
            PruneDeadPawns();

            if (Find.TickManager.TicksGame < nextWaveTick)
            {
                return;
            }

            if (wavesDone >= TotalWaves)
            {
                FinishWaves();
                return;
            }

            SpawnWave();
        }

        private void PruneDeadPawns()
        {
            for (int i = spawnedPawns.Count - 1; i >= 0; i--)
            {
                Pawn p = spawnedPawns[i];
                if (p == null || p.Destroyed || !p.Spawned || p.Dead)
                {
                    spawnedPawns.RemoveAt(i);
                }
            }
        }

        private void SpawnWave()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.wavePawnKind);
            if (kind == null)
            {
                Log.Error("[Botania] 盖亚传送门找不到刷怪种族: " + Props.wavePawnKind + "，波次中止");
                completed = true;
                return;
            }

            int count = Props.waveSizes[wavesDone];
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(
                    parent.Position, Map, (int)Props.spawnRadius,
                    c => c.Walkable(Map) && !c.Fogged(Map));
                if (!cell.IsValid)
                {
                    continue;
                }
                Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
                if (pawn == null)
                {
                    continue;
                }
                GenSpawn.Spawn(pawn, cell, Map);
                spawnedPawns.Add(pawn);
                spawned++;
            }

            wavesDone++;
            nextWaveTick = Find.TickManager.TicksGame + Props.waveIntervalTicks;

            Messages.Message(
                "Botania.GaiaWave.Message".Translate(wavesDone, TotalWaves, spawned),
                new LookTargets(parent), MessageTypeDefOf.ThreatBig);
        }

        private void FinishWaves()
        {
            completed = true;
            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaPortalReady.Label".Translate(),
                "Botania.GaiaPortalReady.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(parent));
        }

        /// <summary>传送门不可被攻击、不可被破坏。</summary>
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            absorbed = true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (!surrendered && !completed)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Botania.GaiaSurrender.Label".Translate(),
                    defaultDesc = "Botania.GaiaSurrender.Desc".Translate(),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = Surrender
                };
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 立即下一波",
                    defaultDesc = "跳过等待，立刻刷出下一波。",
                    action = () =>
                    {
                        nextWaveTick = Find.TickManager.TicksGame;
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 清空小怪",
                    defaultDesc = "把场上本传送门刷出的小怪全部移除。",
                    action = DespawnAllSpawned
                };
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 跳过剩余波次",
                    defaultDesc = "直接把波次计数拉满，进入「传送门开启」状态。",
                    action = () =>
                    {
                        wavesDone = TotalWaves;
                        nextWaveTick = Find.TickManager.TicksGame;
                    }
                };
            }
        }

        private void Surrender()
        {
            if (surrendered || completed)
            {
                return;
            }
            surrendered = true;
            DespawnAllSpawned();

            MapComponent_GaiaEvent mc = Map.GetComponent<MapComponent_GaiaEvent>();
            if (mc != null)
            {
                mc.NotifyPortalSurrendered(parent);
            }

            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaSurrenderDone.Label".Translate(),
                "Botania.GaiaSurrenderDone.Text".Translate(),
                LetterDefOf.NeutralEvent,
                new LookTargets(parent));
        }

        private void DespawnAllSpawned()
        {
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn p = spawnedPawns[i];
                if (p != null && p.Spawned && !p.Dead)
                {
                    p.DeSpawn(DestroyMode.Vanish);
                }
            }
            spawnedPawns.Clear();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref wavesDone, "gaiaWavesDone", 0);
            Scribe_Values.Look(ref nextWaveTick, "gaiaNextWaveTick", -1);
            Scribe_Values.Look(ref completed, "gaiaCompleted", false);
            Scribe_Values.Look(ref surrendered, "gaiaSurrendered", false);
            Scribe_Collections.Look(ref spawnedPawns, "gaiaSpawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spawnedPawns == null)
            {
                spawnedPawns = new List<Pawn>();
            }
        }
    }
}
