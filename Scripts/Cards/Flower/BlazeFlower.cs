using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(TokenCardPool))]
public class BlazeFlower : AbstractBurnMarkFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(24, ValueProp.Unpowered)
    ];

    protected override decimal DamageDecrease => 0.6m;

    protected override decimal DamageIncrease => 1.4m;

    // 形态：生灵 → 降60％（增伤仍为基础 40％），死灵 → 升60％（减伤仍为基础 40％）。
    protected override decimal AliveDamageDecrease => 0.4m;

    protected override decimal DeadDamageIncrease => 1.6m;

    protected override bool IsAdvancedTier => true; // 二阶（24 伤害）；最高阶，无上级
}
