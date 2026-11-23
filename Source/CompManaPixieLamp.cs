using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 精灵灯：不耗电，从魔力网络抽 1 魔力 / 60 秒。
    ///
    /// 断魔语义和精灵门一致——**直接熄灭**，来了魔力再自己亮回来。
    /// 实现方式刻意不碰 CompGlower 内部：原版灯本来就有 CompFlickable（开关），
    /// 我们只是代替玩家去拨这个开关，所以不用反射、也不用改引擎行为。
    ///
    /// 框架迁移：付款 / 节流 / 存档 / 提示 全部交给 CompManaDevice 基类，
    /// 本文件只保留「有没有魔力 → 拨开关」这一件事。
    /// </summary>
    public class CompProperties_ManaPixieLamp : CompProperties_ManaDevice
    {
        public CompProperties_ManaPixieLamp()
        {
            compClass = typeof(CompManaPixieLamp);

            // 精灵灯语义：每小时 1 点魔力；熄灭后 5 秒重试一次。
            manaPerWork = 1f;
            workIntervalTicks = 3600;
            retryIntervalTicks = 300;
            searchRadius = 8f;
            checkIntervalTicks = 60;

            // 沿用旧存档键，老存档里的状态能直接读回来。
            saveKeyPrefix = "botaniaPixieLamp";
            legacyHasManaKey = "botaniaPixieLampHasMana";
            legacyNextWorkKey = "botaniaPixieLampNextChargeTick";

            inspectWorkingKey = "Botania.PixieLampOn";
            inspectNoManaKey = "Botania.PixieLampOff";
        }
    }

    public class CompManaPixieLamp : CompManaDevice
    {
        private CompFlickable flickable;

        public bool Lit => Core.HasMana;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickable = parent.GetComp<CompFlickable>();
            // 刚建好/刚加载先立刻结一次账：没魔力的话当场就是暗的，不用先亮一会儿
            SettleOnce();
        }

        /// <summary>灯永远想亮，能否亮取决于付不付得起魔力。</summary>
        protected override bool WantsWork()
        {
            return true;
        }

        /// <summary>已经付过魔力了，这里只把状态翻译成开关。</summary>
        protected override void DoWork(int ticks)
        {
        }

        protected override void OnSettled(bool worked)
        {
            ApplyLitState();
        }

        /// <summary>把「有没有魔力」翻译成「灯的开关状态」。</summary>
        private void ApplyLitState()
        {
            if (flickable == null)
            {
                return;
            }
            if (flickable.SwitchIsOn != Core.HasMana)
            {
                flickable.SwitchIsOn = Core.HasMana;
            }
        }
    }
}
