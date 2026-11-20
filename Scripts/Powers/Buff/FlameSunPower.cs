using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 焰阳：本回合你受到攻击时，对攻击来源灼烧 <see cref="PowerModel.Amount"/>；
/// <b>下一个玩家回合开始时</b>移除 —— 不能在回合结束时移除，否则撑不到敌方行动回合。
/// <para>触发时机对齐原版荆棘（<c>ThornsPower</c>）：<see cref="AbstractModel.BeforeDamageReceived"/> 里
/// 只认带 <see cref="ValueProp.Move"/> 的攻击（<see cref="ValuePropExtensions.IsPoweredAttack"/>），
/// 于是被格挡住的攻击也照样触发，而反伤、灼烧这类非攻击伤害不触发。</para>
/// </summary>
public class FlameSunPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || dealer == null || dealer == Owner)
        {
            return;
        }
        if (!props.IsPoweredAttack())
        {
            return;
        }
        Flash();
        await ReedBurnCmd.Burn(Amount)
            .FromCreature(Owner)
            .Targeting(dealer)
            .Execute(choiceContext);
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        // 打出时玩家回合已经开始，所以这里命中的一定是「下一个」玩家回合：覆盖整个敌方行动回合。
        if (side == CombatSide.Player)
        {
            await PowerCmd.Remove(this);
        }
    }
}
