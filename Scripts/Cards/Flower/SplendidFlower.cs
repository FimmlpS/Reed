using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Helper;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

[Pool(typeof(TokenCardPool))]
public class SplendidFlower : AbstractReedCard, IFireFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(24, ValueProp.Unpowered),
        new CardsVar(2),
        new EnergyVar(1)
    ];

    public SplendidFlower() : base(-1, CardType.Status, CardRarity.Token, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        
    }

    async Task IFireFlower.OnBurnt(PlayerChoiceContext playerChoiceContext, FireBurnt fireBurnt)
    {
        for(int i = 0; i < fireBurnt.Times; i++)
        {
            ReedHelper.CreateFireFx(fireBurnt.Target);
            await CreatureCmd.Damage(playerChoiceContext,fireBurnt.Target,DynamicVars.Damage.BaseValue * fireBurnt.Blv,ValueProp.Unpowered,Owner.Creature,this,null);
        }

        for(int i = 0; i < fireBurnt.Times; i++)
        {
            await CardPileCmd.Draw(playerChoiceContext,DynamicVars.Cards.BaseValue, Owner);
            await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue,Owner);
        }
    }

    void IFireFlower.ApplyLivingForm(WhichLivingForm form)
    {
        // 生灵：抽牌 2 → 4；死灵：能量 1 → 2。另一项保持基准；普通形态两项都回基准值。
        // 两项都从 canonical 的数值重算（而不是在现值上加减），形态来回切换才不会越叠越多。
        SplendidFlower template = ModelDb.Card<SplendidFlower>();
        DynamicVars.Cards.BaseValue = form == WhichLivingForm.Alive
            ? template.DynamicVars.Cards.BaseValue + 2m
            : template.DynamicVars.Cards.BaseValue;
        DynamicVars.Energy.BaseValue = form == WhichLivingForm.Dead
            ? template.DynamicVars.Energy.BaseValue + 1m
            : template.DynamicVars.Energy.BaseValue;
    }

    bool IFireFlower.IsAdvanced => true; // 二阶花（24 伤害）；最高阶，无上级

    public override int MaxUpgradeLevel => 0;

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}