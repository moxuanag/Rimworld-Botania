using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力通量场：把设备所在格「魔力网络」中的魔力转化为电力（对应 Botania 的 Mana Fluxfield）。
    /// 每 60 tick 结算一次：网络里魔力够就满功率输出，不够就完全停机。
    ///
    /// 框架迁移（0.8.4 S2 · 组合式示范）：
    /// 本设备的父类必须是 <see cref="CompPowerTrader"/>（要接电网），无法继承 CompManaDevice，
    /// 因此改为**组合** <see cref="ManaDeviceCore"/>：付款、节流、存档由核心负责，
    /// 本文件只保留「有没有魔力 → 输出多少电」。
    /// </summary>
    public class CompProperties_ManaFluxfield : CompProperties_Power, IManaDeviceConfig
    {
        /// <summary>满功率运行时每秒消耗的魔力。</summary>
        public float manaPerSecond = 5f;

        /// <summary>旧版「搜索半径」字段：仅为存档 / XML 兼容保留，不再参与取魔（取魔走所在格的魔力网络）。</summary>
        public float searchRadius = 6f;

        public CompProperties_ManaFluxfield()
        {
            compClass = typeof(CompManaFluxfield);
        }

        // ---- IManaDeviceConfig：把本设备的字段映射成核心需要的配置 ----

        float IManaDeviceConfig.SearchRadius => searchRadius;

        /// <summary>每 60 tick（1 秒）结算一次，所以每次付费额就是每秒消耗。</summary>
        float IManaDeviceConfig.ManaPerWork => manaPerSecond;

        int IManaDeviceConfig.WorkIntervalTicks => 60;

        int IManaDeviceConfig.RetryIntervalTicks => 60;

        float IManaDeviceConfig.ManaTolerance => 0.01f;

        string IManaDeviceConfig.WorkingKey => "Botania.FluxfieldWorking";

        string IManaDeviceConfig.NoManaKey => "Botania.FluxfieldNoMana";

        string IManaDeviceConfig.HasManaSaveKey => "botaniaFluxfieldHasMana";

        string IManaDeviceConfig.NextWorkSaveKey => "botaniaFluxfieldNextWorkTick";

        /// <summary>旧版存档键：老档里就存在 hasMana 这个字段。</summary>
        string IManaDeviceConfig.LegacyHasManaKey => "hasMana";

        string IManaDeviceConfig.LegacyNextWorkKey => null;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (manaPerSecond <= 0f)
            {
                yield return "manaPerSecond must be greater than 0.";
            }
            if (searchRadius < 0f)
            {
                yield return "searchRadius must not be negative.";
            }
            // PowerConsumption 对应 XML 里的 basePowerConsumption（原字段是私有的）。
            // 发电设备的消耗应为负数。
            if (PowerConsumption >= 0f)
            {
                yield return "basePowerConsumption must be negative (this comp produces power).";
            }
        }
    }

    public class CompManaFluxfield : CompPowerTrader
    {
        private ManaDeviceCore core;

        public CompProperties_ManaFluxfield ManaProps => (CompProperties_ManaFluxfield)props;

        private IManaDeviceConfig Config => (IManaDeviceConfig)props;

        private ManaDeviceCore Core => core ??= new ManaDeviceCore(this, Config);

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
            {
                return;
            }
            if (!Core.ShouldWorkNow())
            {
                return;
            }
            Settle();
        }

        private void Settle()
        {
            // 被玩家关掉时：不扣魔力、输出归零
            CompFlickable flickable = parent.TryGetComp<CompFlickable>();
            if (flickable != null && !flickable.SwitchIsOn)
            {
                Core.SetHasMana(false);
                Core.ScheduleNext(true);
                PowerOutput = 0f;
                return;
            }

            // 每 60 tick 结算一次 = 每秒一次，付费额即「每秒消耗」
            bool paid = Core.TryPay(ManaProps.manaPerSecond);
            Core.ScheduleNext(paid);
            PowerOutput = paid ? -ManaProps.PowerConsumption : 0f;
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            string ours = Core.InspectString();
            if (ours.NullOrEmpty())
            {
                return baseString;
            }
            if (baseString.NullOrEmpty())
            {
                return ours;
            }
            return baseString + "\n" + ours;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Core.ExposeData();
        }
    }
}
