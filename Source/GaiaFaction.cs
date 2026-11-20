using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 盖亚阵营的获取/创建。
    ///
    /// Boss 不是机械族，所以不能用 Faction.OfMechanoids。我们自带一个隐藏阵营
    /// Botania_Gaia（permanentEnemy，非人形），但它不参与开局世界生成
    /// （requiredCountAtGameStart=0 + hidden），所以老存档里第一次召唤时要现场创建。
    /// </summary>
    public static class GaiaFaction
    {
        private const string FactionDefName = "Botania_Gaia";

        public static FactionDef Def => DefDatabase<FactionDef>.GetNamedSilentFail(FactionDefName);

        /// <summary>
        /// 程序层面保证「永久敌对」：只要玩家与盖亚的关系不是敌对，就强制改回敌对。
        /// XML 里的 permanentEnemy 只保证不和解，这里再兜一层，防止任何途径（开发者工具、
        /// 事件、mod 冲突）把它改成中立或盟友。
        /// </summary>
        public static void EnsureHostile(Faction faction)
        {
            if (faction == null || Faction.OfPlayer == null)
            {
                return;
            }
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
                Log.Message("[Botania] 盖亚阵营关系被重置为敌对");
            }
        }

        /// <summary>拿到（必要时创建）盖亚阵营；失败时退回机械族，保证 Boss 仍然敌对。</summary>
        public static Faction Get()
        {
            FactionDef def = Def;
            if (def == null)
            {
                Log.Warning("[Botania] 找不到阵营 Def: " + FactionDefName + "，改用机械族");
                return Faction.OfMechanoids;
            }

            // 1) 优先用世界缓存（避免隐藏阵营被引擎清理后又重复创建）
            WorldComponent_Gaia wc = Find.World?.GetComponent<WorldComponent_Gaia>();
            if (wc != null && wc.gaiaFaction != null)
            {
                EnsureHostile(wc.gaiaFaction);
                return wc.gaiaFaction;
            }

            // 2) 世界里已经存在同 Def 的阵营
            Faction existing = Find.FactionManager.FirstFactionOfDef(def);
            if (existing != null)
            {
                EnsureHostile(existing);
                if (wc != null)
                {
                    wc.gaiaFaction = existing;
                }
                return existing;
            }

            // 3) 现场创建
            Faction created = null;
            try
            {
                created = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default, true));
            }
            catch (System.Exception e)
            {
                // 创建阵营本身可能因为名字生成器等配置抛异常；
                // 绝不能让它冒到 OnGUI，否则整个界面会被打断
                Log.Error("[Botania] 创建盖亚阵营时异常（改用机械族）：" + e);
                return Faction.OfMechanoids;
            }
            if (created == null)
            {
                Log.Warning("[Botania] 创建盖亚阵营失败，改用机械族");
                return Faction.OfMechanoids;
            }
            Find.FactionManager.Add(created);
            created.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            EnsureHostile(created);
            if (wc != null)
            {
                wc.gaiaFaction = created;
            }
            Log.Message("[Botania] 已创建盖亚阵营：" + created.Name);
            return created;
        }
    }
}
