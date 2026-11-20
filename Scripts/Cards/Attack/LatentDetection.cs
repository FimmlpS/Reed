using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Attack;

/// <summary>
/// 潜意：造成伤害，然后把**自己**记进接下来 <c>Repeat</c> 个回合的回忆（每个回合一条独立的快照）。
/// 那些回合开始时（<c>Hook.AfterPlayerTurnStart</c>，抽完牌之后）由
/// <see cref="IMemoryEventSubscriber.AfterRoundStarted"/> 到期追忆这一条 —— 也就是再自动打一遍。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class LatentDetection : AbstractReedCard, IMemoryEventSubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(5, ValueProp.Move),
        new RepeatVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public LatentDetection() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        // 追忆出来的那一份不再记录自己
        if (cardPlay.IsFromRecall())
        {
            return;
        }

        // 写进未来回合：基准 = 此牌视为打出的回合（被追忆打出的情况上面已经挡掉），偏移 +1 … +Repeat。
        for (int i = 1; i <= DynamicVars.Repeat.IntValue; i++)
        {
            await RecordCmd.Record(choiceContext, Owner)
                .FromPlay(cardPlay)
                .Offset(i)
                .Card(this)
                .Execute(choiceContext);
        }
    }

    async Task IMemoryEventSubscriber.AfterRoundStarted(
        PlayerChoiceContext choiceContext, Player player, RememberedCard entry)
    {
        // 快照的 CombatState 由补丁补齐，但直接从玩家身上问少一层 owner 依赖。
        ICombatState? combat = player.Creature?.CombatState;
        if (combat == null || player != Owner || !ReferenceEquals(entry.Card, this))
        {
            return;
        }
        if (entry.RoundNumber != combat.RoundNumber)
        {
            return; // 还没到期 / 早已过期
        }
        if (!MemorySystem.Forget(combat, player, entry))
        {
            return;
        }

        await RecallCmd.Recall(choiceContext, player)
            .FromRound(entry.RoundNumber)
            .Card(entry.Card)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(1);
        DynamicVars.Repeat.UpgradeValueBy(1);
    }
}
