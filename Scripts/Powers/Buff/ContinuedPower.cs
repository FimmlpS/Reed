using MegaCrit.Sts2.Core.Entities.Powers;

namespace Reed.Scripts.Powers;

/// <summary>
/// 待续的标记：本场战斗额外获得 <see cref="MegaCrit.Sts2.Core.Models.PowerModel.Amount"/> 组选牌奖励。
/// <para>真正把奖励塞进战斗房间的是「待续」卡本身 —— 与原版 TheHunt / TheHuntPower 一样的分工：
/// 能力只负责给玩家看，战斗结束随能力清空。</para>
/// </summary>
public class ContinuedPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;
}
