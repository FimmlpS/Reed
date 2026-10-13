using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Overlook : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<Defend>(true),
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public Overlook() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {

    }

    /// <summary>
    /// 先把 <see cref="DynamicVars"/> 的 Cards 张「防御+」的副本记进**下一回合**的回忆，
    /// 再从那一回合的回忆里追忆一张牌打出（此刻那边至少有刚记进去的防御+）。
    ///
    /// <para>两张牌都走 <c>FromPlay(cardPlay).Offset(1)</c>：基准回合 = 本牌视为打出的回合，
    /// +1 = 下一回合。下一回合的回忆桶是**未来**的桶，由 <see cref="PlayerMemory.GetRound"/> 实时建，
    /// 于是这次记录会**留到下一回合**——下一回合再追忆出来，或是被别的牌看见。</para>
    /// </summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 记录：ModelDb.Card<Defend>() 取的是 Reed 自己的「防御」（同命名空间），Upgraded() 让它成为防御+。
        await RecordCmd.Record(choiceContext, Owner)
            .FromPlay(cardPlay)
            .Offset(1)
            .Card(ModelDb.Card<Defend>())
            .Upgraded()
            .Count(DynamicVars.Cards.IntValue)
            .Execute(choiceContext);

        // 追忆：不加筛选 = 任意牌（正常就是刚记进去的那些防御+）。
        await RecallCmd.Recall(choiceContext, Owner)
            .FromPlay(cardPlay)
            .Offset(1)
            .Execute(choiceContext);
    }

    /// <summary>悬浮预览：下一回合（本牌基准回合 +1）里最新的至多 5 张牌，也就是这张牌要追忆的那一批。</summary>
    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(1);

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}
