using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 魔力灯：不耗电，从魔力网络抽 1 魔力 / 60 秒。
    ///
    /// 断魔语义和精灵门一致——**直接熄灭**，来了魔力再自己亮回来。
    ///
    /// 关键点（0.8.4 踩坑后重写）：**绝不去动 CompFlickable**。
    /// 原版 CompFlickable 有两套状态：`switchOnInt`（实际开关，`SwitchIsOn`）
    /// 与 `wantSwitchOn`（玩家想要的状态，Gizmo 按钮显示的就是它）。
    /// 直接写 `SwitchIsOn` 会让两者脱钩：按钮显示「开」而灯不亮，
    /// 玩家一旦点关闭，小人来拨开关时 `DoFlick()` 取反反而把灯点亮，
    /// 并且 `WantsFlick()` 永久为真 → 小人反复跑过来拨灯。
    ///
    /// 正确做法：实现引擎自带的 **`IThingGlower`**。CompGlower 的 `ShouldBeLitNow`
    /// 会遍历同物件的所有组件询问 `IThingGlower.ShouldBeLitNow()`
    /// （CompPowerTrader / CompRefuelable 就是这么给灯断电的），
    /// 因此我们只要回答「现在有魔力吗」，灯光就由引擎自己切，开关语义完全不受影响。
    /// </summary>
    public class CompProperties_ManaLamp : CompProperties_ManaDevice
    {
        public CompProperties_ManaLamp()
        {
            compClass = typeof(CompManaLamp);

            // 魔力灯语义：每小时 1 点魔力；熄灭后 5 秒重试一次。
            manaPerWork = 1f;
            workIntervalTicks = 3600;
            retryIntervalTicks = 300;
            searchRadius = 8f;

            // 存档键统一带 botaniaManaLamp 前缀（AGENTS.md §7：平铺存档必须防撞键）。
            // 灯已随 defName 一起改名，旧灯键不再迁移。
            saveKeyPrefix = "botaniaManaLamp";
            legacyHasManaKey = "botaniaManaLampHasMana";
            legacyNextWorkKey = "botaniaManaLampNextChargeTick";

            inspectWorkingKey = "Botania.ManaLampOn";
            inspectNoManaKey = "Botania.ManaLampOff";
        }
    }

    public class CompManaLamp : CompManaDevice, IThingGlower
    {
        private CompGlower glower;

        /// <summary>灯现在是否亮着（= 上次结算付得起魔力）。</summary>
        public bool Lit => Core.HasMana;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            glower = parent.GetComp<CompGlower>();
        }

        /// <summary>
        /// 引擎问「现在该亮吗」。返回 false 时 CompGlower 会把灯关掉，
        /// 返回 true 时仍需 CompFlickable 处于开启状态才会亮——语义正确。
        /// </summary>
        public bool ShouldBeLitNow()
        {
            return Core.HasMana;
        }

        /// <summary>
        /// 玩家手动关掉这盏灯时不扣魔力——与灯光判定保持同一口径
        /// （引擎的 WantsToBeOn 也看 CompFlickable.SwitchIsOn）。
        /// </summary>
        protected override bool WantsWork()
        {
            return FlickUtility.WantsToBeOn(parent);
        }

        protected override void DoWork(int ticks)
        {
        }

        /// <summary>魔力状态一变就主动刷新一次灯光（引擎只在信号/初始化时重算）。</summary>
        protected override void OnManaStateChanged(bool hasMana)
        {
            if (glower != null && parent.Spawned && parent.Map != null)
            {
                glower.UpdateLit(parent.Map);
            }
        }
    }
}
