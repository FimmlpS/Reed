using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Cards.Flower;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Cards.Status;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class IgniteLife : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new CardsVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<BurnMarkFlower>(),
        HoverTipFactory.FromCard<FlameShadow>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    public IgniteLife() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<BurnMarkFlower>(), cardPlay.Target);
        await this.CreateInHand<FlameShadow>(DynamicVars.Cards.IntValue);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}