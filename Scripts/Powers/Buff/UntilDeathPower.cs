using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class UntilDeathPower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    async Task IFireFlowerSubscriber.AfterExitBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {
        if (Owner.Player is not Player player || target.Side == Owner.Side || Amount <= 0)
        {
            return;
        }
        if (target.CombatState == null && CombatState == null)
        {
            return; // 不在战斗中：没有火花可引爆（顺带避免 SparksOf 在这里凭空建一份数据）
        }
        for (int i = 0; i < Amount; i++)
        {
            // 【多人安全】这里原本是 TakeRandom(1, combat.RunState.Rng.CombatTargets)：TakeRandom = 整表洗牌后取前 N，
            // 消耗的随机数个数 = 候选表长度 - 1，而候选表是 mod 本地那份火花表 —— 两端火花张数/顺序一旦有半点不同，
            // 整条战斗随机数流就从这里开始错位，之后每个动作两边算出来的都不一样（同 ChaseFirePower 的隐患）。
            // 改成确定性规则：每轮引爆当前伤害最低的那张火花（并列取列表靠前），引爆后它会从列表里移除，
            // 因此 Amount 轮的结果 = 「依次引爆最弱的 Amount 张」，两端完全一致，且完全不碰同步随机数。
            // 另把“候选必须是 IFireFlower”从循环内的 if 提到筛选里：否则最弱的那张若不是火花卡，
            // 它每轮都会被选中又无法被移除，Amount 轮全部原地空转、一张都引爆不了。
            CardModel? spark = ResistanceSystem.SparksOf(target, player)
                .Where(s => s is IFireFlower)
                .OrderBy(ResistanceSystem.SparkDamageOf)
                .FirstOrDefault();
            if (spark == null)
            {
                break;
            }
            await ReedAttachCmd.Burnt(playerChoiceContext, spark, target);
        }
    }
}
