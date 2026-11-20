using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Botania
{
    /// <summary>
    /// 盖亚·世界之魂（Boss）本体逻辑。
    ///
    /// 为什么用 Pawn 子类而不是 ThingComp：
    ///  · 小人的伤害管线走 Pawn.PreApplyDamage，ThingComp 的前置伤害钩子在 Pawn 上不保证被调用
    ///  · 小人的属性面板文案由 Pawn.GetInspectString 自己拼，不包含 CompInspectStringExtra
    ///
    /// 战斗循环（docs/盖亚Boss设计文档.md）：
    ///   带盾出场 → 破盾 → 瘫痪 20 秒（受伤 ×1.5）→ 瘫痪结束护盾瞬满
    ///   → 打到 66% / 33% → 锁血 5 秒（无敌 + 眩晕）→ 形态升级、护盾瞬满
    /// 技能（本文件）：
    ///   I  大地之怒 / 根须缠绕；II + 魔力射线 + 召唤盖亚之种；III + 世界之魂（一次性）
    /// </summary>
    [StaticConstructorOnStartup]
    public class Pawn_GaiaBoss : Pawn
    {
        private const string ShieldHediffDefName = "Botania_GaiaShield";

        private static readonly float[] PhaseShields = { 300f, 400f, 500f };
        private static readonly float[] PhaseThresholds = { 0.66f, 0.33f };

        // 原版护盾腰带用的那张气泡贴图
        private static readonly Material BubbleMat =
            MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent);

        private static readonly Color WarnColor = new Color(0.95f, 0.25f, 0.2f, 0.45f);
        private static readonly Color BeamColor = new Color(1f, 0.35f, 0.15f, 0.55f);
        private static readonly Color RootColor = new Color(0.45f, 0.9f, 0.4f, 0.5f);
        private static readonly Color SoulColor = new Color(0.95f, 0.1f, 0.1f, 0.5f);

        private float shield = 300f;
        private int phaseIndex;
        private int invulnerableUntil = -1;
        private int paralyzedUntil = -1;
        private bool dropsGiven;
        private bool initialized;
        private bool revertFactionNotified;

        // ---------------- 技能状态 ----------------
        private GaiaSkill currentSkill;
        private int skillStageStart;
        private int skillWarnUntil;
        private List<IntVec3> skillCells = new List<IntVec3>();
        private Color skillColor = WarnColor;
        private List<Pawn> entangleTargets = new List<Pawn>();
        private int[] skillCooldownUntil = new int[6];

        private bool worldSoulActive;
        private float worldSoulRadius;
        private int worldSoulLastMsgTier = 20;
        private HashSet<Pawn> worldSoulHit = new HashSet<Pawn>();
        private bool worldSoulDone;
        private bool seedsPhase2Done;
        private bool seedsPhase3Done;

        private Thing currentTarget;
        private int nextTargetScanTick = -1;
        private int nextPathTick = -1;
        private int nextStuckCheckTick = -1;
        private IntVec3 lastCheckPos = IntVec3.Invalid;
        private bool pathingStuck;

        /// <summary>走近到这个距离后站定放技能（JobGiver 与技能门控共用）。</summary>
        public const float EngageRange = 4.5f;

        /// <summary>当前 AI 目标（供 JobGiver_GaiaCombat 读取）。</summary>
        public Thing AITarget => currentTarget;

        /// <summary>是否正在施法/蓄力（此时 AI 应站定不动）。</summary>
        public bool IsBusyCasting => worldSoulActive || currentSkill != GaiaSkill.None;

        /// <summary>目标不可达时由 JobGiver 通知，让技能可以按射程远程出手。</summary>
        public void NotifyPathBlocked()
        {
            pathingStuck = true;
        }

        private int Now => Find.TickManager.TicksGame;

        public float Shield => shield;

        public float ShieldMax
        {
            get
            {
                int idx = phaseIndex < 0 ? 0 : (phaseIndex >= PhaseShields.Length ? PhaseShields.Length - 1 : phaseIndex);
                return PhaseShields[idx];
            }
        }

        public int PhaseNumber => phaseIndex + 1;

        public bool IsParalyzed => paralyzedUntil > Now;

        public bool IsInvulnerable => invulnerableUntil > Now;

        public bool IsCastingWorldSoul => worldSoulActive;

        public GaiaSkill CurrentSkill => worldSoulActive ? GaiaSkill.WorldSoul : currentSkill;

        // ---------------- 生命周期 ----------------

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!initialized)
            {
                initialized = true;
                shield = ShieldMax;
                SyncShieldHediff();
                EnsureUnstoppable();
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (Dead)
            {
                return;
            }

            // 盖亚不能被收服：一旦阵营不是盖亚，立刻改回去
            Faction gaiaFaction = GaiaFaction.Get();
            if (gaiaFaction != null && Faction != gaiaFaction)
            {
                SetFaction(gaiaFaction);
                if (!revertFactionNotified)
                {
                    revertFactionNotified = true;
                    Messages.Message("Botania.GaiaBossCannotRecruit".Translate(),
                        new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
                }
            }
            // 兜底：万一进入倒地状态（断肢/异常途径），把「盖亚之躯」补回来
            if (Downed)
            {
                EnsureUnstoppable();
                Log.Warning("[Botania] Boss 进入了倒地状态，已重新施加盖亚之躯");
            }

            // 定期复查：盖亚与玩家的关系必须一直是敌对
            if (Now % 250 == 0)
            {
                GaiaFaction.EnsureHostile(gaiaFaction);
            }

            // 瘫痪结束：护盾瞬满
            if (paralyzedUntil > 0 && Now >= paralyzedUntil)
            {
                paralyzedUntil = -1;
                shield = ShieldMax;
                SyncShieldHediff();
                Messages.Message("Botania.GaiaBossShieldBack".Translate(),
                    new LookTargets(this), MessageTypeDefOf.ThreatBig, false);
            }

            if (invulnerableUntil > 0 && Now >= invulnerableUntil)
            {
                invulnerableUntil = -1;
            }

            // 每 30 tick 同步一次护盾 Hediff：防止出生瞬间 health 尚未就绪导致没挂上
            if (Now % 30 == 0)
            {
                SyncShieldHediff();
            }

            TickCombat();
            TickSkills();
            TickSwingMote();

            if (!worldSoulActive)
            {
                TryAdvancePhase();
            }
        }

        // ---------------- 伤害 ----------------

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            if (absorbed || Dead)
            {
                return;
            }

            // 世界之魂蓄力：完全无敌（连护盾都不掉）
            if (worldSoulActive)
            {
                absorbed = true;
                return;
            }

            // 锁血：完全免疫
            if (IsInvulnerable)
            {
                absorbed = true;
                return;
            }

            float amount = dinfo.Amount;

            // 瘫痪：受伤 ×1.5
            if (IsParalyzed)
            {
                amount *= 1.5f;
            }

            // 有盾：80% 减伤，并从护盾里扣
            if (shield > 0f)
            {
                float reduced = amount * 0.2f;
                shield -= reduced;
                if (shield <= 0f)
                {
                    shield = 0f;
                    StartParalysis();
                }
                amount = reduced;
            }

            dinfo.SetAmount(amount);
            SyncShieldHediff();
        }

        private void StartParalysis()
        {
            paralyzedUntil = Now + 1200;
            if (stances != null && stances.stunner != null)
            {
                stances.stunner.StunFor(1200, this, false, true, true);
            }
            SyncShieldHediff();
            Messages.Message(
                "Botania.GaiaBossShieldBroken.Text".Translate(),
                new LookTargets(this),
                MessageTypeDefOf.NeutralEvent);
        }

        // ---------------- 阶段 ----------------

        private void TryAdvancePhase()
        {
            if (IsInvulnerable)
            {
                return;
            }
            if (phaseIndex >= PhaseThresholds.Length)
            {
                return;
            }
            float pct = health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (pct > PhaseThresholds[phaseIndex])
            {
                return;
            }

            phaseIndex++;
            invulnerableUntil = Now + 300;
            paralyzedUntil = -1;
            shield = ShieldMax;
            SyncShieldHediff();

            if (stances != null && stances.stunner != null)
            {
                stances.stunner.StunFor(300, this, false, true, true);
            }

            Messages.Message(
                "Botania.GaiaBossPhase.Text".Translate(PhaseNumber),
                new LookTargets(this),
                MessageTypeDefOf.NeutralEvent);

            // 进入阶段 II / III 的一次性技能
            if (phaseIndex == 1 && !seedsPhase2Done)
            {
                seedsPhase2Done = true;
                CastSummonSeeds();
            }
            if (phaseIndex == 2 && !seedsPhase3Done)
            {
                seedsPhase3Done = true;
                CastSummonSeeds();
            }
            if (phaseIndex >= 2 && !worldSoulDone)
            {
                worldSoulDone = true;
                StartWorldSoul();
            }
        }

        // ---------------- 技能调度 ----------------

        private bool CanAct => !Dead && !IsInvulnerable && !IsParalyzed && !worldSoulActive;

        private void TickSkills()
        {
            if (worldSoulActive)
            {
                AdvanceWorldSoul();
                return;
            }
            if (currentSkill != GaiaSkill.None)
            {
                AdvanceSkill();
                return;
            }
            TryPickSkill();
        }

        /// <summary>
        /// 走位：目标距离 &gt; EngageRange 就往它附近走（每 30 tick 重下任务），
        /// 到了就站定准备放技能。施法期间不动。
        /// </summary>
        private void TickCombat()
        {
            if (Dead || Map == null)
            {
                return;
            }

            if (Now >= nextTargetScanTick)
            {
                nextTargetScanTick = Now + 30;
                currentTarget = FindNearestAttackTarget();
            }
            CheckStuck();

            if (currentSkill != GaiaSkill.None || worldSoulActive || !CanAct)
            {
                return;
            }
            if (currentTarget == null || currentTarget.Destroyed || !currentTarget.Spawned)
            {
                return;
            }

            float dist = currentTarget.Position.DistanceTo(Position);
            if (dist <= EngageRange)
            {
                pather?.StopDead();
                return;
            }

            // 移动由思考树 + JobGiver_GaiaCombat 负责，这里不再抢 pather
            nextPathTick = Now + 30;
        }

        /// <summary>用原版仇恨缓存找最近的敌对目标（含炮塔、可攻击建筑）。</summary>
        private Thing FindNearestAttackTarget()
        {
            if (Map == null || Faction == null)
            {
                return null;
            }
            HashSet<IAttackTarget> targets = Map.attackTargetsCache.TargetsHostileToFaction(Faction);
            if (targets == null)
            {
                return null;
            }
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (IAttackTarget at in targets)
            {
                Thing th = at?.Thing;
                if (th == null || th == this || th.Destroyed || !th.Spawned)
                {
                    continue;
                }
                if (!th.HostileTo(Faction))
                {
                    continue;
                }
                float d = th.Position.DistanceTo(Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = th;
                }
            }
            return best;
        }

        /// <summary>卡住检测：连续 120 tick 原地不动但想追人时，允许在技能射程内远程出手。</summary>
        private void CheckStuck()
        {
            if (Now < nextStuckCheckTick)
            {
                return;
            }
            nextStuckCheckTick = Now + 120;
            bool moved = lastCheckPos.IsValid && !Position.InHorDistOf(lastCheckPos, 0.6f);
            pathingStuck = !moved && currentTarget != null
                           && currentTarget.Position.DistanceTo(Position) > EngageRange;
            lastCheckPos = Position;
        }

        private void TryPickSkill()
        {
            if (!CanAct)
            {
                return;
            }

            // 必须走近到攻击距离内才放技能（远了先走位）
            if (currentTarget == null || currentTarget.Destroyed || !currentTarget.Spawned)
            {
                return;
            }
            float distToTarget = currentTarget.Position.DistanceTo(Position);
            // 正常：走近 4 格左右才出手；走不过去（卡住）时允许在技能射程内远程打
            if (!pathingStuck && distToTarget > EngageRange + 1.5f)
            {
                return;
            }

            // 阶段 I：大地之怒（4s）、根须缠绕（6s）
            if (Now >= skillCooldownUntil[(int)GaiaSkill.EarthWrath] && TryFindConeTarget(out IntVec3 dir))
            {
                StartEarthWrath(dir);
                return;
            }
            if (Now >= skillCooldownUntil[(int)GaiaSkill.Entangle] && TryFindEntangleTargets(out List<Pawn> targets))
            {
                StartEntangle(targets);
                return;
            }
            // 阶段 II 起：魔力射线（5s）
            if (phaseIndex >= 1 && Now >= skillCooldownUntil[(int)GaiaSkill.ManaBeam] && TryFindBeamTarget(out IntVec3 beamTarget))
            {
                StartManaBeam(beamTarget);
            }
        }

        private void FreezePosition()
        {
            pather?.StopDead();
        }

        private List<Pawn> PlayerPawnsInRange(float range, out Pawn nearest, out Pawn farthest)
        {
            nearest = null;
            farthest = null;
            List<Pawn> result = new List<Pawn>();
            float nearestDist = float.MaxValue;
            float farthestDist = -1f;
            Map map = Map;
            if (map == null)
            {
                return result;
            }
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p == this || p.Dead || p.Downed || !p.Spawned)
                {
                    continue;
                }
                // 只打敌对目标（收服/同阵营一律排除）
                if (p.Faction == null || !p.HostileTo(this))
                {
                    continue;
                }
                float d = p.Position.DistanceTo(Position);
                if (d > range)
                {
                    continue;
                }
                if (!GenSight.LineOfSight(Position, p.Position, map))
                {
                    continue;
                }
                result.Add(p);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = p;
                }
                if (d > farthestDist)
                {
                    farthestDist = d;
                    farthest = p;
                }
            }
            return result;
        }

        private bool TryFindConeTarget(out IntVec3 dir)
        {
            dir = IntVec3.Invalid;
            if (currentTarget == null)
            {
                return false;
            }
            dir = currentTarget.Position;
            return true;
        }

        private bool TryFindEntangleTargets(out List<Pawn> targets)
        {
            targets = new List<Pawn>();
            List<Pawn> pawns = HostilePawnsInRange(GaiaSkills.EntangleRange);
            if (pawns.Count == 0)
            {
                return false;
            }
            pawns.Sort((a, b) => a.Position.DistanceToSquared(Position).CompareTo(b.Position.DistanceToSquared(Position)));
            for (int i = 0; i < pawns.Count && i < GaiaSkills.EntangleMaxTargets; i++)
            {
                targets.Add(pawns[i]);
            }
            return targets.Count > 0;
        }

        /// <summary>范围内的敌对 Pawn（根须缠绕这类只对生物有效的技能用）。</summary>
        private List<Pawn> HostilePawnsInRange(float range)
        {
            List<Pawn> result = new List<Pawn>();
            if (Map == null || Faction == null)
            {
                return result;
            }
            HashSet<IAttackTarget> targets = Map.attackTargetsCache.TargetsHostileToFaction(Faction);
            if (targets == null)
            {
                return result;
            }
            foreach (IAttackTarget at in targets)
            {
                Pawn p = at?.Thing as Pawn;
                if (p == null || p == this || p.Dead || p.Downed || !p.Spawned)
                {
                    continue;
                }
                if (!p.HostileTo(Faction))
                {
                    continue;
                }
                if (p.Position.DistanceTo(Position) > range)
                {
                    continue;
                }
                if (!GenSight.LineOfSight(Position, p.Position, Map))
                {
                    continue;
                }
                result.Add(p);
            }
            return result;
        }

        private bool TryFindBeamTarget(out IntVec3 target)
        {
            target = IntVec3.Invalid;
            if (currentTarget == null)
            {
                return false;
            }
            target = currentTarget.Position;
            return true;
        }

        private void StartEarthWrath(IntVec3 dir)
        {
            currentSkill = GaiaSkill.EarthWrath;
            skillStageStart = Now;
            skillWarnUntil = Now + GaiaSkills.EarthWrathWarn;
            skillCells = GaiaSkills.ConeCells(this, dir, GaiaSkills.EarthWrathRadius, GaiaSkills.EarthWrathHalfAngle);
            skillColor = WarnColor;
            FreezePosition();
        }

        private void StartEntangle(List<Pawn> targets)
        {
            currentSkill = GaiaSkill.Entangle;
            skillStageStart = Now;
            skillWarnUntil = Now + GaiaSkills.EntangleWarn;
            entangleTargets = targets;
            skillCells = new List<IntVec3>();
            for (int i = 0; i < targets.Count; i++)
            {
                skillCells.Add(targets[i].Position);
            }
            skillColor = RootColor;
            FreezePosition();
        }

        private void StartManaBeam(IntVec3 target)
        {
            currentSkill = GaiaSkill.ManaBeam;
            skillStageStart = Now;
            skillWarnUntil = Now + GaiaSkills.ManaBeamCharge;
            skillCells = GaiaSkills.BeamCells(this, target, GaiaSkills.ManaBeamRange);
            skillColor = BeamColor;
            FreezePosition();
        }

        private void AdvanceSkill()
        {
            if (Dead)
            {
                EndSkill(GaiaSkill.None, 0);
                return;
            }

            FreezePosition();

            if (Now < skillWarnUntil)
            {
                return;
            }

            switch (currentSkill)
            {
                case GaiaSkill.EarthWrath:
                    DoEarthWrath();
                    EndSkill(GaiaSkill.EarthWrath, GaiaSkills.EarthWrathCd);
                    break;
                case GaiaSkill.Entangle:
                    DoEntangle();
                    EndSkill(GaiaSkill.Entangle, GaiaSkills.EntangleCd);
                    break;
                case GaiaSkill.ManaBeam:
                    DoManaBeam();
                    EndSkill(GaiaSkill.ManaBeam, GaiaSkills.ManaBeamCd);
                    break;
                default:
                    EndSkill(GaiaSkill.None, 0);
                    break;
            }
        }

        private void EndSkill(GaiaSkill skill, int cooldownTicks)
        {
            if (skill != GaiaSkill.None)
            {
                skillCooldownUntil[(int)skill] = Now + cooldownTicks;
            }
            currentSkill = GaiaSkill.None;
            skillCells = new List<IntVec3>();
            entangleTargets = new List<Pawn>();
        }

        // ---------------- 各技能效果 ----------------

        private void DoEarthWrath()
        {
            List<IntVec3> cells = skillCells;
            Map map = Map;
            if (map == null)
            {
                return;
            }

            GaiaSkills.DamageHostileThingsInCells(Map, Faction, cells, DamageDefOf.Blunt,
                GaiaSkills.EarthWrathDamage, this, 0.5f, true, Position);

            // 视觉：命中区域撒地面裂纹
            for (int i = 0; i < cells.Count && i < 10; i += Math.Max(1, cells.Count / 10))
            {
                IntVec3 c = cells[i];
                SpawnMoteAt("Botania_Mote_GaiaCrack", c.ToVector3Shifted(), 1f);
            }

            GenExplosion.DoExplosion(Position, map, 1.5f, DamageDefOf.Blunt, this, 0,
                0f, doSoundEffects: false);
            Messages.Message("Botania.GaiaSkillEarthWrath".Translate(),
                new LookTargets(Position, map), MessageTypeDefOf.NeutralEvent, false);
        }

        private void DoEntangle()
        {
            HediffDef rootDef = DefDatabase<HediffDef>.GetNamedSilentFail("Botania_GaiaRooted");
            if (rootDef == null)
            {
                Log.Error("[Botania] 找不到 HediffDef Botania_GaiaRooted");
                return;
            }
            // 生效时必须重新核对：只有仍然站在绿色预警格子里的目标才会被缠住。
            // （预警 2 秒内跑出去的应该免疫，否则「躲开」毫无意义）
            HashSet<IntVec3> warnedCells = new HashSet<IntVec3>(skillCells);
            int hitCount = 0;
            for (int i = 0; i < entangleTargets.Count; i++)
            {
                Pawn p = entangleTargets[i];
                if (p == null || p.Dead || p.Downed || !p.Spawned)
                {
                    continue;
                }
                if (!warnedCells.Contains(p.Position))
                {
                    continue;   // 已经走出预警范围：躲开了
                }
                Hediff hediff = HediffMaker.MakeHediff(rootDef, p);
                hediff.Severity = 1f;
                p.health.AddHediff(hediff);
                hitCount++;
            }
            Log.Message($"[Botania] 根须缠绕：命中 {hitCount} / {entangleTargets.Count} 个目标（其余已离开预警格）");
            Messages.Message("Botania.GaiaSkillEntangle".Translate(),
                new LookTargets(Position, Map), MessageTypeDefOf.NeutralEvent, false);
        }

        private void DoManaBeam()
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }
            List<IntVec3> cells = skillCells;
            GaiaSkills.DamageHostileThingsInCells(map, Faction, cells, DamageDefOf.Burn,
                GaiaSkills.ManaBeamDamage, this, 0.3f, false, Position);

            // 视觉：沿射线撒光斑
            for (int i = 0; i < cells.Count; i += Math.Max(1, cells.Count / 14))
            {
                SpawnMoteAt("Botania_Mote_GaiaSpark", cells[i].ToVector3Shifted(), 1.2f);
            }
            Messages.Message("Botania.GaiaSkillManaBeam".Translate(),
                new LookTargets(Position, map), MessageTypeDefOf.NeutralEvent, false);
        }

        private void CastSummonSeeds()
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(GaiaSkills.SeedPawnKind);
            if (kind == null)
            {
                Log.Error("[Botania] 找不到召唤小怪种族: " + GaiaSkills.SeedPawnKind);
                return;
            }
            for (int i = 0; i < GaiaSkills.SummonSeedsCount; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(Position, map, 5,
                    c => c.Walkable(map) && !c.Fogged(map));
                if (!cell.IsValid)
                {
                    continue;
                }
                Pawn seed = PawnGenerator.GeneratePawn(kind, GaiaFaction.Get());
                if (seed != null)
                {
                    GenSpawn.Spawn(seed, cell, map);
                }
            }
            Messages.Message("Botania.GaiaSkillSummon".Translate(),
                new LookTargets(Position, map), MessageTypeDefOf.NeutralEvent, false);
        }

        // ---------------- 世界之魂 ----------------

        private void StartWorldSoul()
        {
            worldSoulActive = true;
            worldSoulRadius = 1f;
            worldSoulLastMsgTier = 20;
            worldSoulHit = new HashSet<Pawn>();
            skillStageStart = Now;
            skillCells = new List<IntVec3>();
            skillColor = SoulColor;
            FreezePosition();
            Messages.Message("Botania.GaiaWorldSoulStart".Translate(),
                new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
        }

        private void AdvanceWorldSoul()
        {
            FreezePosition();

            Map map = Map;
            if (map == null || Dead)
            {
                worldSoulActive = false;
                return;
            }

            int elapsed = Now - skillStageStart;
            int remainSec = Mathf.CeilToInt((GaiaSkills.WorldSoulCharge - elapsed) / 60f);

            // 20 / 10 / 3 秒各提示一次（普通消息）
            if (remainSec <= 10 && worldSoulLastMsgTier > 10)
            {
                worldSoulLastMsgTier = 10;
                Messages.Message("Botania.GaiaWorldSoulTen".Translate(),
                    new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
            }
            else if (remainSec <= 3 && worldSoulLastMsgTier > 3)
            {
                worldSoulLastMsgTier = 3;
                Messages.Message("Botania.GaiaWorldSoulThree".Translate(),
                    new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
            }

            // 波前推进：每 6 tick（0.1 秒）推一次，画一圈红
            if (Now % 6 == 0)
            {
                float prev = worldSoulRadius;
                worldSoulRadius = Mathf.Min(GaiaSkills.WorldSoulMaxRadius,
                    worldSoulRadius + GaiaSkills.WorldSoulSpeedPerTick * 6f);

                List<IntVec3> ring = GaiaSkills.RingCells(map, Position, prev, worldSoulRadius);
                skillCells = ring;

                // 命中判定：新扫过的格子里、且从 boss 有视线的目标
                for (int i = 0; i < ring.Count; i++)
                {
                    List<Thing> things = ring[i].GetThingList(map);
                    for (int j = 0; j < things.Count; j++)
                    {
                        Pawn p = things[j] as Pawn;
                        if (p == null || p.Dead || worldSoulHit.Contains(p))
                        {
                            continue;
                        }
                        if (!GenSight.LineOfSight(Position, p.Position, map))
                        {
                            continue;   // 墙后：完全免伤
                        }
                        worldSoulHit.Add(p);
                        p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, GaiaSkills.WorldSoulDamage, 1f, -1f, this));
                        if (p.stances != null && p.stances.stunner != null)
                        {
                            p.stances.stunner.StunFor(GaiaSkills.WorldSoulStunTicks, this, false, true, true);
                        }
                    }
                }
            }

            if (elapsed >= GaiaSkills.WorldSoulCharge || worldSoulRadius >= GaiaSkills.WorldSoulMaxRadius)
            {
                worldSoulActive = false;
                skillCells = new List<IntVec3>();
                Messages.Message("Botania.GaiaWorldSoulEnd".Translate(),
                    new LookTargets(this), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // ---------------- 绘制 ----------------

        private int nextSwingMoteTick;

        private void SpawnMoteAt(string defName, Vector3 pos, float scale = 1f)
        {
            Map map = Map;
            if (map == null || defName.NullOrEmpty())
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            try
            {
                MoteMaker.MakeStaticMote(pos, map, def, scale);
            }
            catch (Exception)
            {
                // 特效失败不影响机制
            }
        }

        /// <summary>近战挥击时在身前生成手臂弧光。</summary>
        private void TickSwingMote()
        {
            if (Now < nextSwingMoteTick)
            {
                return;
            }
            if (!(stances?.curStance is Stance_Busy busy) || busy.verb == null || busy.neverAimWeapon)
            {
                return;
            }
            nextSwingMoteTick = Now + 12;

            Vector3 dir = Vector3.forward;
            if (busy.focusTarg.IsValid)
            {
                dir = (busy.focusTarg.Cell.ToVector3() - Position.ToVector3()).Yto0();
            }
            else if (Rotation == Rot4.North)
            {
                dir = Vector3.forward;
            }
            else if (Rotation == Rot4.East)
            {
                dir = Vector3.right;
            }
            else if (Rotation == Rot4.West)
            {
                dir = Vector3.left;
            }
            else
            {
                dir = Vector3.back;
            }
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = Vector3.forward;
            }
            dir.Normalize();
            Vector3 pos = DrawPos + dir * 0.9f;
            SpawnMoteAt("Botania_Mote_GaiaSwing", pos, 1f);
        }

        /// <summary>预警范围：由 MapComponent_GaiaTelegraph 每帧读取并绘制。</summary>
        public bool TryGetTelegraph(out List<IntVec3> cells, out Color color)
        {
            cells = skillCells;
            color = skillColor;
            return cells != null && cells.Count > 0;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            if (shield <= 0f)
            {
                return;
            }

            try
            {
                float frac = Mathf.Clamp01(shield / Mathf.Max(1f, ShieldMax));
                float size = Mathf.Lerp(3.6f, 4.6f, frac);
                Vector3 pos = drawLoc;
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(pos, Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up),
                    new Vector3(size, 1f, size));
                Graphics.DrawMesh(MeshPool.plane10, matrix, BubbleMat, 0);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Botania] 护盾气泡绘制失败：" + e, 8172631);
            }
        }

        // ---------------- 界面 ----------------

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            string extra = worldSoulActive
                ? "Botania.GaiaBossInspect.WorldSoul".Translate(
                    Mathf.CeilToInt((GaiaSkills.WorldSoulCharge - (Now - skillStageStart)) / 60f).ToString())
                : (IsParalyzed
                    ? "Botania.GaiaBossInspect.Paralyzed".Translate()
                    : (shield > 0f
                        ? "Botania.GaiaBossInspect.Shield".Translate(
                            Mathf.RoundToInt(shield).ToString(), Mathf.RoundToInt(ShieldMax).ToString())
                        : "Botania.GaiaBossInspect.NoShield".Translate()));
            string phase = "Botania.GaiaBossInspect.Phase".Translate(PhaseNumber);
            return baseText.NullOrEmpty() ? phase + "\n" + extra : baseText + "\n" + phase + "\n" + extra;
        }

        // ---------------- Gizmo（像原版小人技能一样挂在 Boss 身上） ----------------

        private static Texture2D SkillIcon(string name)
        {
            Texture2D tex = ContentFinder<Texture2D>.Get("UI/Gaia/" + name, false);
            return tex ?? BaseContent.BadTex;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            // 技能与测试工具只给开发者用：必须同时开「开发者模式」和 god mode
            if (!Prefs.DevMode || !DebugSettings.godMode)
            {
                yield break;
            }

            // 五个技能：开发者模式下选中 Boss 就能看到
            yield return EarthWrathGizmo();
            yield return SkillTargetGizmo(GaiaSkill.Entangle, "Botania.GaiaSkillEntangle.Label",
                "Entangle", "Botania.GaiaSkillEntangle.Desc", GaiaSkills.EntangleRange);
            yield return SkillTargetGizmo(GaiaSkill.ManaBeam, "Botania.GaiaSkillManaBeam.Label",
                "ManaBeam", "Botania.GaiaSkillManaBeam.Desc", GaiaSkills.ManaBeamRange);
            yield return SkillGizmo(GaiaSkill.SummonSeeds, "Botania.GaiaSkillSummon.Label",
                "SummonSeeds", "Botania.GaiaSkillSummon.Desc");
            yield return SkillGizmo(GaiaSkill.WorldSoul, "Botania.GaiaWorldSoulStart.Label",
                "WorldSoul", "Botania.GaiaWorldSoulStart.Desc");

            // 开发工具：同样挂在 Boss 身上
            yield return new Command_Action
            {
                defaultLabel = "[DEV] 清除冷却",
                defaultDesc = "把所有技能冷却清零。",
                icon = TexCommand.ClearPrioritizedWork,
                action = DebugClearCooldowns
            };
            yield return new Command_Action
            {
                defaultLabel = "[DEV] 打断当前技能",
                defaultDesc = "取消正在进行的预警或蓄力。",
                icon = TexCommand.ClearPrioritizedWork,
                action = DebugCancelSkill
            };
            yield return new Command_Action
            {
                defaultLabel = "[DEV] 查看技能状态",
                defaultDesc = "把阶段、冷却、当前技能打印到日志。",
                icon = TexCommand.GatherSpotActive,
                action = () => Log.Message("[Botania] " + DebugStatus())
            };
        }

        /// <summary>大地之怒：点按钮后再点地图选方向（扇形朝该点展开）。</summary>
        private Gizmo EarthWrathGizmo()
        {
            int remain = Mathf.Max(0, skillCooldownUntil[(int)GaiaSkill.EarthWrath] - Now);
            Command_Target cmd = new Command_Target();
            cmd.defaultLabel = "Botania.GaiaSkillEarthWrath.Label".Translate();
            string desc = "Botania.GaiaSkillEarthWrath.Desc".Translate();
            if (CurrentSkill == GaiaSkill.EarthWrath)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Casting".Translate();
            }
            else if (remain > 0)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Cooldown".Translate(remain.ToStringTicksToPeriod());
            }
            cmd.defaultDesc = desc;
            cmd.icon = SkillIcon("EarthWrath");
            cmd.targetingParams = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetMechs = false,
                canTargetSelf = false,
                validator = ti => ti.Cell.DistanceTo(Position) <= GaiaSkills.EarthWrathRadius + 6f
            };
            cmd.action = ti =>
            {
                if (Prefs.DevMode)
                {
                    DebugForceEarthWrathAt(ti.Cell);
                }
                else
                {
                    Messages.Message("Botania.GaiaSkillCommon.BossOnly".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
            };
            return cmd;
        }

        /// <summary>需要玩家点一个落点的技能（根须缠绕 / 魔力射线）。</summary>
        private Gizmo SkillTargetGizmo(GaiaSkill skill, string labelKey, string iconName, string descKey, float maxDist)
        {
            int remain = Mathf.Max(0, skillCooldownUntil[(int)skill] - Now);
            Command_Target cmd = new Command_Target();
            cmd.defaultLabel = labelKey.Translate();
            string desc = descKey.Translate();
            if (CurrentSkill == skill)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Casting".Translate();
            }
            else if (remain > 0)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Cooldown".Translate(remain.ToStringTicksToPeriod());
            }
            cmd.defaultDesc = desc;
            cmd.icon = SkillIcon(iconName);
            cmd.targetingParams = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetMechs = false,
                canTargetSelf = false,
                validator = ti => ti.Cell.DistanceTo(Position) <= maxDist
            };
            cmd.action = ti =>
            {
                if (Prefs.DevMode && DebugSettings.godMode)
                {
                    DebugForceSkillAt(skill, ti.Cell);
                }
            };
            return cmd;
        }

        private Gizmo SkillGizmo(GaiaSkill skill, string labelKey, string iconName, string descKey)
        {
            int remain = Mathf.Max(0, skillCooldownUntil[(int)skill] - Now);
            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = labelKey.Translate();
            string desc = descKey.Translate();
            if (CurrentSkill == skill)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Casting".Translate();
            }
            else if (remain > 0)
            {
                desc += "\n\n" + "Botania.GaiaSkillCommon.Cooldown".Translate(remain.ToStringTicksToPeriod());
            }
            cmd.defaultDesc = desc;
            cmd.icon = SkillIcon(iconName);
            // 平常由 Boss 自己施放；玩家点按钮只在开发者模式下真的施放
            cmd.action = () =>
            {
                if (Prefs.DevMode)
                {
                    DebugForceSkill(skill);
                }
                else
                {
                    Messages.Message("Botania.GaiaSkillCommon.BossOnly".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
            };
            return cmd;
        }

        // ---------------- 开发者工具接口 ----------------

        public bool DebugForceSkill(GaiaSkill skill)
        {
            if (Dead || Map == null)
            {
                return false;
            }
            EndSkill(GaiaSkill.None, 0);
            switch (skill)
            {
                case GaiaSkill.EarthWrath:
                    StartEarthWrath(Position + IntVec3.North * 5);
                    return true;
                case GaiaSkill.Entangle:
                    if (!TryFindEntangleTargets(out List<Pawn> targets))
                    {
                        return false;
                    }
                    StartEntangle(targets);
                    return true;
                case GaiaSkill.ManaBeam:
                    if (!TryFindBeamTarget(out IntVec3 beamTarget))
                    {
                        return false;
                    }
                    StartManaBeam(beamTarget);
                    return true;
                case GaiaSkill.SummonSeeds:
                    CastSummonSeeds();
                    return true;
                case GaiaSkill.WorldSoul:
                    StartWorldSoul();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>开发者：朝/在指定格子施放技能（根须缠绕取落点附近目标，魔力射线朝该点打线）。</summary>
        public bool DebugForceSkillAt(GaiaSkill skill, IntVec3 target)
        {
            if (Dead || Map == null)
            {
                return false;
            }
            EndSkill(GaiaSkill.None, 0);
            switch (skill)
            {
                case GaiaSkill.EarthWrath:
                    StartEarthWrath(target);
                    return true;
                case GaiaSkill.ManaBeam:
                    StartManaBeam(target);
                    return true;
                case GaiaSkill.Entangle:
                {
                    List<Pawn> picked = new List<Pawn>();
                    List<Pawn> all = HostilePawnsInRange(GaiaSkills.EntangleRange);
                    all.Sort((a, b) => a.Position.DistanceToSquared(target).CompareTo(b.Position.DistanceToSquared(target)));
                    for (int i = 0; i < all.Count && picked.Count < GaiaSkills.EntangleMaxTargets; i++)
                    {
                        if (all[i].Position.DistanceTo(target) <= 3.5f)
                        {
                            picked.Add(all[i]);
                        }
                    }
                    if (picked.Count == 0)
                    {
                        return false;
                    }
                    StartEntangle(picked);
                    return true;
                }
                default:
                    return DebugForceSkill(skill);
            }
        }

        /// <summary>开发者：朝指定格子施放大地之怒。</summary>
        public bool DebugForceEarthWrathAt(IntVec3 target)
        {
            if (Dead || Map == null)
            {
                return false;
            }
            EndSkill(GaiaSkill.None, 0);
            StartEarthWrath(target);
            return true;
        }

        public void DebugClearCooldowns()
        {
            for (int i = 0; i < skillCooldownUntil.Length; i++)
            {
                skillCooldownUntil[i] = 0;
            }
        }

        public void DebugCancelSkill()
        {
            EndSkill(GaiaSkill.None, 0);
            worldSoulActive = false;
            skillCells = new List<IntVec3>();
        }

        public string DebugStatus()
        {
            int cdEarth = Math.Max(0, skillCooldownUntil[(int)GaiaSkill.EarthWrath] - Now);
            int cdRoot = Math.Max(0, skillCooldownUntil[(int)GaiaSkill.Entangle] - Now);
            int cdBeam = Math.Max(0, skillCooldownUntil[(int)GaiaSkill.ManaBeam] - Now);
            return $"阶段={PhaseNumber} 当前技能={CurrentSkill} 护盾={shield:F0}/{ShieldMax:F0} " +
                   $"冷却[怒={cdEarth} 缠={cdRoot} 射={cdBeam}] 世界之魂={(worldSoulActive ? "进行中" : (worldSoulDone ? "已用" : "未用"))}";
        }

        // ---------------- 掉落 ----------------

        /// <summary>
        /// 掉落必须挂在 Kill 上：Pawn 死亡后会被 Corpse 替换并离开地图，
        /// Tick 不再执行，写在 Tick 里的掉落永远不会触发（实测踩过）。
        /// </summary>
        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            Map map = Map;
            IntVec3 pos = Position;
            base.Kill(dinfo, exactCulprit);
            if (map != null && pos.IsValid)
            {
                GiveDropsOnce(map, pos);
            }
        }

        private void GiveDropsOnce(Map map, IntVec3 pos)
        {
            if (dropsGiven)
            {
                return;
            }
            dropsGiven = true;

            SpawnItem(map, pos, "Botania_GaiaCore", 1);
            SpawnItem(map, pos, "Botania_PixieDust", Rand.RangeInclusive(8, 15));
            SpawnItem(map, pos, "Botania_TerrasteelIngot", Rand.RangeInclusive(3, 5));

            ResearchProjectDef gaia = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Botania_Gaia");
            bool unlocked = gaia != null && gaia.IsFinished;
            if (!unlocked || Rand.Chance(0.25f))
            {
                SpawnItem(map, pos, "Botania_GaiaSpirit", 1);
            }
        }

        private static void SpawnItem(Map map, IntVec3 pos, string defName, int count)
        {
            if (count <= 0)
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Warning("[Botania] 盖亚掉落找不到物品: " + defName);
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenSpawn.Spawn(thing, pos, map, WipeMode.VanishOrMoveAside);
        }

        // ---------------- 护盾 Hediff ----------------

        /// <summary>
        /// 「盖亚之躯」：把关键能力垫到 100%，保证 Boss 只会被打死、不会倒地。
        /// </summary>
        private void EnsureUnstoppable()
        {
            if (health == null || health.hediffSet == null)
            {
                return;
            }
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Botania_GaiaUnstoppable");
            if (def == null)
            {
                return;
            }
            for (int i = 0; i < health.hediffSet.hediffs.Count; i++)
            {
                if (health.hediffSet.hediffs[i].def == def)
                {
                    return;
                }
            }
            Hediff hediff = HediffMaker.MakeHediff(def, this);
            hediff.Severity = 1f;
            health.AddHediff(hediff);
        }

        private void SyncShieldHediff()
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(ShieldHediffDefName);
            if (def == null || health == null || health.hediffSet == null)
            {
                return;
            }
            Hediff existing = null;
            for (int i = 0; i < health.hediffSet.hediffs.Count; i++)
            {
                if (health.hediffSet.hediffs[i].def == def)
                {
                    existing = health.hediffSet.hediffs[i];
                    break;
                }
            }

            if (shield <= 0f)
            {
                if (existing != null)
                {
                    health.RemoveHediff(existing);
                }
                return;
            }

            float severity = shield / Mathf.Max(1f, ShieldMax);
            if (existing == null)
            {
                Hediff hediff = HediffMaker.MakeHediff(def, this);
                hediff.Severity = severity;
                health.AddHediff(hediff);
            }
            else
            {
                existing.Severity = severity;
            }
        }

        // ---------------- 存档 ----------------

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shield, "gaiaShield", 300f);
            Scribe_Values.Look(ref phaseIndex, "gaiaPhaseIndex", 0);
            Scribe_Values.Look(ref invulnerableUntil, "gaiaInvulnUntil", -1);
            Scribe_Values.Look(ref paralyzedUntil, "gaiaParalyzedUntil", -1);
            Scribe_Values.Look(ref dropsGiven, "gaiaDropsGiven", false);
            Scribe_Values.Look(ref initialized, "gaiaInitialized", false);
            Scribe_Values.Look(ref currentSkill, "gaiaCurrentSkill", GaiaSkill.None);
            Scribe_Values.Look(ref skillWarnUntil, "gaiaSkillWarnUntil", 0);
            Scribe_Values.Look(ref worldSoulActive, "gaiaWorldSoulActive", false);
            Scribe_Values.Look(ref worldSoulRadius, "gaiaWorldSoulRadius", 1f);
            Scribe_Values.Look(ref worldSoulDone, "gaiaWorldSoulDone", false);
            Scribe_Values.Look(ref seedsPhase2Done, "gaiaSeeds2", false);
            Scribe_Values.Look(ref seedsPhase3Done, "gaiaSeeds3", false);
            Scribe_Collections.Look(ref worldSoulHit, "gaiaWorldSoulHit", LookMode.Reference);
            Scribe_Values.Look(ref skillCooldownUntil, "gaiaSkillCd", null);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (skillCooldownUntil == null || skillCooldownUntil.Length != 6)
                {
                    skillCooldownUntil = new int[6];
                }
                if (worldSoulHit == null)
                {
                    worldSoulHit = new HashSet<Pawn>();
                }
            }
        }
    }
}
