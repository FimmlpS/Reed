using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class EmperorTravel : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public EmperorTravel() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? played = await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .OfType(CardType.Attack)
        .Execute(choiceContext);

        if (played == null)
        {
            return;
        }
        await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .Card(this)   // 指定牌：不弹选牌屏，也不消耗回忆，直接打出这张牌的副本
        .Execute(choiceContext);
    }

    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c => c.Type == CardType.Attack);

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
