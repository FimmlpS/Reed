using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Patches;

namespace Reed.Scripts.Cards.Power;

[Pool(typeof(ReedCardPool))]
public class UntilDeath : AbstractReedCard, IFullCardSubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<UntilDeathPower>("UntilDeathPower", 1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burning),
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower)
    ];

    public UntilDeath() : base(1, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<UntilDeathPower>(choiceContext, Owner.Creature,
            DynamicVars["UntilDeathPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
