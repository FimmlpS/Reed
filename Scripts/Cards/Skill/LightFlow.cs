using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Status;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class LightFlow : AbstractReedCard, IMemorySubscriber
{

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromCard<FlameLight>()
    ];

    public LightFlow() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<LightFlowPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

        await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .OfType(CardType.Skill)
        .Execute(choiceContext);

        await PowerCmd.Apply<LightFlowPower>(choiceContext, Owner.Creature, -1, Owner.Creature, this);
    }

    /// <summary>悬浮预览：上一回合（本牌基准回合 -1）里最新的至多 5 张技能牌。</summary>
    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c => c.Type == CardType.Skill);

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
