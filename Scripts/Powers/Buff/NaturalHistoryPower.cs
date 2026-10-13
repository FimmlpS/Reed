using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Powers;

public class NaturalHistoryPower : AbstractReedPower, IMemoryEventSubscriber
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    private class Data
    {
        public int cardsLeft = _baseCardsLeft;
    }

    public override int DisplayAmount => GetInternalData<Data>().cardsLeft;

    private const int _baseCardsLeft = 10;

    private const string _baseCardsKey = "BaseCards";

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar(_baseCardsKey, _baseCardsLeft)
    ];

    protected override object InitInternalData()
    {
        return new Data();
    }

    public Task AfterRemembered(PlayerChoiceContext choiceContext, Player player, CardModel card)
        => Count(choiceContext, player);

    public Task AfterRecalled(PlayerChoiceContext choiceContext, Player player, CardModel card)
        => Count(choiceContext, player);

    private async Task Count(PlayerChoiceContext choiceContext, Player player)
    {
        if (choiceContext == null || Owner.Player is not Player owner || !ReferenceEquals(player, owner))
        {
            return;
        }

        Data data = GetInternalData<Data>();
        data.cardsLeft--;
        if (data.cardsLeft > 0)
        {
            InvokeDisplayAmountChanged();
            return;
        }

        Flash();
        await CardPileCmd.Draw(choiceContext, Amount, owner);
        data.cardsLeft = _baseCardsLeft;
        InvokeDisplayAmountChanged();
    }
}
