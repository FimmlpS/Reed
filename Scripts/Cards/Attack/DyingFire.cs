using BaseLib.Utils;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class DyingFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(24,ValueProp.Move),
        new RepeatVar(1)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Resistance)
    ];

    public DyingFire() : base(3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        await cardPlay.Target.ChangeMaxResistance(-DynamicVars.Repeat.IntValue);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
        RemoveKeyword(CardKeyword.Exhaust);
    }
}