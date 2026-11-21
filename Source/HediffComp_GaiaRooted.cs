using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 根须缠绕：定身 + 持续伤害，**自己管持续时间**。
    ///
    /// 不用 HediffComp_Disappears：这套引擎里它的字段名/行为与预期不一致，
    /// 之前出现过「不消失 → 永久定身」。这里直接记 endTick，到点自己 RemoveHediff。
    ///
    /// 定身方式：每 tick 调 pather.StopDead()，**不能**把 Moving 容量压到 0
    /// （那样 RimWorld 会判定为倒地 incapacitated）。
    /// </summary>
    public class HediffCompProperties_GaiaRooted : HediffCompProperties
    {
        public float damagePerTickInterval = 2f;
        public int intervalTicks = 120;
        public float armorPenetration = 0.5f;

        /// <summary>总持续时长（tick）。3 秒 = 180。</summary>
        public int durationTicks = 180;

        /// <summary>命中特效的 ThingDef（根须贴图）。</summary>
        public string moteDefName = "Botania_Mote_GaiaRoots";

        /// <summary>特效重挂间隔（tick）。Mote 自身有寿命，靠定期重挂保持不断。</summary>
        public int moteIntervalTicks = 45;

        public HediffCompProperties_GaiaRooted()
        {
            compClass = typeof(HediffComp_GaiaRooted);
        }
    }

    public class HediffComp_GaiaRooted : HediffComp
    {
        /// <summary>外部指定的持续时长（&gt;0 时覆盖 Def 里的 durationTicks）。</summary>
        public int overrideDurationTicks = -1;

        private int nextDamageTick;
        private int endTick = -1;
        private int nextMoteTick = -1;

        private HediffCompProperties_GaiaRooted Props => (HediffCompProperties_GaiaRooted)props;

        /// <summary>外部指定定身时长（在 AddHediff 之后调用）。</summary>
        public void SetDuration(int ticks)
        {
            if (ticks > 0)
            {
                endTick = Find.TickManager.TicksGame + ticks;
            }
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            nextDamageTick = Find.TickManager.TicksGame + Props.intervalTicks;
            endTick = Find.TickManager.TicksGame + Props.durationTicks;
            nextMoteTick = Find.TickManager.TicksGame;
            SpawnRootMote();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.Dead || pawn.Map == null)
            {
                return;
            }

            // 到点：自己结束
            if (endTick > 0 && Find.TickManager.TicksGame >= endTick)
            {
                pawn.health?.RemoveHediff(parent);
                return;
            }

            // 定身：每 tick 停住脚步
            pawn.pather?.StopDead();

            // 根须特效：定期重挂，视觉上一直缠在身上；效果结束后不再重挂，自然会消失
            if (Find.TickManager.TicksGame >= nextMoteTick)
            {
                nextMoteTick = Find.TickManager.TicksGame + Props.moteIntervalTicks;
                SpawnRootMote();
            }

            if (Find.TickManager.TicksGame < nextDamageTick)
            {
                return;
            }
            nextDamageTick = Find.TickManager.TicksGame + Props.intervalTicks;

            DamageInfo damage = new DamageInfo(DamageDefOf.Blunt, Props.damagePerTickInterval,
                Props.armorPenetration, -1f, null);
            damage.SetBodyRegion(BodyPartHeight.Bottom, BodyPartDepth.Outside);
            pawn.TakeDamage(damage);
        }

        private void SpawnRootMote()
        {
            Pawn pawn = parent?.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || Props.moteDefName.NullOrEmpty())
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(Props.moteDefName);
            if (def == null)
            {
                return;
            }
            try
            {
                MoteMaker.MakeAttachedOverlay(pawn, def, Vector3.zero, 1f, 0.85f);
            }
            catch (System.Exception)
            {
                // 特效失败不影响机制
            }
        }

        /// <summary>健康面板括号里显示剩余秒数。</summary>
        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (endTick <= 0)
                {
                    return null;
                }
                int left = endTick - Find.TickManager.TicksGame;
                if (left <= 0)
                {
                    return null;
                }
                return "Botania.GaiaRootedBracket".Translate((left / 60f).ToString("0.0"));
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref nextDamageTick, "gaiaRootedNextTick", 0);
            Scribe_Values.Look(ref endTick, "gaiaRootedEndTick", -1);
        }
    }
}
