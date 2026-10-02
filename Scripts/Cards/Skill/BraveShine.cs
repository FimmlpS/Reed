using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class BraveShine : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameLight>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(1),
        new PowerVar<LightPower>("LightPower",1)
    ];

    public BraveShine() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await this.CreateInHand<FlameLight>(DynamicVars.Cards.IntValue);
        await PowerCmd.Apply<LightPower>(choiceContext,Owner.Creature,DynamicVars["LightPower"].BaseValue,Owner.Creature,this);
    }    

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}