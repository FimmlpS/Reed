using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class HeartClear : AbstractReedCard, IMemoryEventSubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(11,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory),
        HoverTipFactory.FromKeyword(ReedKeywords.Recall)
    ];

    public HeartClear() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
    }

    async Task IMemoryEventSubscriber.AfterRecalled(PlayerChoiceContext choiceContext, Player player, CardModel card)
    {
        ICombatState? combat = player?.Creature?.CombatState ?? CombatState;
        if (combat == null || card == null)
        {
            return;
        }
        // 「如果此牌在回忆中」：round = 自己那条回忆所在的回合
        if (!MemorySystem.IsInMemory(player, this, out int round))
        {
            return;
        }
        // 「其他牌」
        if (ReferenceEquals(card, this))
        {
            return;
        }
        // 「当前回合」= 被追忆的那张牌所在的回合，要和自己在同一个回合桶里
        if (card.PlayedRound(combat) != round)
        {
            return;
        }

        await RecallCmd.Recall(choiceContext, player)
            .FromRound(round)
            .Where(c => ReferenceEquals(c, this))
            .Execute(choiceContext);
    }
}