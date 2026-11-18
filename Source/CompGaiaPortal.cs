using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚传送门配置。
    /// 流程：碎块被击碎 → 传送门出现（未激活）→ 小人走到门中央**调查** → 调查完才开波次。
    /// 打不过可以投降：召唤物被收回，传送门黯淡并进入冷却；冷却结束后重新派人调查即可，
    /// 不会再天降第二块碎块。
    /// </summary>
    public class CompProperties_GaiaPortal : CompProperties
    {
        public string wavePawnKind = "Mech_Scyther";
        public List<int> waveSizes = new List<int> { 6, 8, 10 };
        public int waveIntervalTicks = 5400;
        public int firstWaveDelayTicks = 600;
        public int surrenderCooldownTicks = 60000;
        public float spawnRadius = 8f;

        /// <summary>调查所需累计工作量（一个人、研究速度 1.0 时约 1200 tick = 20 秒）。</summary>
        public int investigationTicks = 1200;

        /// <summary>调查判定：小人必须站在传送门占用的格子里。</summary>
        public bool mustStandInside = true;

        public CompProperties_GaiaPortal()
        {
            compClass = typeof(CompGaiaPortal);
        }
    }

    /// <summary>
    /// 盖亚传送门状态机：Dormant（待调查）→ Waves（波次中）→ Opened；投降回到 Dimmed（黯淡冷却）→ Dormant。
    ///
    /// 调查进度的唯一来源是 CompTick：**有殖民者站在传送门的 3×3 范围里**才累加，
    /// 按该殖民者的研究速度算。这样不依赖自定义 JobDriver 的 tickAction
    /// （本引擎里那套不稳），只要人站进去就会涨。
    ///
    /// 「自动调查」开关（默认关）：
    ///  · 关：只有玩家点「派人调查」指定的小人会被派过去，工作系统不主动派人
    ///  · 开：挂进「研究」工作类型，小人按研究优先级自己来
    /// </summary>
    public class CompGaiaPortal : ThingComp
    {
        private enum Phase
        {
            Dormant,
            Waves,
            Opened,
            Dimmed
        }

        private Phase phase = Phase.Dormant;
        private float investigationProgress;
        private int wavesDone;
        private int nextWaveTick = -1;
        private int cooldownUntil = -1;
        private bool autoInvestigate;
        private List<Pawn> spawnedPawns = new List<Pawn>();

        private CompProperties_GaiaPortal Props => (CompProperties_GaiaPortal)props;

        private Map Map => parent.Map;

        public int TotalWaves => Props.waveSizes == null ? 0 : Props.waveSizes.Count;

        public bool NeedsInvestigation => phase == Phase.Dormant;

        public bool AutoInvestigate => autoInvestigate;

        public float InvestigationPercent
        {
            get
            {
                int need = Props.investigationTicks;
                if (need <= 0)
                {
                    return 1f;
                }
                return investigationProgress <= 0f ? 0f : investigationProgress / need;
            }
        }

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
                        phase = Phase.Dormant;
                        investigationProgress = 0f;
                        wavesDone = 0;
                        nextWaveTick = -1;
                        Log.Message("[Botania] 盖亚传送门冷却结束，可以重新派人调查");
                    }
                    break;

                case Phase.Dormant:
                    TickInvestigation();
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

        // ---------------- 调查（靠「人站在门里」推进） ----------------

        private void TickInvestigation()
        {
            int workers = CountInvestigatorsInside();
            if (workers <= 0)
            {
                return;
            }

            float work = 0f;
            List<Pawn> colonists = Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (!IsInvestigating(p))
                {
                    continue;
                }
                float speed = p.GetStatValue(StatDefOf.ResearchSpeed);
                work += speed <= 0f ? 0.1f : speed;
            }

            investigationProgress += work;
            if (investigationProgress >= Props.investigationTicks)
            {
                Activate();
            }
        }

        private int CountInvestigatorsInside()
        {
            int n = 0;
            List<Pawn> colonists = Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (IsInvestigating(colonists[i]))
                {
                    n++;
                }
            }
            return n;
        }

        private bool IsInvestigating(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned)
            {
                return false;
            }
            CellRect rect = parent.OccupiedRect();
            return rect.Contains(p.Position);
        }

        private void Activate()
        {
            phase = Phase.Waves;
            investigationProgress = 0f;
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
            investigationProgress = 0f;
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

        /// <summary>传送门不可被攻击、不可被破坏。</summary>
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            absorbed = true;
        }

        // ---------------- 显示与交互 ----------------

        public override string CompInspectStringExtra()
        {
            switch (phase)
            {
                case Phase.Dormant:
                    return "Botania.GaiaInspect.Dormant".Translate(
                        (int)(InvestigationPercent * 100f),
                        autoInvestigate ? "Botania.GaiaAuto.On".Translate() : "Botania.GaiaAuto.Off".Translate());
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

                yield return new Command_Toggle
                {
                    defaultLabel = "Botania.GaiaAuto.Label".Translate(),
                    defaultDesc = "Botania.GaiaAuto.Desc".Translate(),
                    icon = TexCommand.ToggleVent,
                    isActive = () => autoInvestigate,
                    toggleAction = () =>
                    {
                        autoInvestigate = !autoInvestigate;
                        Messages.Message(
                            (autoInvestigate ? "Botania.GaiaAuto.OnMsg" : "Botania.GaiaAuto.OffMsg").Translate(),
                            new LookTargets(parent), MessageTypeDefOf.NeutralEvent, false);
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

        /// <summary>派指定小人去调查：走到门中央并原地等待（进度由 CompTick 按「站在门里」累计）。</summary>
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

            IntVec3 stand = FindStandCell(parent);
            if (!stand.IsValid)
            {
                Messages.Message("Botania.GaiaInvestigate.NoCell".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Botania_InvestigateGaiaPortal");
            if (def != null)
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, parent, stand), JobTag.Misc);
            }
            else
            {
                // 兜底：没有 JobDef 就只让他走过去待着（站在门里同样会涨进度）
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, stand), JobTag.Misc);
            }

            Messages.Message("Botania.GaiaInvestigate.Ordered".Translate(pawn.LabelShort),
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 站位：3×3 传送门中央那一格（现在可通行，小人能直接站进去）。
        /// </summary>
        public static IntVec3 FindStandCell(Thing portal)
        {
            if (portal == null || !portal.Spawned)
            {
                return IntVec3.Invalid;
            }
            IntVec3 center = portal.OccupiedRect().CenterCell;
            if (center.InBounds(portal.Map) && center.Walkable(portal.Map))
            {
                return center;
            }
            return CellFinder.RandomClosewalkCellNear(center, portal.Map, 3,
                c => c.Walkable(portal.Map) && !c.Fogged(portal.Map));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref phase, "gaiaPhase", Phase.Dormant);
            Scribe_Values.Look(ref investigationProgress, "gaiaInvestigation", 0f);
            Scribe_Values.Look(ref wavesDone, "gaiaWavesDone", 0);
            Scribe_Values.Look(ref nextWaveTick, "gaiaNextWaveTick", -1);
            Scribe_Values.Look(ref cooldownUntil, "gaiaCooldownUntil", -1);
            Scribe_Values.Look(ref autoInvestigate, "gaiaAutoInvestigate", false);
            Scribe_Collections.Look(ref spawnedPawns, "gaiaSpawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spawnedPawns == null)
            {
                spawnedPawns = new List<Pawn>();
            }
        }
    }
}
