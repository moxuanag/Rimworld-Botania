using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Botania
{
    public class CompProperties_BotaniaBlueprint : CompProperties
    {
        /// <summary>这张蓝图指向的研究项目（必须是写了 techprintCount 的项目）。</summary>
        public ResearchProjectDef project;

        /// <summary>解读时给研究者的智力经验。</summary>
        public float studyExp = 500f;

        public CompProperties_BotaniaBlueprint()
        {
            compClass = typeof(CompBotaniaBlueprint);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            if (project == null)
            {
                yield return "BotaniaBlueprint: 没有设置 project";
            }
            else if (project.TechprintCount <= 0)
            {
                yield return "BotaniaBlueprint: 目标项目 " + project.defName
                    + " 的 techprintCount 为 0，蓝图没有意义";
            }
        }
    }

    /// <summary>
    /// 科技蓝图的「无皇权 DLC」解锁途径。
    ///
    /// 为什么需要它：原版 CompTechprint 的右键菜单第一行就是
    ///     if (!ModLister.CheckRoyalty("Techprint")) yield break;
    /// 而 ResearchManager.ApplyTechprint 开头同样有这道检查，
    /// 所以没有皇权的玩家拿着蓝图什么也做不了。
    ///
    /// 为什么不用 Harmony 补掉那行：DLC 依赖不止一处 ——
    /// JobDriver_ApplyTechprint 结束时还会用 SoundDefOf.TechprintApplied，
    /// 那是皇权 DLC 才有的 SoundDef，没装时为 null，直接 NullReferenceException。
    /// 补一处就得补一串，不如自己给一条干净的路。
    ///
    /// 为什么不做成「走过去、研究台前读 600 tick」：这套引擎里自定义
    /// JobDef + JobDriver 实测不生效（见开发笔记），所以退成即时解读。
    /// 有皇权时本组件完全沉默，菜单由原版 CompTechprint 提供。
    /// </summary>
    public class CompBotaniaBlueprint : ThingComp
    {
        public CompProperties_BotaniaBlueprint Props => (CompProperties_BotaniaBlueprint)props;

        private ResearchProjectDef Project => Props.project;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            // 有皇权：原版已经给了「应用科技蓝图」菜单，这里不重复出现
            if (ModsConfig.RoyaltyActive)
            {
                yield break;
            }
            ResearchProjectDef proj = Project;
            if (proj == null)
            {
                yield break;
            }

            string label = "Botania.BlueprintStudy".Translate(proj.LabelCap).CapitalizeFirst();

            if (Find.ResearchManager.GetTechprints(proj) >= proj.TechprintCount)
            {
                yield return new FloatMenuOption(
                    label + ": " + "Botania.BlueprintAlreadyApplied".Translate(), null);
                yield break;
            }
            if (selPawn.WorkTypeIsDisabled(WorkTypeDefOf.Research)
                || selPawn.WorkTagIsDisabled(WorkTags.Intellectual))
            {
                yield return new FloatMenuOption(
                    label + ": " + "Botania.BlueprintCantStudy".Translate(), null);
                yield break;
            }
            if (!selPawn.CanReach(parent, PathEndMode.ClosestTouch, Danger.Some))
            {
                yield return new FloatMenuOption(
                    label + ": " + "CannotReach".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                Study(selPawn);
            });
        }

        private void Study(Pawn pawn)
        {
            ResearchProjectDef proj = Project;
            if (proj == null || parent == null || parent.Destroyed)
            {
                return;
            }
            if (Find.ResearchManager.GetTechprints(proj) >= proj.TechprintCount)
            {
                return;
            }

            Find.ResearchManager.AddTechprints(proj, 1);
            if (pawn.skills != null)
            {
                pawn.skills.Learn(SkillDefOf.Intellectual, Props.studyExp);
            }
            Messages.Message(
                "Botania.BlueprintApplied".Translate(pawn.LabelShort, proj.LabelCap),
                new LookTargets(parent), MessageTypeDefOf.PositiveEvent, false);
            parent.Destroy(DestroyMode.Vanish);
        }
    }
}
