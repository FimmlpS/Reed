using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 焰阳：获得 13（升级 16）点格挡；本回合受到攻击时，对来源灼烧 2（升级 3）。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class FlameSun : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(13, ValueProp.Move),
        new PowerVar<FlameSunPower>("FlameSunPower", 2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public FlameSun() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<FlameSunPower>(choiceContext, Owner.Creature,
            DynamicVars["FlameSunPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
        DynamicVars["FlameSunPower"].UpgradeValueBy(1);
    }
}
