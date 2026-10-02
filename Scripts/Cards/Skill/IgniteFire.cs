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

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class IgniteFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<NormalFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    public IgniteFire() : base(1, CardType.Skill, CardRarity.Basic, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<NormalFlower>(), cardPlay.Target);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}