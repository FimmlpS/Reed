using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 红龙的恨意：目标每次失去/恢复抗性时，最终数值 +<see cref="PowerModel.Amount"/>
/// （例：灼烧 1 实际扣 2；燃烧状态下恢复 1 实际回 2）。
/// <para>移除时机固定为「目标<b>下一次离开燃烧状态</b>」，所以当前是否在燃烧都可以挂上：
/// 不在燃烧时它先挂着，等目标燃起来、再在下一次熄灭的那一刻一起移除。</para>
/// </summary>
public class HatePower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    int IFireFlowerSubscriber.ModifyResistanceChange(Creature target, int amount, bool isLoss)
    {
        if (target != Owner || Amount <= 0)
        {
            return amount;
        }
        return amount + Amount;
    }

    async Task IFireFlowerSubscriber.AfterExitBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {
        if (target != Owner)
        {
            return;
        }
        await PowerCmd.Remove(this);
    }
}
