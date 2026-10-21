using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 本模组的全局设置（游戏内「选项 → 模组设置」里可改）。
    /// 目前只有修复器的目标上限开关。
    /// </summary>
    public class BotaniaModSettings : ModSettings
    {
        /// <summary>是否限制每周期修复的目标数量。关掉 = 范围内所有破的东西一起修。</summary>
        public bool limitRepairTargets;

        /// <summary>上限数量（limitRepairTargets 为 true 时生效）。</summary>
        public int maxRepairTargets = 4;

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
            Scribe_Values.Look(ref limitRepairTargets, "limitRepairTargets", false);
            Scribe_Values.Look(ref maxRepairTargets, "maxRepairTargets", 4);
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
            BotaniaModSettings settings = GetSettings<BotaniaModSettings>();

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Botania.SettingsLimitTargets".Translate(), ref settings.limitRepairTargets,
                "Botania.SettingsLimitTargetsDesc".Translate());

            if (settings.limitRepairTargets)
            {
                listing.Label("Botania.SettingsMaxTargets".Translate(settings.maxRepairTargets));
                float value = listing.Slider(settings.maxRepairTargets, 1f, 50f);
                settings.maxRepairTargets = Mathf.RoundToInt(value);
            }
            else
            {
                listing.Label("Botania.SettingsUnlimited".Translate());
            }

            listing.GapLine();
            listing.Label("Botania.SettingsRepairCost".Translate());
            listing.End();
        }
    }
}
