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
        }

        public override string SettingsCategory() => "植物魔法 Botania";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            BotaniaModSettings s = GetSettings<BotaniaModSettings>();
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("Botania.SettingsStructureHeader".Translate());
            list.CheckboxLabeled("Botania.SettingsLimitTargets".Translate(), ref s.limitStructureTargets,
                "Botania.SettingsLimitTargetsDesc".Translate());
            if (s.limitStructureTargets)
            {
                list.Label("Botania.SettingsMaxTargets".Translate(s.maxStructureTargets));
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
