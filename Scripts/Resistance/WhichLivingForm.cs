using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 火花牌的「形态」：<b>生灵</b>（火花提供的变成防御能力）或<b>死灵</b>（变成攻击能力）。
/// <see cref="None"/> = 玩家还没有「生死之灵」，一切按火花自己的基础数值走。
///
/// <para><b>形态不记在火花上，而是玩家身上的一个能力</b>：唯一实体是 <see cref="WhichLivingPower"/>，
/// 形态位借用它的 <c>Amount</c>（1 = 生灵、2 = 死灵）。这么做是为了让形态能进原版的网络校验和 ——
/// <c>NetFullCombatState.PowerState</c> 只同步能力的 id 与 amount，其它自定义字段两端各有各的、分叉了也查不出来。
/// 火花只在自己<b>被读的时候</b>问一次“我的 owner 现在是什么形态”，所以切换形态既不遍历也不改写火花；
/// 唯一要跟着改的是那些<b>把数值写在牌面上</b>的火花（映耀/灿烂，见 <see cref="IFireFlower.ApplyLivingForm"/>）。</para>
/// </summary>
public enum WhichLivingForm
{
    /// <summary>普通形态：没有生死之灵（或读不出形态位）。</summary>
    None = 0,

    /// <summary>生灵：火花提供防御能力。</summary>
    Alive = 1,

    /// <summary>死灵：火花提供攻击能力。</summary>
    Dead = 2,
}

/// <summary>
/// 形态相关的公共入口。类名叫 <c>LivingForm</c> 而不是 <c>WhichLiving</c>：后者已被「生死之灵」卡本卡占用
/// （卡类名必须与本地化 id 对应，见 <c>REED-WHICH_LIVING</c>），从别的命名空间引用会撞名。
/// </summary>
public static class LivingForm
{
    /// <summary>形态位写在 <see cref="WhichLivingPower"/> 的 Amount 上的取值。</summary>
    public static decimal ToAmount(WhichLivingForm form) => (decimal)(int)form;

    /// <summary>某玩家当前的形态。</summary>
    public static WhichLivingForm Of(Player? owner) => Of(owner?.Creature);

    /// <summary>某生物（玩家）当前的形态；不是玩家、或没有生死之灵时都是 <see cref="WhichLivingForm.None"/>。</summary>
    public static WhichLivingForm Of(Creature? creature)
    {
        WhichLivingPower? power = creature?.GetPower<WhichLivingPower>();
        return power == null ? WhichLivingForm.None : power.Form;
    }

    /// <summary>某张火花牌 owner 当下的形态。规范卡（canonical）没有 owner，一律 None（= 牌库/预览看到的基础数值）。</summary>
    public static WhichLivingForm Of(CardModel? card)
        => card is { IsMutable: true } mutable ? Of(mutable.Owner) : WhichLivingForm.None;

    /// <summary>
    /// 把形态翻译成描述用的两个布尔（cards.json / powers.json 里的 <c>{Alive:…|}{Dead:…|}</c>）。
    /// <b>两个都必须加</b>：描述里两个条件都有，少一个就会取不到值，SmartFormat 会把整段描述降级成原文
    /// （LocManager 的兜底是「记一条错误并返回未格式化的文本」，牌面会直接露出 <c>{Alive:…}</c> 花括号）。
    /// </summary>
    public static void AddArgsTo(LocString description, Player? owner)
    {
        WhichLivingForm form = Of(owner);
        description.Add("Alive", form == WhichLivingForm.Alive);
        description.Add("Dead", form == WhichLivingForm.Dead);
    }

    /// <summary>
    /// 让一张火花跟上它 owner 当前的形态 —— 幂等，重复调用无害。
    /// 规范卡没有 owner，直接跳过。
    /// </summary>
    public static void ApplyTo(CardModel? spark)
    {
        if (spark is not { IsMutable: true } card || card is not IFireFlower flower)
        {
            return;
        }
        flower.ApplyLivingForm(Of(card.Owner));
    }

    /// <summary>
    /// 把某玩家名下<b>所有已附着的火花</b>（挂在自己或别人身上都算）按当前形态重算一遍，并刷新它们牌面上的文本。
    /// 打出「生死之灵」、以及玩家失去该能力（形态回落到普通）时调用。
    /// </summary>
    public static void ApplyToAllSparksOf(Player? owner)
    {
        if (owner is null)
        {
            return;
        }
        ICombatState? combat = owner.Creature.CombatState;
        if (combat == null)
        {
            return; // 不在战斗中：没有火花，也没有牌面要刷
        }

        foreach (Creature creature in combat.Creatures)
        {
            // Peek 而不是 Get：只是重算形态，不该给没建过数据的生物凭空建一份。
            ResistanceData? data = ResistanceSystem.PeekData(creature);
            if (data == null || !data.Sparks.TryGetValue(owner.NetId, out List<CardModel>? sparks))
            {
                continue;
            }
            foreach (CardModel spark in sparks)
            {
                ApplyTo(spark);
            }
            // 数值改完要让已显示的牌面重排文本（抽牌数/能量图标就写在牌面上）。
            data.Bar?.Spark?.RefreshCardVisuals();
        }
    }

    /// <summary>
    /// 「基础／终末」两张花的爆发收尾：按形态令<b>来源</b>（= 火花的 owner）获得格挡或活力。
    /// 数值直接读火花自己牌面上的变量（<c>Block</c> / <c>VigorPower</c>），谁没声明那个变量就不结算 ——
    /// 于是每张花只要在 <c>CanonicalVars</c> 里声明这两个变量，生灵/死灵/普通三态就自动成立，不必各写一份分支。
    /// </summary>
    public static async Task GrantFormBonus(PlayerChoiceContext playerChoiceContext, CardModel spark)
    {
        if (spark is not { IsMutable: true } card)
        {
            return;
        }
        WhichLivingForm form = Of(card.Owner);
        if (form == WhichLivingForm.None)
        {
            return; // 普通形态：爆发只造成伤害
        }
        Creature self = card.Owner.Creature;
        if (form == WhichLivingForm.Alive && card.DynamicVars.ContainsKey("Block"))
        {
            await CreatureCmd.GainBlock(self, card.DynamicVars.Block, null);
        }
        else if (form == WhichLivingForm.Dead && card.DynamicVars.ContainsKey("VigorPower"))
        {
            await PowerCmd.Apply<VigorPower>(playerChoiceContext, self, card.DynamicVars["VigorPower"].BaseValue, self, card);
        }
    }
}
