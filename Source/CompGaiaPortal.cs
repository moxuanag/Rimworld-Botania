using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚传送门配置。
    /// 流程：碎块被击碎 → 传送门出现但**未激活** → 小人走过去调查 → 调查完成才开波次。
    /// 打不过可以投降：召唤物被收回，传送门黯淡并进入冷却；冷却结束后**重新派人调查**即可再开，
    /// 不会再天降一块新碎块。
    /// </summary>
    public class CompProperties_GaiaPortal : CompProperties
    {
        /// <summary>每波刷的种族。占位：原版机械镰刀手。</summary>
        public string wavePawnKind = "Mech_Scyther";

        /// <summary>每波数量。</summary>
        public List<int> waveSizes = new List<int> { 6, 8, 10 };

        /// <summary>波与波之间的间隔（tick）。90 秒 = 5400。</summary>
        public int waveIntervalTicks = 5400;

        /// <summary>调查完成后第一波的延迟。</summary>
        public int firstWaveDelayTicks = 600;

        /// <summary>投降后的黯淡冷却（tick）。1 天 = 60000。</summary>
        public int surrenderCooldownTicks = 60000;

        /// <summary>刷怪半径。</summary>
        public float spawnRadius = 8f;

        /// <summary>调查所需累计 tick（一个人站在旁边 1200 tick = 20 秒）。</summary>
        public int investigationTicks = 1200;

        /// <summary>调查判定半径（格）。</summary>
        public float investigateRadius = 2.9f;

        /// <summary>同时计入进度的调查人数上限。</summary>
        public int maxInvestigators = 3;

        public CompProperties_GaiaPortal()
        {
            compClass = typeof(CompGaiaPortal);
        }
    }

    /// <summary>
    /// 盖亚传送门的运行状态机：待调查 → 波次中 → 已开启；投降则回到黯淡（冷却）→ 待调查。
    /// 传送门本身不可被攻击、不可被破坏、不可被占领。
    /// </summary>
    public class CompGaiaPortal : ThingComp
    {
        private enum Phase
        {
            Dormant,    // 未激活，等人来调查
            Waves,      // 正在放波次
            Opened,     // 波次清空，传送门开启（Boss 待实装）
            Dimmed      // 投降后黯淡，冷却中
        }

        private Phase phase = Phase.Dormant;
        private int investigationProgress;
        private int wavesDone;
        private int nextWaveTick = -1;
        private int cooldownUntil = -1;
        private List<Pawn> spawnedPawns = new List<Pawn>();

        private CompProperties_GaiaPortal Props => (CompProperties_GaiaPortal)props;

        private Map Map => parent.Map;

        public int TotalWaves => Props.waveSizes == null ? 0 : Props.waveSizes.Count;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (phase == Phase.Waves && nextWaveTick < 0)
            {
                nextWaveTick = Find.TickManager.TicksGame + Props.firstWaveDelayTicks;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            switch (phase)
            {
                case Phase.Dimmed:
                    if (Find.TickManager.TicksGame >= cooldownUntil)
                    {
                        // 冷却结束：回到待调查，不会再有新的碎块
                        phase = Phase.Dormant;
                        investigationProgress = 0;
                        wavesDone = 0;
                        nextWaveTick = -1;
                        Log.Message("[Botania] 盖亚传送门冷却结束，可以重新派人调查");
                    }
                    break;

                case Phase.Dormant:
                    // 进度只由 JobDriver_InvestigateGaiaPortal 推进（必须有小人在做这项工作）
                    break;

                case Phase.Waves:
                    PruneDeadPawns();
                    if (Find.TickManager.TicksGame >= nextWaveTick)
                    {
                        if (wavesDone >= TotalWaves)
                        {
                            OpenPortal();
                        }
                        else
                        {
                            SpawnWave();
                        }
                    }
                    break;
            }
        }

        // ---------------- 调查（由 JobDriver 推进） ----------------

        /// <summary>是否处于「待调查」状态（WorkGiver / JobDriver 用）。</summary>
        public bool NeedsInvestigation => phase == Phase.Dormant;

        /// <summary>调查进度 0~1（给进度条用）。</summary>
        public float InvestigationPercent
        {
            get
            {
                int need = Props.investigationTicks;
                if (need <= 0)
                {
                    return 1f;
                }
                return investigationProgress <= 0 ? 0f : (float)investigationProgress / need;
            }
        }

        /// <summary>
        /// 小人工作一 tick 调一次。amount 是这一 tick 的有效工作量（按研究速度算）。
        /// 返回 true 表示调查已完成（JobDriver 据此结束当前 toil）。
        /// </summary>
        public bool AddInvestigationWork(float amount)
        {
            if (phase != Phase.Dormant)
            {
                return true;
            }
            investigationProgress += (int)(amount <= 0f ? 1f : amount);
            if (investigationProgress >= Props.investigationTicks)
            {
                Activate();
                return true;
            }
            return false;
        }

        private void Activate()
        {
            phase = Phase.Waves;
            investigationProgress = 0;
            wavesDone = 0;
            nextWaveTick = Find.TickManager.TicksGame + Props.firstWaveDelayTicks;
            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaPortalActivated.Label".Translate(),
                "Botania.GaiaPortalActivated.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(parent));
        }

        // ---------------- 波次 ----------------

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
                Log.Error("[Botania] 盖亚传送门找不到刷怪种族: " + Props.wavePawnKind);
                OpenPortal();
                return;
            }

            Faction hostile = Faction.OfMechanoids;
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
                Pawn pawn = PawnGenerator.GeneratePawn(kind, hostile);
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

        private void OpenPortal()
        {
            phase = Phase.Opened;
            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaPortalReady.Label".Translate(),
                "Botania.GaiaPortalReady.Text".Translate(),
                LetterDefOf.ThreatBig,
                new LookTargets(parent));
        }

        // ---------------- 投降 ----------------

        private void Surrender()
        {
            if (phase != Phase.Waves && phase != Phase.Opened)
            {
                return;
            }
            DespawnAllSpawned();
            phase = Phase.Dimmed;
            wavesDone = 0;
            investigationProgress = 0;
            nextWaveTick = -1;
            cooldownUntil = Find.TickManager.TicksGame + Props.surrenderCooldownTicks;

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

        // ---------------- 交互与显示 ----------------

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            absorbed = true;
        }

        public override string CompInspectStringExtra()
        {
            switch (phase)
            {
                case Phase.Dormant:
                    return "Botania.GaiaInspect.Dormant".Translate(
                        investigationProgress * 100 / Props.investigationTicks);
                case Phase.Waves:
                    return "Botania.GaiaInspect.Waves".Translate(wavesDone, TotalWaves);
                case Phase.Opened:
                    return "Botania.GaiaInspect.Opened".Translate();
                case Phase.Dimmed:
                    int left = cooldownUntil - Find.TickManager.TicksGame;
                    return "Botania.GaiaInspect.Dimmed".Translate(left.ToStringTicksToPeriod());
                default:
                    return null;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (phase == Phase.Dormant)
            {
                yield return new Command_Target
                {
                    defaultLabel = "Botania.GaiaInvestigate.Label".Translate(),
                    defaultDesc = "Botania.GaiaInvestigate.Desc".Translate(),
                    icon = TexCommand.GatherSpotActive,
                    targetingParams = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetLocations = false,
                        canTargetBuildings = false,
                        canTargetAnimals = false,
                        canTargetMechs = false,
                        validator = ti => ti.Thing is Pawn p && p.IsColonistPlayerControlled && !p.Dead
                    },
                    action = ti =>
                    {
                        if (ti.Thing is Pawn p)
                        {
                            OrderInvestigation(p);
                        }
                    }
                };
            }

            if (phase == Phase.Waves || phase == Phase.Opened)
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
                    defaultLabel = "[DEV] 完成调查",
                    action = Activate
                };
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 立即下一波",
                    action = () => nextWaveTick = Find.TickManager.TicksGame
                };
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 清空敌人",
                    action = DespawnAllSpawned
                };
                yield return new Command_Action
                {
                    defaultLabel = "[DEV] 进入黯淡冷却",
                    action = Surrender
                };
            }
        }

        /// <summary>
        /// 把**指定的**殖民者派到门口：先 Goto 到传送门旁，再原地等待 investigationTicks。
        /// 光用 Goto 会在到达后立刻恢复日常工作走开，必须再排一个 Wait。
        /// </summary>
        private void OrderInvestigation(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned)
            {
                return;
            }

            if (pawn.Drafted && pawn.drafter != null)
            {
                pawn.drafter.Drafted = false;
            }

            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Botania_InvestigateGaiaPortal");
            if (def == null)
            {
                Log.Error("[Botania] 找不到 JobDef Botania_InvestigateGaiaPortal");
                return;
            }
            IntVec3 stand = FindStandCell(parent);
            if (!stand.IsValid)
            {
                Messages.Message("Botania.GaiaInvestigate.NoCell".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Job job = JobMaker.MakeJob(def, parent, stand);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            Messages.Message("Botania.GaiaInvestigate.Ordered".Translate(pawn.LabelShort),
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 算一个「小人能站」的格子：传送门周围 1~4 格、可通行、不在建筑内部。
        /// 传送门是 2×2 不可通行建筑，施工/调查都只能用相邻格。
        /// </summary>
        public static IntVec3 FindStandCell(Thing portal)
        {
            if (portal == null || !portal.Spawned)
            {
                return IntVec3.Invalid;
            }
            Map map = portal.Map;
            CellRect rect = portal.OccupiedRect();
            for (int radius = 1; radius <= 4; radius++)
            {
                CellRect ring = rect.ExpandedBy(radius);
                foreach (IntVec3 c in ring.Cells)
                {
                    if (rect.Contains(c))
                    {
                        continue;
                    }
                    if (!c.InBounds(map) || !c.Walkable(map) || c.Fogged(map))
                    {
                        continue;
                    }
                    return c;
                }
            }
            // 退一步：用原版的「附近可走格」
            IntVec3 near = CellFinder.RandomClosewalkCellNear(rect.CenterCell, map, 4,
                x => x.Walkable(map) && !x.Fogged(map) && !rect.Contains(x));
            return near;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref phase, "gaiaPhase", Phase.Dormant);
            Scribe_Values.Look(ref investigationProgress, "gaiaInvestigation", 0);
            Scribe_Values.Look(ref wavesDone, "gaiaWavesDone", 0);
            Scribe_Values.Look(ref nextWaveTick, "gaiaNextWaveTick", -1);
            Scribe_Values.Look(ref cooldownUntil, "gaiaCooldownUntil", -1);
            Scribe_Collections.Look(ref spawnedPawns, "gaiaSpawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spawnedPawns == null)
            {
                spawnedPawns = new List<Pawn>();
            }
        }
    }
}
