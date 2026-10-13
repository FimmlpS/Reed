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
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class AliveDraw : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new CardsVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<ShineFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    public AliveDraw() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<ShineFlower>(), cardPlay.Target);
        await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, Owner.Creature, DynamicVars.Cards.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}