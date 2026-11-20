using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class Rekindle : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public Rekindle() : base(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        ICombatState? combat = Owner.Creature?.CombatState;
        int? round = combat == null
            ? null
            : EarliestAttackRound(MemorySystem.Of(Owner), MemorySystem.MinRound(combat, Owner), combat.RoundNumber);
        if (round == null)
        {
            return;
        }

        await RecallCmd.Recall(choiceContext, Owner)
        .FromRound(round.Value)
        .OfType(CardType.Attack)
        .Execute(choiceContext);
    }

    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
    {
        if (context.Memory == null)
        {
            return [];
        }
        int minRound = context.Player == null ? 1 : MemorySystem.MinRound(context.Player.Creature?.CombatState, context.Player);
        int? round = EarliestAttackRound(context.Memory, minRound, context.CurrentRound);
        return round == null ? [] : context.LatestAt(round.Value, c => c.Type == CardType.Attack);
    }

    /// <summary>最早一条「有攻击牌记录」的回合（只看当前回合及以前）。</summary>
    private static int? EarliestAttackRound(PlayerMemory memory, int minRound, int maxRound)
    {
        foreach (int round in memory.Rounds)
        {
            if (round < minRound || round > maxRound)
            {
                continue;
            }
            if (memory.PeekRound(round).Any(e => e.Card.Type == CardType.Attack))
            {
                return round;
            }
        }
        return null;
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}
