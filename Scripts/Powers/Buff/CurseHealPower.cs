using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Powers;

public class CurseHealPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if(dealer == Owner && cardSource != null)
        {
            decimal block = 0.25m * result.TotalDamage * Amount;
            if (block > 0)
            {
                Flash();
                await CreatureCmd.GainBlock(Owner, block, ValueProp.Unpowered, null);
            }
        }
    }
}