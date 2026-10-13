using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class HeartKnot : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<HeartKnotPower>("HeartKnotPower",2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.Static(StaticHoverTip.Block)
    ];

    public HeartKnot() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<HeartKnotPower>(choiceContext, Owner.Creature, DynamicVars["HeartKnotPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["HeartKnotPower"].UpgradeValueBy(1);
    }
}