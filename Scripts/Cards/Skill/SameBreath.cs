using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Cards.Status;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class SameBreath : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameLight>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Unyielding)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(2)
    ];

    public SameBreath() : base(1, CardType.Skill, CardRarity.Rare, TargetType.None)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach(CardModel card in await this.CreateInHand<FlameLight>(DynamicVars.Cards.IntValue))
        {
            CardCmd.ApplyKeyword(card,ReedKeywords.Unyielding);
        }
    }    

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}