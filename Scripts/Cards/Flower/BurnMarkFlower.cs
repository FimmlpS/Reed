using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(ReedCardPool))]
public class BurnMarkFlower : AbstractBurnMarkFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Unpowered)
    ];

    protected override decimal DamageDecrease => 0.75m;

    protected override decimal DamageIncrease => 1.25m;

    protected override CardModel? UpperCard => ModelDb.Card<BlazeFlower>();
}
