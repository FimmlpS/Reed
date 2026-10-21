using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Powers;

public class DragonCrownPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    private class Data
    {
        public int usesLeft;
    }

    public override int DisplayAmount => GetInternalData<Data>().usesLeft;

    protected override object InitInternalData()
    {
        return new Data();
    }

    private bool IsOwnerStatus(CardModel card)
    {
        return Owner.Player is Player player && card.Owner == player && card.Type == CardType.Status;
    }

    // 官方全局关键词钩子：次数没用完时把手牌里状态牌的 Unplayable 摘掉（CanPlay / 手牌灰显都读 Keywords）；
    // 归零后返回 false，关键词自动恢复，不需要手动加回去。
    public override bool TryModifyKeywordsInCombat(CardModel card, ISet<CardKeyword> keywords)
    {
        if (GetInternalData<Data>().usesLeft <= 0 || !IsOwnerStatus(card) || card.Pile?.Type != PileType.Hand)
        {
            return false;
        }
        return keywords.Remove(CardKeyword.Unplayable);
    }

    // 官方结果堆钩子（同 CorruptionPower）：状态牌打出后进消耗堆，而不是弃牌堆。
    public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay, ResourceInfo resources, CardLocation location)
    {
        if (GetInternalData<Data>().usesLeft <= 0 || !IsOwnerStatus(card))
        {
            return location;
        }
        location.pileType = PileType.Exhaust;
        return location;
    }

    // 首次施加与之后叠层都走这里（amount 为本次施加量）。
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (!ReferenceEquals(power, this) || amount <= 0)
        {
            return Task.CompletedTask;
        }
        GetInternalData<Data>().usesLeft += (int)amount;
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    // 在抽牌之前重置：AfterPlayerTurnStart 是在起手抽牌之后才广播的，放那里会让刚抽到的状态牌仍按「次数已用完」渲染。
    public override Task AfterEnergyReset(Player player)
    {
        if (!ReferenceEquals(player, Owner.Player))
        {
            return Task.CompletedTask;
        }
        GetInternalData<Data>().usesLeft = Amount;
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.IsAutoPlay || !cardPlay.IsFirstInSeries || !IsOwnerStatus(cardPlay.Card))
        {
            return;
        }
        Data data = GetInternalData<Data>();
        if (data.usesLeft <= 0)
        {
            return;
        }

        // 先抽牌，再扣次数。
        if (Owner.Player is Player player)
        {
            await CardPileCmd.Draw(choiceContext, 1, player);
        }
        data.usesLeft--;
        InvokeDisplayAmountChanged();
    }
}
