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
    /// </summary>
    public class CompProperties_ManaPixieLamp : CompProperties
    {
        /// <summary>每次结算要付的魔力量。</summary>
        public float manaPerCharge = 1f;

        /// <summary>结算间隔（tick）。默认 60 秒。</summary>
        public int chargeIntervalTicks = 3600;

        /// <summary>找魔力源的半径。</summary>
        public float searchRadius = 8f;

        public CompProperties_ManaPixieLamp()
        {
            compClass = typeof(CompManaPixieLamp);
        }
    }

    public class CompManaPixieLamp : ThingComp
    {
        /// <summary>上一次结算是否有魔力。没有就一直熄着。</summary>
        private bool hasMana = true;

        private int nextChargeTick;

        private CompFlickable flickable;

        public CompProperties_ManaPixieLamp Props => (CompProperties_ManaPixieLamp)props;

        public bool Lit => hasMana;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickable = parent.GetComp<CompFlickable>();
            if (nextChargeTick <= 0)
            {
                // 刚建好先立刻结一次账：没魔力的话当场就是暗的，不用等一分钟
                nextChargeTick = Find.TickManager.TicksGame;
            }
            ApplyLitState();
        }

        public override void CompTick()
        {
            // 每秒看一次表就够了，3600 tick 的账不值得每个 tick 都算
            if (!parent.IsHashIntervalTick(60))
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now < nextChargeTick)
            {
                return;
            }
            nextChargeTick = now + Props.chargeIntervalTicks;
            TryCharge();
        }

        private void TryCharge()
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            float need = Props.manaPerCharge;
            IManaSource source = ManaSourceUtil.FindNearest(
                parent.Position, map, Props.searchRadius, need, parent);

            bool got = source != null && source.DrainMana(need) >= need - 0.001f;
            if (got == hasMana)
            {
                return;
            }
            hasMana = got;
            ApplyLitState();
        }

        /// <summary>把「有没有魔力」翻译成「灯的开关状态」。</summary>
        private void ApplyLitState()
        {
            if (flickable == null)
            {
                return;
            }
            if (flickable.SwitchIsOn != hasMana)
            {
                flickable.SwitchIsOn = hasMana;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Map == null)
            {
                return null;
            }
            return hasMana
                ? "Botania.PixieLampOn".Translate()
                : "Botania.PixieLampOff".Translate();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasMana, "botaniaPixieLampHasMana", true);
            Scribe_Values.Look(ref nextChargeTick, "botaniaPixieLampNextChargeTick", 0);
        }
    }
}
