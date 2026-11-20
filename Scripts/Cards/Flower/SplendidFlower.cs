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
            if (!ReedHelper.CanStillBurst(fireBurnt.Target))
            {
                break; // 目标已倒下/离场：剩余次数不再结算（抽牌照旧）
            }
            ReedHelper.CreateFireFx(fireBurnt.Target);
            await CreatureCmd.Damage(playerChoiceContext,fireBurnt.Target,DynamicVars.Damage.BaseValue * fireBurnt.Blv,ValueProp.Unpowered,Owner.Creature,this,null);
        }

        for(int i = 0; i < fireBurnt.Times; i++)
        {
            IEnumerable<CardModel> drawn = await CardPileCmd.Draw(playerChoiceContext,DynamicVars.Cards.BaseValue, Owner);
            await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue,Owner);
            // 形态奖励现读：生灵 → 抽到的技能牌本回合耗能-1；死灵 → 抽到的攻击牌。普通形态只抽牌+回能。
            LivingForm.DiscountDrawn(this, drawn);
        }
    }

    bool IFireFlower.IsAdvanced => true; // 二阶花（24 伤害）；最高阶，无上级

    public override int MaxUpgradeLevel => 0;

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}