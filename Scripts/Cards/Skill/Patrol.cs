using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Patrol : AbstractReedCard
{
    protected override bool HasEnergyCostX => true;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<NormalFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public Patrol() : base(0, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int energyCost = cardPlay.Resources.EnergyValue + (IsUpgraded?1:0);
        for(int i = 0; i < energyCost; i++)
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<NormalFlower>(), cardPlay.Target);
        }
    }    

    protected override void OnUpgrade()
    {

    }
}