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

        /// <summary>拿到（必要时创建）盖亚阵营；失败时退回机械族，保证 Boss 仍然敌对。</summary>
        public static Faction Get()
        {
            FactionDef def = Def;
            if (def == null)
            {
                Log.Warning("[Botania] 找不到阵营 Def: " + FactionDefName + "，改用机械族");
                return Faction.OfMechanoids;
            }

            Faction existing = Find.FactionManager.FirstFactionOfDef(def);
            if (existing != null)
            {
                return existing;
            }

            Faction created = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default, true));
            if (created == null)
            {
                Log.Warning("[Botania] 创建盖亚阵营失败，改用机械族");
                return Faction.OfMechanoids;
            }
            Find.FactionManager.Add(created);
            created.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            Log.Message("[Botania] 已创建盖亚阵营：" + created.Name);
            return created;
        }
    }
}
