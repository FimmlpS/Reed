using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Status;

[Pool(typeof(ReedCardPool))]
public class FlameShadow : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BurnVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public FlameShadow() : base(0, CardType.Status, CardRarity.Token, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .FromCard(this,cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this)
        {
            if(card.Pile?.Type!=PileType.Hand && card.Pile?.Type != PileType.Exhaust)
            {
                await CardCmd.Exhaust(new ThrowingPlayerChoiceContext(),this,false);
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Burn().UpgradeValueBy(1);
    }
}