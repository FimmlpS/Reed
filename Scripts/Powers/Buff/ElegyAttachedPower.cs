using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace Reed.Scripts.Powers;

/// <summary>
/// 挽歌的「本回合已触发」标记：挂在被附着的那名生物身上，主要是给玩家的提示，
/// 玩家回合结束时移除。
/// <para><see cref="PowerInstanceType.InstancedPerApplier"/>：按施予者区分实例 ——
/// 多人下每个玩家的挽歌各有一份独立标记，互不占用；
/// 同一玩家对同一生物只会有一份（<see cref="ElegyPower"/> 据此判重）。</para>
/// </summary>
public class ElegyAttachedPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.InstancedPerApplier;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Player)
        {
            await PowerCmd.Remove(this);
        }
    }
}
