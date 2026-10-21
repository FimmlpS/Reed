using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.DynamicVars;

namespace Reed.Scripts.Powers;

public class LightFlowPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public async override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if(cardPlay.Card.Owner == Owner.Player)
        {
            await cardPlay.Card.CreateInHand<FlameLight>(Amount);
        }
    }
}