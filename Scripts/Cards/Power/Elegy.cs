using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Power;

/// <summary>
/// 挽歌：每名生物在每回合首次被你附着一张牌时，你获得格挡。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class Elegy : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<ElegyPower>("ElegyPower", 5)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public Elegy() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ElegyPower>(choiceContext, Owner.Creature,
            DynamicVars["ElegyPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["ElegyPower"].UpgradeValueBy(2);
    }
}
