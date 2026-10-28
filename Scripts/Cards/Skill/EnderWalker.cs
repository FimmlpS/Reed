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

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class EnderWalker : AbstractReedCard, IMemoryEventSubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(4, ValueProp.Move),
        new RepeatVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public EnderWalker() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

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
        // 不能用 this.CombatState：回忆里的快照不进任何牌堆，CardModel.CombatState 对它恒为 null
        // （只有「牌在战斗牌堆里」才返回），只能从玩家身上问。
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
        DynamicVars.Block.UpgradeValueBy(1);
        DynamicVars.Repeat.UpgradeValueBy(1);
    }
}
