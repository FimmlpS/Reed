using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class HerFlamePower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        Creature target = null;
        IEnumerable<Creature> targets = GetPossibleTargets().Where(c=>c.IsAlive && !c.IsBurningResistance());
        if (targets.Count() > 0)
        {
            target = targets.TakeRandom(1,CombatState.RunState.Rng.CombatTargets).FirstOrDefault();
        }
        if(target == null)
        {
            targets = GetPossibleTargets().Where(c=>c.IsAlive);
            if(targets.Count() > 0)
            {
                target = targets.TakeRandom(1,CombatState.RunState.Rng.CombatTargets).FirstOrDefault();
            }
        }
        if(target != null)
        {
            await ReedBurnCmd.Burn(Amount)
            .FromCreature(Owner)
            .Targeting(target)
            .Execute(choiceContext);
        }
    }

    private IReadOnlyList<Creature> GetPossibleTargets()
    {
        if(CombatState == null)
        {
            return [];
        }
        return CombatState.GetOpponentsOf(Owner);
    }
}