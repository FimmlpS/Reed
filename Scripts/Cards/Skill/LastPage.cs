using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class LastPage : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(8,ValueProp.Move),
        new EnergyVar(1)
    ];

    public LastPage() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature,DynamicVars.Block,cardPlay);
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue,Owner);
        EnergyCost.AddThisCombat(1);
        DynamicVars.Energy.BaseValue+=1;
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}
