using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Powers;

public class RecallPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    Stack<CardModel> applyModels = new Stack<CardModel>();

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if(cardSource!=null)
            applyModels.Push(cardSource);
        return Task.CompletedTask;
    }

    public async override Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (applyModels.Count > 0 && applyModels.Peek()==cardPlay.Card)
        {
            applyModels.Pop();
            return;
        }
        if (cardPlay.IsAutoPlay || cardPlay.Card.Owner != Owner.Player)
        {
            return;
        }
        Flash();
        await RecallCmd.Recall(choiceContext, Owner.Player)
        .Offset(-1)
        .Where(c => (c.Type == CardType.Attack || c.Type == CardType.Skill) && c.EnergyCost.GetAmountToSpend()<=Amount)
        .Execute(choiceContext);
        await PowerCmd.ModifyAmount(choiceContext, this, -1, Owner, null);
    }
}