using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Resistance;

public interface IFireFlowerSubscriber
{
    public virtual async Task OnBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        
    }

    public virtual FireBurnt ModifyBurnt(CardModel fireFlower, FireBurnt fireBurnt)
    {
        return fireBurnt;
    }

    public virtual async Task AfterModifyBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        
    }
}