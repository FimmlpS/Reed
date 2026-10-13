using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Power;

[Pool(typeof(ReedCardPool))]
public class LightShadow : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameLight>(),
        HoverTipFactory.FromCard<FlameShadow>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Burning)
    ];

    public LightShadow() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<LightShadowPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }
}
