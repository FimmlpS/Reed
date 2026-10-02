using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Resistance;
using Reed.Scripts.Cards.Status;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class HerShadow : AbstractReedCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameShadow>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BurnVar(2),
        new CardsVar(2)
    ];

    public HerShadow() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .FromCard(this,cardPlay)
        .Targeting(CombatState?.GetOpponentsOf(Owner.Creature).ToList()??[])
        .Execute(choiceContext);

        await this.CreateInHand<FlameShadow>(DynamicVars.Cards.IntValue);
    }    

    protected override void OnUpgrade()
    {
        DynamicVars.Burn().UpgradeValueBy(1);
    }
}