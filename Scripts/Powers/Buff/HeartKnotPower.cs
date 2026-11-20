using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Powers;

public class HeartKnotPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if(cardPlay.Card.Owner.Creature == Owner && cardPlay.IsAutoPlay)
        {
            await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null);
        }
    }

    /// <summary>
    /// 回合结束时手牌自动结算（<c>HasTurnEndInHandEffect</c>，如焰光）不会产生 <see cref="CardPlay"/>，
    /// 收不到 <see cref="AfterCardPlayed"/>，由 <c>TurnEndInHandPatch</c> 调到这里补一次。
    /// </summary>
    public static async Task NotifyTurnEndInHand(PlayerChoiceContext choiceContext, CardModel card)
    {
        Creature? owner = card.Owner?.Creature;
        if (owner == null || owner.CombatState == null)
        {
            return;
        }
        foreach (HeartKnotPower knot in owner.Powers.OfType<HeartKnotPower>())
        {
            await CreatureCmd.GainBlock(owner, knot.Amount, ValueProp.Unpowered, null);
        }
    }
}
