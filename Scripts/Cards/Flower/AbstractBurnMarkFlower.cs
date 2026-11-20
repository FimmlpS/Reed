using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Helper;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Flower;

/// <summary>
/// 「灼痕 / 烈焰」这一族的公共实现 —— 带<b>持续</b>效果的火花牌：挂上后不止被动等爆发，
/// 只要宿主还处于燃烧状态，就同时给它加上**仿虚弱 + 仿易伤**的伤害修正：
/// 宿主造成的攻击伤害 ×<see cref="DamageDecrease"/>，宿主受到的攻击伤害 ×<see cref="DamageIncrease"/>。
///
/// <para><b>怎么收到伤害 hook</b>：火花牌不在任何牌堆里，原版不会把卡牌算进 hook 监听者，
/// 靠 <see cref="ResistanceSystem.RegisterHookSubscription"/> 把已 attach 的火花登记给
/// <c>CombatState.IterateHookListeners</c>；宿主则由 <see cref="ResistanceSystem.HostOf"/> 反查（attach 时记下）。</para>
/// </summary>
public abstract class AbstractBurnMarkFlower : AbstractReedCard, IFireFlower
{
    protected AbstractBurnMarkFlower() : base(-1, CardType.Status, CardRarity.Token, TargetType.Self)
    {
    }

    /// <summary>宿主造成的攻击伤害的倍率（仿虚弱：<c>0.75</c> = 降低 25%）。</summary>
    protected abstract decimal DamageDecrease { get; }

    /// <summary>宿主受到的攻击伤害的倍率（仿易伤：<c>1.25</c> = 提升 25%）。</summary>
    protected abstract decimal DamageIncrease { get; }

    /// <summary>
    /// 「生死之灵」形态下的倍率：生灵偏向压低宿主的输出（<see cref="AliveDamageDecrease"/>），
    /// 死灵偏向放大宿主所受的伤害（<see cref="DeadDamageIncrease"/>）。
    /// 默认都和基础值相同 —— 只覆写<b>本族的形态确实改变了的那几项</b>，没覆写的项形态下不变。
    /// </summary>
    protected virtual decimal AliveDamageDecrease => DamageDecrease;

    /// <inheritdoc cref="AliveDamageDecrease"/>
    protected virtual decimal AliveDamageIncrease => DamageIncrease;

    /// <inheritdoc cref="AliveDamageDecrease"/>
    protected virtual decimal DeadDamageDecrease => DamageDecrease;

    /// <inheritdoc cref="AliveDamageDecrease"/>
    protected virtual decimal DeadDamageIncrease => DamageIncrease;

    /// <summary>本卡的上级形态（合体进阶用）；null = 没有上级。</summary>
    protected virtual CardModel? UpperCard => null;

    /// <summary>本卡是否为进阶形态（每族花的最上级）；供按张数计伤的效果使用，见 <see cref="IFireFlower.IsAdvanced"/>。</summary>
    protected virtual bool IsAdvancedTier => false;

    public override int MaxUpgradeLevel => 0;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        Creature? host = ResistanceSystem.HostOf(this);
        // 没挂在任何人身上 / 不是攻击伤害（同虚弱·易伤的口径）/ 宿主没在燃烧 → 都不生效。
        if (host == null || !props.IsPoweredAttack() || !host.IsBurningResistance())
        {
            return 1m;
        }
        decimal result = 1m;
        // 形态取自火花的 owner（把它附着上去的那名玩家），不是宿主：同一张火花挂在不同人身上，
        // 强度按各自玩家当前选的是生灵还是死灵走。普通形态（没打出生死之灵）= 卡自己的基础值。
        (decimal decrease, decimal increase) = LivingForm.Of(this) switch
        {
            WhichLivingForm.Alive => (AliveDamageDecrease, AliveDamageIncrease),
            WhichLivingForm.Dead => (DeadDamageDecrease, DeadDamageIncrease),
            _ => (DamageDecrease, DamageIncrease),
        };
        if (ReferenceEquals(dealer, host))
        {
            result *= decrease;
        }
        if (ReferenceEquals(target, host))
        {
            result *= increase;
        }
        return result;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
    }

    CardModel IFireFlower.GetUpperCard()
    {
        // 接口把返回值标成不可空，但约定就是 null = 没有上级（接口自己的默认实现也返回 null），
        // 所以这里照抄它的标记、用 ! 压掉不可空的空值告警。
        return UpperCard!;
    }

    bool IFireFlower.IsAdvanced => IsAdvancedTier;

    async Task IFireFlower.OnBurnt(PlayerChoiceContext playerChoiceContext, FireBurnt fireBurnt)
    {
        for (int i = 0; i < fireBurnt.Times; i++)
        {
            if (!ReedHelper.CanStillBurst(fireBurnt.Target))
            {
                break; // 目标已倒下/离场：剩余次数不再结算
            }
            ReedHelper.CreateFireFx(fireBurnt.Target);
            await CreatureCmd.Damage(playerChoiceContext, fireBurnt.Target, DynamicVars.Damage.BaseValue * fireBurnt.Blv, ValueProp.Unpowered, Owner.Creature, this, null);
        }
    }

    protected override void OnUpgrade()
    {
        base.OnUpgrade();
    }
}
