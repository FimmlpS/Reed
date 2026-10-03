using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class AsTorch : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<AsTorchPower>("AsTorchPower",1)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public AsTorch() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<AsTorchPower>(choiceContext,cardPlay.Target,DynamicVars["AsTorchPower"].BaseValue,Owner.Creature,this);
    }    

    protected override void OnUpgrade()
    {
        DynamicVars["AsTorchPower"].UpgradeValueBy(1);
    }
}