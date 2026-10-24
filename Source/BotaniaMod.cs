using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 本模组的全局设置（选项 → 模组设置）。
    /// 建筑修复与装备修复各自独立开关与上限，默认都开启限制、每次 5 个。
    /// </summary>
    public class BotaniaModSettings : ModSettings
    {
        public bool limitStructureTargets = true;
        public int maxStructureTargets = 5;

        public bool limitItemTargets = true;
        public int maxItemTargets = 5;

        public static BotaniaModSettings Instance
        {
            get
            {
                BotaniaMod mod = LoadedModManager.GetMod<BotaniaMod>();
                return mod?.GetSettings<BotaniaModSettings>();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref limitStructureTargets, "limitStructureTargets", true);
            Scribe_Values.Look(ref maxStructureTargets, "maxStructureTargets", 5);
            Scribe_Values.Look(ref limitItemTargets, "limitItemTargets", true);
            Scribe_Values.Look(ref maxItemTargets, "maxItemTargets", 5);
        }
    }

    public class BotaniaMod : Mod
    {
        public BotaniaMod(ModContentPack content) : base(content)
        {
            CheckMiningYieldCap();
        }

        /// <summary>
        /// 启动自检：确认 `Patches/Botania_MiningYieldCap.xml` 真的生效。
        ///
        /// 原版 `StatDef MiningYield` 的 `maxValue` 是 1.25。碎岩镐给了 `+2` 偏移，
        /// 天花板不抬就会被夹掉（实测 50 = 40 × 1.25，见踩坑 #26）。
        /// 这条日志是「补丁到底打没打上」的唯一可靠证据——
        /// 进游戏后搜 `[Botania]` 就能确认，不用靠猜掉落数量。
        /// </summary>
        private static void CheckMiningYieldCap()
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail("MiningYield");
            if (stat == null)
            {
                Log.Warning("[Botania] 找不到 StatDef MiningYield：碎岩镐的矿物掉落 ×3 不会生效。");
                return;
            }
            if (stat.maxValue <= 1.25f)
            {
                Log.Warning("[Botania] MiningYield 上限仍是 " + stat.maxValue.ToString("F2")
                    + "，补丁没生效：碎岩镐的矿物掉落会被夹到 125% 以内。"
                    + "请检查 Patches/Botania_MiningYieldCap.xml 是否被其他模组覆盖。");
                return;
            }
            Log.Message("[Botania] 采矿效率上限已抬到 " + stat.maxValue.ToString("F1")
                + "（原版 1.25）：碎岩镐的矿物掉落 ×3 可正常生效。");
        }

        public override string SettingsCategory() => "植物魔法 Botania";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            BotaniaModSettings s = GetSettings<BotaniaModSettings>();
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("Botania.SettingsStructureHeader".Translate());
            list.CheckboxLabeled("Botania.SettingsLimitStructureTargets".Translate(), ref s.limitStructureTargets,
                "Botania.SettingsLimitTargetsDesc".Translate());
            if (s.limitStructureTargets)
            {
                list.Label("Botania.SettingsMaxStructureTargets".Translate(s.maxStructureTargets));
                s.maxStructureTargets = Mathf.RoundToInt(list.Slider(s.maxStructureTargets, 1f, 50f));
            }
            else
            {
                list.Label("Botania.SettingsUnlimited".Translate());
            }

            list.GapLine();

            list.Label("Botania.SettingsItemHeader".Translate());
            list.CheckboxLabeled("Botania.SettingsLimitItemTargets".Translate(), ref s.limitItemTargets,
                "Botania.SettingsLimitTargetsDesc".Translate());
            if (s.limitItemTargets)
            {
                list.Label("Botania.SettingsMaxItemTargets".Translate(s.maxItemTargets));
                s.maxItemTargets = Mathf.RoundToInt(list.Slider(s.maxItemTargets, 1f, 50f));
            }
            else
            {
                list.Label("Botania.SettingsUnlimited".Translate());
            }

            list.GapLine();
            list.Label("Botania.SettingsRepairCost".Translate());
            list.End();
        }
    }
}
