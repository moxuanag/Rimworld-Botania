using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「研究完成 → 赠送一件东西」的执行者（Def 驱动）。
    ///
    /// 它只认识 <see cref="BotaniaResearchGiftDef"/>：加一个赠品 = 写一条 XML，不改 C#。
    /// 引擎会用 <c>Activator.CreateInstance(type, game)</c> 自动创建本类
    /// （<c>Verse.Game</c> 里扫 <c>typeof(GameComponent).AllSubclassesNonAbstract()</c>），
    /// 所以**不需要**在 XML 里登记。
    ///
    /// 发放顺序（每一步失败才降级）：
    ///   ① 基地（Home 区）内随机找格，用 <see cref="GenConstruct.CanPlaceBlueprintAt"/> 判可放 → 直接生成；
    ///   ② 降级：基地内任意「可站、无建筑、无植物」的格子 → 直接生成；
    ///   ③ 再不行：做成可收纳物品（<see cref="MinifiedThing"/>）掉在基地边缘，并弹黄字提示。
    /// 只要本次真的处理了（含 ③），就记入 granted，避免每 250 tick 刷屏重试。
    /// 没有玩家基地地图时不记录，等下次再试。
    ///
    /// 老档兼容：<see cref="LoadedGame"/> 里把「已经完成但没记录」的研究**静默记账**，
    /// 不补发花 —— 否则老存档一载入就被塞一屋子东西。
    /// </summary>
    public class GameComponent_ResearchGifts : GameComponent
    {
        private const string SaveKey = "botaniaResearchGifts";

        /// <summary>检查间隔（tick）。研究完成不是毫秒级事件，没必要每 tick 扫。</summary>
        private const int CheckIntervalTicks = 250;

        /// <summary>每处落位最多试几格。</summary>
        private const int PlacementAttempts = 200;

        /// <summary>已经处理过的研究（存 defName；无论成功失败都记，避免刷屏重试）。</summary>
        private List<string> granted = new List<string>();

        private int nextCheckTick;

        public GameComponent_ResearchGifts(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref granted, SaveKey, LookMode.Value);
            if (granted == null)
            {
                granted = new List<string>();
            }
        }

        /// <summary>
        /// 读档完成：老档（或本功能首次上线前的档）里已经完成的研究只记账、不补发。
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            List<BotaniaResearchGiftDef> defs = DefDatabase<BotaniaResearchGiftDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                BotaniaResearchGiftDef giftDef = defs[i];
                if (giftDef?.research == null)
                {
                    continue;
                }
                if (giftDef.research.IsFinished)
                {
                    RecordGranted(giftDef.research.defName);
                }
            }
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick)
            {
                return;
            }
            nextCheckTick = now + CheckIntervalTicks;
            CheckGifts();
        }

        /// <summary>遍历所有赠品定义：研究完成且没处理过 → 试着发一份。</summary>
        private void CheckGifts()
        {
            List<BotaniaResearchGiftDef> defs = DefDatabase<BotaniaResearchGiftDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                BotaniaResearchGiftDef giftDef = defs[i];
                if (giftDef?.research == null || giftDef.gift == null)
                {
                    continue;
                }
                if (!giftDef.research.IsFinished)
                {
                    continue;
                }
                if (IsGranted(giftDef.research.defName))
                {
                    continue;
                }
                // 没有玩家基地地图：不记账，下次再试。
                if (!TryGive(giftDef))
                {
                    continue;
                }
                RecordGranted(giftDef.research.defName);
            }
        }

        /// <summary>真的发一份。返回 false 只表示「这次没法发」（没有基地地图），可以重试。</summary>
        private static bool TryGive(BotaniaResearchGiftDef giftDef)
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return false;
            }
            ThingDef gift = giftDef.gift;
            List<IntVec3> homeCells = CollectHomeCells(map);

            Thing placed;
            if (TrySpawnByBlueprintRules(gift, map, homeCells, out placed))
            {
                NotifyPlayer(giftDef, map, placed);
                return true;
            }
            if (TrySpawnOnStandableCell(gift, map, homeCells, out placed))
            {
                NotifyPlayer(giftDef, map, placed);
                return true;
            }
            DropAsItemAtBaseEdge(giftDef, map, homeCells);
            return true;
        }

        /// <summary>① 基地内按「能否放蓝图」挑格，挑到就生成。</summary>
        private static bool TrySpawnByBlueprintRules(ThingDef gift, Map map, List<IntVec3> cells, out Thing spawned)
        {
            spawned = null;
            if (gift == null || cells.Count == 0)
            {
                return false;
            }
            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                IntVec3 candidate = cells[Rand.Range(0, cells.Count)];
                if (!candidate.InBounds(map))
                {
                    continue;
                }
                if (!CanPlaceGiftAt(gift, candidate, map))
                {
                    continue;
                }
                spawned = GenSpawn.Spawn(gift, candidate, map, Rot4.North);
                return spawned != null;
            }
            return false;
        }

        /// <summary>
        /// ② 降级：基地内任意可站、没有建筑/植物/水的格子，直接生成。
        /// （非建筑类的赠品走到这一步就是「当成掉落物放在基地里」。）
        /// </summary>
        private static bool TrySpawnOnStandableCell(ThingDef gift, Map map, List<IntVec3> cells, out Thing spawned)
        {
            spawned = null;
            if (gift == null || cells.Count == 0)
            {
                return false;
            }
            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                IntVec3 candidate = cells[Rand.Range(0, cells.Count)];
                if (!candidate.InBounds(map) || candidate.Fogged(map))
                {
                    continue;
                }
                if (!candidate.Standable(map))
                {
                    continue;
                }
                if (candidate.GetEdifice(map) != null)
                {
                    continue;
                }
                if (candidate.GetPlant(map) != null)
                {
                    continue;
                }
                spawned = GenSpawn.Spawn(gift, candidate, map, Rot4.North);
                return spawned != null;
            }
            return false;
        }

        /// <summary>
        /// ③ 兜底：把赠品做成可收纳物品（<see cref="MinifiedThing"/>）掉在基地边缘，并弹黄字。
        /// 不可收纳的赠品就直接放一栋实体 —— 宁可难看，也不要让玩家「研究做完了却什么都没拿到」。
        /// </summary>
        private static bool DropAsItemAtBaseEdge(BotaniaResearchGiftDef giftDef, Map map, List<IntVec3> cells)
        {
            ThingDef gift = giftDef.gift;
            if (gift == null)
            {
                return false;
            }
            IntVec3 edge = FindBaseEdgeCell(map, cells);
            Thing item = null;
            if (gift.Minifiable)
            {
                MinifiedThing minified = ThingMaker.MakeThing(gift.minifiedDef) as MinifiedThing;
                if (minified != null)
                {
                    minified.InnerThing = ThingMaker.MakeThing(gift);
                    item = minified;
                }
            }
            if (item == null)
            {
                item = ThingMaker.MakeThing(gift);
            }
            if (!GenPlace.TryPlaceThing(item, edge, map, ThingPlaceMode.Near))
            {
                item.Destroy(DestroyMode.Vanish);
                Messages.Message("Botania.GiftLetter.Fallback".Translate(gift.LabelCap),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            Messages.Message("Botania.GiftLetter.Fallback".Translate(gift.LabelCap),
                new LookTargets(item), MessageTypeDefOf.CautionInput, false);
            NotifyPlayer(giftDef, map, item);
            return true;
        }

        /// <summary>
        /// 发放通知：配了 <see cref="BotaniaResearchGiftDef.letter"/> 就发信，
        /// 否则只弹一条 Message（不打断游戏节奏）。
        /// </summary>
        private static void NotifyPlayer(BotaniaResearchGiftDef giftDef, Map map, Thing thing)
        {
            if (giftDef?.gift == null)
            {
                return;
            }
            string label = "Botania.GiftLetter.Label".Translate(giftDef.gift.LabelCap);
            string text = "Botania.GiftLetter.Text".Translate(giftDef.gift.LabelCap);
            LookTargets targets = thing != null ? new LookTargets(thing) : new LookTargets(map.Center, map);
            if (giftDef.letter != null)
            {
                Find.LetterStack.ReceiveLetter(label, text, giftDef.letter, targets);
            }
            else
            {
                Messages.Message(text, targets, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        /// <summary>基地（Home 区）内所有格。没画 Home 区时返回空表，由后面的分支降级。</summary>
        private static List<IntVec3> CollectHomeCells(Map map)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Area home = map.areaManager.Home;
            if (home == null)
            {
                return cells;
            }
            foreach (IntVec3 cell in home.ActiveCells)
            {
                cells.Add(cell);
            }
            return cells;
        }

        /// <summary>基地边缘：取离 Home 区重心最远的那个 Home 格（每张图只算一次，成本可忽略）。</summary>
        private static IntVec3 FindBaseEdgeCell(Map map, List<IntVec3> cells)
        {
            if (cells.Count == 0)
            {
                return map.Center;
            }
            float sumX = 0f;
            float sumZ = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                sumX += cells[i].x;
                sumZ += cells[i].z;
            }
            float centerX = sumX / cells.Count;
            float centerZ = sumZ / cells.Count;
            IntVec3 best = cells[0];
            float bestDistance = -1f;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                float dx = cell.x - centerX;
                float dz = cell.z - centerZ;
                float distance = dx * dx + dz * dz;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>
        /// 蓝图规则判可放。赠品如果不是建筑（或 def 配置异常），原版判定可能抛异常，
        /// 这里当「放不了」处理并只报一次错，绝不让它把 tick 打断。
        /// </summary>
        private static bool CanPlaceGiftAt(ThingDef gift, IntVec3 cell, Map map)
        {
            try
            {
                return GenConstruct.CanPlaceBlueprintAt(gift, cell, Rot4.North, map).Accepted;
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    "Botania: CanPlaceBlueprintAt failed for gift " + gift.defName + ": " + exception,
                    gift.defName.GetHashCode());
                return false;
            }
        }

        private bool IsGranted(string researchDefName)
        {
            return granted != null && granted.Contains(researchDefName);
        }

        private void RecordGranted(string researchDefName)
        {
            if (researchDefName.NullOrEmpty())
            {
                return;
            }
            if (granted == null)
            {
                granted = new List<string>();
            }
            if (!granted.Contains(researchDefName))
            {
                granted.Add(researchDefName);
            }
        }
    }
}