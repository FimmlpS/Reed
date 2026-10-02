using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class AsTorchPower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    FireBurnt IFireFlowerSubscriber.ModifyBurnt(CardModel fireFlower, FireBurnt fireBurnt)
    {
        FireBurnt b = new FireBurnt(fireBurnt).ModifyTimes(Amount);
        return b;
    }

    async Task IFireFlowerSubscriber.AfterModifyBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        await PowerCmd.Remove(this);
    }
}