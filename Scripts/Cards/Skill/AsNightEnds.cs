using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Enums;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.DynamicVars;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class AsNightEnds : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower),
        HoverTipFactory.FromCard<FlameShadow>()
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override bool HasEnergyCostX => true;

    public AsNightEnds() : base(0, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int value = cardPlay.Resources.EnergyValue;
        await PowerCmd.Apply<AsTorchPower>(choiceContext,cardPlay.Target,value,Owner.Creature,this);
        await this.CreateInHand<FlameShadow>(value+(IsUpgraded?2:0),Owner);
    }    

    protected override void OnUpgrade()
    {
        
    }
}