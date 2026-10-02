using BaseLib.Utils;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class Scorch : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(3,ValueProp.Move),
        new RepeatVar(3),
        new BurnVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public Scorch() : base(2, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .OnlyPlayAnimOnce()
        .WithHitCount(DynamicVars.Repeat.IntValue)
        .Execute(choiceContext);

        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(1);
        DynamicVars.Burn().UpgradeValueBy(1);
    }
}