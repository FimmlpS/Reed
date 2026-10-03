using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class AsTorchPower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    FireBurnt IFireFlowerSubscriber.ModifyBurnt(CardModel fireFlower, FireBurnt fireBurnt, bool triggerByBurning)
    {
        if(fireBurnt.Target != Owner || !triggerByBurning)
        {
            return fireBurnt;
        }
        FireBurnt b = new FireBurnt(fireBurnt).ModifyTimes(Amount);
        return b;
    }

    async Task IFireFlowerSubscriber.AfterAllBurnt(PlayerChoiceContext playerChoiceContext, Creature c, bool triggerByBurning)
    {
        if(triggerByBurning)
            await PowerCmd.ModifyAmount(playerChoiceContext, this, -Amount, Owner, null);
    }
}