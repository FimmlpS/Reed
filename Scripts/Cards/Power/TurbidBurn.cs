using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Attack;

/// <summary>
/// 浊燃：前半段（一次性）向自己附着一张映耀火花，后半段获得 <see cref="TurbidBurnPower"/>。
/// 两张文本见 REED-TURBID_BURN.description / REED-TURBID_BURN_POWER.description。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class TurbidBurn : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<TurbidBurnPower>("TurbidBurnPower",1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<ShineFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public TurbidBurn() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 前半段（一次性）：向自己附着一张映耀火花。
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<ShineFlower>(), Owner.Creature);
        // 后半段：获得浊燃。
        await PowerCmd.Apply<TurbidBurnPower>(choiceContext, Owner.Creature, DynamicVars["TurbidBurnPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
