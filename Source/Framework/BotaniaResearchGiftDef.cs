using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Botania
{
    /// <summary>
    /// 「研究完成 → 赠送一件东西」的数据定义（Def 驱动，加赠品只写 XML，不用改 C#）。
    ///
    /// 用法（Defs/ 下任意 xml）：
    /// <code>
    /// &lt;Botania.BotaniaResearchGiftDef&gt;
    ///   &lt;defName&gt;Botania_Gift_FertilityFlower&lt;/defName&gt;
    ///   &lt;research&gt;Botania_FuncAdvanced&lt;/research&gt;   &lt;!-- 完成哪项研究后送 --&gt;
    ///   &lt;gift&gt;Botania_FertilityFlower&lt;/gift&gt;         &lt;!-- 送什么 --&gt;
    ///   &lt;letter&gt;PositiveEvent&lt;/letter&gt;                 &lt;!-- 可选：留空则只发一条 Message --&gt;
    /// &lt;/Botania.BotaniaResearchGiftDef&gt;
    /// </code>
    /// 自定义 Def 类型名要写全名（<c>Botania.BotaniaResearchGiftDef</c>），只写短名引擎不认。
    ///
    /// 实际发放在 <see cref="GameComponent_ResearchGifts"/> 里：它只遍历
    /// <c>DefDatabase&lt;BotaniaResearchGiftDef&gt;.AllDefs</c>，不认识任何具体研究或物品。
    /// </summary>
    public class BotaniaResearchGiftDef : Def
    {
        /// <summary>完成哪项研究后赠送。</summary>
        public ResearchProjectDef research;

        /// <summary>赠送的东西（通常是一栋花/设备）。</summary>
        public ThingDef gift;

        /// <summary>
        /// 发信用的信件类型。**允许为空**：为空时不发信，只弹一条普通 Message
        /// （所以这里不做「非空」校验，否则合法配置会被当成配置错误）。
        /// </summary>
        public LetterDef letter;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (research == null)
            {
                yield return "research is required (which ResearchProjectDef unlocks this gift).";
            }
            if (gift == null)
            {
                yield return "gift is required (which ThingDef to give).";
            }
        }
    }
}
