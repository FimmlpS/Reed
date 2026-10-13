using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 浊燃：你每打出一张攻击牌，对<b>所有生物</b>（含你自己）灼烧 <see cref="PowerModel.Amount"/> 点抗性。
///
/// <para>「所有生物」是刻意的：这既是牌的设计，也是玩家侧抗性机制的验收入口 —— 自己被打到 0 抗性后
/// 会走 <see cref="ResistanceSystem.EnterBurning"/>（条转紫 + 常驻火焰），随后每打出一张牌由
/// <c>BurnHealPatch</c> 回 1 点抗性，回满时 <see cref="ResistanceSystem.ExitBurning"/> 退出燃烧。</para>
///
/// <para>触发过滤：只认<b>本 power 主人</b>打出的、<b>攻击</b>类型、且是<b>一串里的第一张</b>
/// （Replay/重复打出不会刷多次）。自动打出（追忆等）照常触发 —— 牌面文案就是「每打出一张攻击牌」。</para>
/// </summary>
public class TurbidBurnPower : AbstractReedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Player != Owner.Player || cardPlay.Card.Type != CardType.Attack || !cardPlay.IsFirstInSeries)
        {
            return;
        }
        // 快照：灼烧过程中会连锁改变各生物的抗性/状态（甚至引爆火花），不要边走边枚举活体集合。
        List<Creature> targets = (Owner.CombatState?.Creatures ?? []).Where(c => c.IsAlive).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        Flash();
        await ReedBurnCmd.Burn(Amount)
            .FromCreature(Owner)
            .Targeting(targets)
            .Execute(choiceContext);
    }
}
