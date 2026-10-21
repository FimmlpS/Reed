using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(TokenCardPool))]
public class BurnMarkFlower : AbstractBurnMarkFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Unpowered)
    ];

    protected override decimal DamageDecrease => 0.75m;

    protected override decimal DamageIncrease => 1.25m;

    // 形态：生灵 → 压得更狠（降40％），死灵 → 被打得更疼（升40％）。
    // 另一项在各自的形态里保持基础值（生灵不额外增伤、死灵不额外减伤），所以只覆写这两项。
    protected override decimal AliveDamageDecrease => 0.6m;

    protected override decimal DeadDamageIncrease => 1.4m;

    protected override CardModel? UpperCard => ModelDb.Card<BlazeFlower>();
}
