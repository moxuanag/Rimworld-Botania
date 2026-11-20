using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚事件的全局状态与触发：
    ///  · 玩家完成研究 Botania_Terrasteel 后 3~5 天，天空砸下一块「盖亚碎块」
    ///  · **每个存档只砸一次**。之后所有「重新开始」都由传送门自己处理
    ///    （投降 → 黯淡 → 冷却 → 重新调查），不会再掉第二块碎块。
    ///
    /// 用 MapComponent 而不是 IncidentDef：这一版引擎没有 Harmony，改不了原版
    /// 的随机事件池；自己计时反而更可控，也方便开发者菜单一键测试。
    /// </summary>
    public class MapComponent_GaiaEvent : MapComponent
    {
        private const string TriggerResearch = "Botania_Terrasteel";
        private const string ShardDefName = "Botania_GaiaShard";

        private bool shardSpawned;
        private int triggerTick = -1;

        public MapComponent_GaiaEvent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shardSpawned, "gaiaShardSpawned", false);
            Scribe_Values.Look(ref triggerTick, "gaiaTriggerTick", -1);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }

            EnsureScheduled();

            if (shardSpawned || triggerTick < 0)
            {
                return;
            }

            if (Find.TickManager.TicksGame < triggerTick)
            {
                return;
            }

            if (SpawnShard())
            {
                shardSpawned = true;
            }
        }

        /// <summary>研究完成后开始 3~5 天倒计时。只在第一次排期。</summary>
        public void EnsureScheduled()
        {
            if (shardSpawned || triggerTick > 0)
            {
                return;
            }
            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(TriggerResearch);
            if (proj == null || !proj.IsFinished)
            {
                return;
            }
            triggerTick = Find.TickManager.TicksGame + Rand.Range(180000, 300000);
            Log.Message("[Botania] 盖亚事件已排期：" + triggerTick + " tick 后掉落碎块");
        }

        private bool SpawnShard()
        {
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail(ShardDefName);
            if (shardDef == null)
            {
                Log.Error("[Botania] 找不到盖亚碎块 Def: " + ShardDefName);
                return false;
            }

            if (!TryFindShardCell(out IntVec3 cell))
            {
                // 找不到空地就别卡死，10 分钟后再试
                triggerTick = Find.TickManager.TicksGame + 60000;
                return false;
            }

            GenSpawn.Spawn(shardDef, cell, map, Rot4.North, WipeMode.Vanish);
            // 这个引擎里 explosionSound 传 null 会走 PlayOneShot(null) 报错，直接把声音关掉
            GenExplosion.DoExplosion(cell, map, 3f, DamageDefOf.Blunt, null, 40, 0.5f, doSoundEffects: false);

            Find.LetterStack.ReceiveLetter(
                "Botania.GaiaShardFall.Label".Translate(),
                "Botania.GaiaShardFall.Text".Translate(),
                LetterDefOf.NeutralEvent,
                new LookTargets(cell, map));
            return true;
        }

        /// <summary>落点：露天、可站立、15 格内没有玩家建筑。</summary>
        private bool TryFindShardCell(out IntVec3 result)
        {
            bool Validator(IntVec3 c)
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || c.Fogged(map))
                {
                    return false;
                }
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    if (b.Position.DistanceTo(c) < 15f)
                    {
                        return false;
                    }
                }
                return true;
            }

            return CellFinder.TryFindRandomCellNear(map.Center, map, 45, Validator, out result, 400)
                   || CellFinder.TryFindRandomCellNear(map.Center, map, 90, Validator, out result, 800);
        }

        // ---------------- 开发者菜单 ----------------

        [DebugAction("Botania", "生成盖亚碎块",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DevSpawnShardNow()
        {
            MapComponent_GaiaEvent mc = Find.CurrentMap?.GetComponent<MapComponent_GaiaEvent>();
            if (mc == null)
            {
                return;
            }
            mc.shardSpawned = false;
            mc.triggerTick = Find.TickManager.TicksGame;
        }

        [DebugAction("Botania", "立即开启盖亚传送门",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DevSpawnPortalNow()
        {
            Map m = Find.CurrentMap;
            if (m == null)
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Botania_GaiaPortal");
            if (def == null)
            {
                return;
            }
            IntVec3 cell = Find.CameraDriver.MapPosition;
            if (!cell.Standable(m))
            {
                cell = CellFinder.RandomClosewalkCellNear(cell, m, 10);
            }
            GenSpawn.Spawn(def, cell, m);
        }

        [DebugAction("Botania", "生成盖亚世界之魂（Boss）",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DevSpawnBoss()
        {
            Map m = Find.CurrentMap;
            if (m == null)
            {
                return;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Botania_GaiaWorldSoul");
            if (kind == null)
            {
                Log.Error("[Botania] 找不到 Boss PawnKindDef: Botania_GaiaWorldSoul");
                return;
            }

            IntVec3 cell = Find.CameraDriver.MapPosition;
            if (!cell.InBounds(m) || !cell.Walkable(m))
            {
                cell = CellFinder.RandomClosewalkCellNear(Find.CameraDriver.MapPosition, m, 8,
                    c => c.Walkable(m) && !c.Fogged(m));
            }
            if (!cell.IsValid)
            {
                Log.Error("[Botania] 找不到可以放 Boss 的格子");
                return;
            }

            Pawn boss = PawnGenerator.GeneratePawn(kind, Faction.OfMechanoids);
            if (boss == null)
            {
                Log.Error("[Botania] Boss 生成失败");
                return;
            }

            string bossName = "Botania.GaiaBossName".Translate();
            boss.Name = new NameSingle(bossName);
            GenSpawn.Spawn(boss, cell, m);
            boss.Name = new NameSingle(bossName);

            Log.Message("[Botania] 开发者生成 Boss：" + boss.LabelCap);
            Messages.Message("Botania.GaiaBossSpawned".Translate(),
                new LookTargets(boss), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>找当前地图上的 Boss：优先选中的，其次离镜头最近的。</summary>
        private static Pawn_GaiaBoss FindBoss()
        {
            Map m = Find.CurrentMap;
            if (m == null)
            {
                return null;
            }
            if (Find.Selector != null && Find.Selector.SingleSelectedThing is Pawn_GaiaBoss selected)
            {
                return selected;
            }
            Pawn_GaiaBoss best = null;
            float bestDist = float.MaxValue;
            IntVec3 cam = Find.CameraDriver.MapPosition;
            IReadOnlyList<Pawn> pawns = m.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn_GaiaBoss boss = pawns[i] as Pawn_GaiaBoss;
                if (boss == null || boss.Dead)
                {
                    continue;
                }
                float d = boss.Position.DistanceToSquared(cam);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = boss;
                }
            }
            return best;
        }

        [DebugAction("Botania", "查看盖亚事件状态",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DevShowState()
        {
            MapComponent_GaiaEvent mc = Find.CurrentMap?.GetComponent<MapComponent_GaiaEvent>();
            if (mc == null)
            {
                return;
            }
            Log.Message($"[Botania] 盖亚事件：碎块已生成={mc.shardSpawned} 触发 tick={mc.triggerTick} 当前 tick={Find.TickManager.TicksGame}");
        }
    }
}
