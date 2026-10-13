using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(ReedCardPool))]
public class BlazeFlower : AbstractBurnMarkFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(24, ValueProp.Unpowered)
    ];

    protected override decimal DamageDecrease => 0.6m;

    protected override decimal DamageIncrease => 1.4m;
}
