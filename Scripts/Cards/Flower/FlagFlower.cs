using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Helper;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

/// <summary>
/// 旗帜：巡标的进阶形态。爆发时对宿主造成 24 点伤害；被移除时令来源获得 2 点能量，
/// 以及（生灵）16 点格挡 /（死灵）8 点活力。
/// </summary>
[Pool(typeof(TokenCardPool))]
public class FlagFlower : AbstractReedCard, IFireFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(24, ValueProp.Unpowered),
        new EnergyVar(2),
        new BlockVar(16, ValueProp.Move),   // 生灵形态：被移除时令来源获得格挡
        new PowerVar<VigorPower>(8)         // 死灵形态：被移除时令来源获得活力
    ];

    public FlagFlower() : base(-1, CardType.Status, CardRarity.Token, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {

    }

    async Task IFireFlower.OnBurnt(PlayerChoiceContext playerChoiceContext, FireBurnt fireBurnt)
    {
        for (int i = 0; i < fireBurnt.Times; i++)
        {
            if (!ReedHelper.CanStillBurst(fireBurnt.Target))
            {
                break; // 目标已倒下/离场：剩余次数不再结算
            }
            ReedHelper.CreateFireFx(fireBurnt.Target);
            await CreatureCmd.Damage(playerChoiceContext, fireBurnt.Target,
                DynamicVars.Damage.BaseValue * fireBurnt.Blv, ValueProp.Unpowered, Owner.Creature, this, null);
        }
    }

    async Task IFireFlower.OnRemoved(PlayerChoiceContext playerChoiceContext, Creature target)
    {
        await LivingForm.GrantRemovalReward(playerChoiceContext, this);
    }

    bool IFireFlower.IsAdvanced => true; // 二阶：最高形态，无上级

    public override int MaxUpgradeLevel => 0;

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}
