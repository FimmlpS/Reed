using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Resistance;

public interface IFireFlowerSubscriber
{
    public virtual async Task OnBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        
    }

    public virtual FireBurnt ModifyBurnt(CardModel fireFlower, FireBurnt fireBurnt, bool triggerByBurning)
    {
        return fireBurnt;
    }

    public virtual async Task AfterModifyBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt, bool triggerByBurning)
    {
        
    }

    public virtual int ModifySparkMax(Creature c, Player owner, int amount)
    {
        return amount;
    }

    public virtual async Task AfterAllBurnt(PlayerChoiceContext playerChoiceContext, Creature c, bool triggerByBurning)
    {
        
    }

    public virtual async Task BeforeBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {
        
    }

    public virtual async Task AfterBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {
        
    }
}