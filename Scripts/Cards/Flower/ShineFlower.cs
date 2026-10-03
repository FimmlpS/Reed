using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(ReedCardPool))]
public class ShineFlower : AbstractReedCard, IFireFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Unpowered),
        new CardsVar(1)
    ];

    public ShineFlower() : base(1, CardType.Status, CardRarity.Token, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        
    }

    async Task IFireFlower.OnBurnt(PlayerChoiceContext playerChoiceContext, FireBurnt fireBurnt)
    {
        for(int i = 0; i < fireBurnt.Times; i++)
        {
            await CreatureCmd.Damage(playerChoiceContext,fireBurnt.Target,DynamicVars.Damage.BaseValue * fireBurnt.Blv,ValueProp.Unpowered,Owner.Creature,this,null);
        }

        for(int i = 0; i < fireBurnt.Times; i++)
        {
            await CardPileCmd.Draw(playerChoiceContext,DynamicVars.Cards.BaseValue, Owner);
        }
    }

    CardModel IFireFlower.GetUpperCard()
    {
        return ModelDb.Card<SplendidFlower>();
    }

    public override int MaxUpgradeLevel => 0;

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}