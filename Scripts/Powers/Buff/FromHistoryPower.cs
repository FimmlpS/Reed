using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Powers;

public class FromHistoryPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!cardPlay.IsFromRecall()
            || !cardPlay.IsFirstInSeries
            || Owner.Player is not Player player
            || !ReferenceEquals(cardPlay.Player, player))
        {
            return;
        }

        int round = cardPlay.PlayedRound(CombatState);
        List<CardModel> pool = player.Character.CardPool
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(c => c is not IMemorySubscriber && (c.Type == CardType.Attack||c.Type ==CardType.Skill||c.Type ==CardType.Power))
            .ToList();

        Flash();
        foreach (CardModel card in CardFactory.GetDistinctForCombat(player, pool, (int)Amount, player.RunState.Rng.CombatCardGeneration))
        {
            // RecordAsync（而不是同步的 Record）：这些牌也要算进「博物」那类订阅者的进出计数。
            await MemorySystem.RecordAsync(choiceContext, CombatState, player, round, card);
        }
        return;
    }
}
