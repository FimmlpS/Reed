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
public class ToTheDeath : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public ToTheDeath() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? recalled = await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .Where(c=>c.Type==CardType.Skill || (IsUpgraded && c.Type==CardType.Attack))
        .Execute(choiceContext);

        if (recalled != null)
        {
            await RecordCmd.Record(choiceContext,Owner)
            .FromPlay(cardPlay)
            .Offset(0)
            .Card(recalled)
            .Execute(choiceContext);
        }
    }

    /// <summary>悬浮预览：上一回合（本牌基准回合 -1）里最新的至多 5 张技能牌。</summary>
    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c=>c.Type==CardType.Skill || (IsUpgraded && c.Type==CardType.Attack));

    protected override void OnUpgrade()
    {
        
    }
}
