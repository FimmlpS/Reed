using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 挽歌：每名生物在每回合首次被你附着一张牌时，你获得 <see cref="PowerModel.Amount"/> 点格挡。
/// <para>「本回合已触发过」记在生物身上的 <see cref="ElegyAttachedPower"/>（按施予者区分实例），
/// 玩家回合结束时由它自己移除；同一生物、同一玩家每回合只结算一次。</para>
/// </summary>
public class ElegyPower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    async Task IFireFlowerSubscriber.AfterAttach(PlayerChoiceContext playerChoiceContext, Player owner, CardModel card, Creature target)
    {
        if (Owner.Player is not Player me || owner != me || Amount <= 0)
        {
            return; // 只认自己的附着（多人：别人的挽歌不替你结算）
        }
        if (target.CombatState == null || target.IsDead || !target.CanReceivePowers)
        {
            return;
        }
        // 已经带着「自己这份」标记 → 本回合已触发过，不再给格挡。
        if (target.GetPowerInstances<ElegyAttachedPower>().Any(p => p.Applier == Owner))
        {
            return;
        }
        ElegyAttachedPower? marker = await PowerCmd.Apply<ElegyAttachedPower>(playerChoiceContext, target, 1m, Owner, null);
        if (marker == null)
        {
            return; // 标记挂不上（目标此刻不接受能力）就别给格挡，否则会无限重复触发
        }
        Flash();
        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null);
    }
}
