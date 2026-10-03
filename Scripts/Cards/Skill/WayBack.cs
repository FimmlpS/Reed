using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class WayBack : AbstractReedCard, IFireFlowerSubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<ShineFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public WayBack() : base(3, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<ShineFlower>(), cardPlay.Target);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }

    async Task IFireFlowerSubscriber.OnBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        EnergyCost.AddUntilPlayed(-fireBurnt.Times);
    }
}