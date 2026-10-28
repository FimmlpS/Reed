using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class IncFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Move),
        new PowerVar<IncFirePower>("IncFirePower", 2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn),
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    public IncFire() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target == null)
        {
            return;
        }

        await PowerCmd.Apply<IncFirePower>(choiceContext, cardPlay.Target, DynamicVars["IncFirePower"].BaseValue, Owner.Creature, this);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars["IncFirePower"].UpgradeValueBy(1);
    }
}
