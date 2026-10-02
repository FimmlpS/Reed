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
public class Thorn : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CalculationBaseVar(8),
        new ExtraDamageVar(7),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card,target)=> {
            if(target!=null && target.IsBurningResistance()){
                return 1;
            }
            return 0;
        })
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burning)
    ];

    public Thorn() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .WithHitFx("vfx/vfx_dramatic_stab")
        .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(2);
        DynamicVars.ExtraDamage.UpgradeValueBy(3);
    }
}