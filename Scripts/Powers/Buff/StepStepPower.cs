using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Powers;

public class StepStepPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (Owner.Player is not Player owner || !ReferenceEquals(player, owner))
        {
            return;
        }

        Flash();
        await RecallCmd.Recall(choiceContext, owner)
            .FromRound(Amount)
            .Where(c => c.Type == CardType.Attack || c.Type == CardType.Skill)
            .Execute(choiceContext);
        await PowerCmd.ModifyAmount(choiceContext, this, 1, Owner, null);
    }
}
