using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Reed.Scripts.Cards.Power;

[Pool(typeof(ReedCardPool))]
public class NaturalHistory : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<NaturalHistoryPower>("NaturalHistoryPower", 1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public NaturalHistory() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<NaturalHistoryPower>(choiceContext, Owner.Creature, DynamicVars["NaturalHistoryPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
