using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Helper;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

/// <summary>
/// 巡标：爆发时对宿主造成 8 点伤害；被从宿主身上移除时，令来源获得 1 点能量，
/// 以及（生灵）8 点格挡 /（死灵）4 点活力。上级形态是 <see cref="FlagFlower"/>。
/// </summary>
[Pool(typeof(TokenCardPool))]
public class PatrolFlower : AbstractReedCard, IFireFlower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Unpowered),
        new EnergyVar(1),
        new BlockVar(8, ValueProp.Move),    // 生灵形态：被移除时令来源获得格挡
        new PowerVar<VigorPower>(4)         // 死灵形态：被移除时令来源获得活力
    ];

    public PatrolFlower() : base(-1, CardType.Status, CardRarity.Token, TargetType.Self)
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

    CardModel IFireFlower.GetUpperCard()
    {
        return ModelDb.Card<FlagFlower>();
    }

    bool IFireFlower.IsAdvanced => false; // 一阶：上级是 FlagFlower

    public override int MaxUpgradeLevel => 0;

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}
