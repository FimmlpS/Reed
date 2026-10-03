using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 给 <see cref="CardPlay"/> 补一个「视为在第几回合打出」的属性。
///
/// 游戏的 <see cref="CardPlay"/> 没有回合字段，且只在 <c>CardModel.OnPlayWrapper</c> 里 new 一次、无法改类，
/// 因此这里用**副作用表**（ConditionalWeakTable，随对象回收、不加字段）实现等价属性：
/// <list type="number">
///   <item>追忆方在打出前给那张副本 <see cref="MarkPlayedRound"/>（记下“视为第几回合”）；</item>
///   <item><c>Hook.BeforeCardPlayed</c> 里 <see cref="StampPendingRound"/> 把预定的回合盖到这次 CardPlay 上；</item>
///   <item>卡牌效果（<c>OnPlay</c> 内部）用 <see cref="PlayedRound"/> 读回来。</item>
/// </list>
///
/// **连锁追忆的关键**：所有「上一回合 / 下 x 回合」的基准都取自 <see cref="PlayedRound"/>，
/// 而不是 <c>CombatState.RoundNumber</c>。于是「第 3 回合打出的牌」在被追忆打出的那一刻，
/// 它的 <see cref="PlayedRound"/> 仍然是 3，它自己再去追忆「上一回合」就能拿到第 2 回合 —— 一路向上追溯。
/// </summary>
public static class MemoryPlayRound
{
    /// <summary>待打出副本的预定回合（在 <c>Hook.BeforeCardPlayed</c> 被取走并盖到 CardPlay 上）。</summary>
    private static readonly ConditionalWeakTable<CardModel, StrongBox<int>> Pending = new();

    /// <summary>已打出的 CardPlay 的回合戳。</summary>
    private static readonly ConditionalWeakTable<CardPlay, StrongBox<int>> Stamped = new();

    /// <summary>给一张「即将被打出」的卡预定它的打出回合（追忆打出前调用）。</summary>
    public static void MarkPlayedRound(this CardModel card, int round)
    {
        if (card == null)
        {
            return;
        }
        Pending.AddOrUpdate(card, new StrongBox<int>(round));
    }

    /// <summary>
    /// 读取这张卡的预定回合。**刻意不清除**：同一张牌可能被 Replay 连续打出多次，
    /// 每次 <c>BeforeCardPlayed</c> 都该盖上同一个「视为打出回合」，否则第二次起就会退回当前回合、链式追忆断掉。
    /// 追忆副本打出后即消失，表项随副本一起被回收，不需要手动清理。
    /// </summary>
    public static bool TryPeekPlayedRound(this CardModel card, out int round)
    {
        round = 0;
        if (card == null || !Pending.TryGetValue(card, out StrongBox<int>? box))
        {
            return false;
        }
        round = box.Value;
        return true;
    }

    /// <summary>在 <c>Hook.BeforeCardPlayed</c> 处调用：若这张牌带着预定回合，就盖到本次 CardPlay 上。</summary>
    public static void StampPendingRound(this CardPlay play)
    {
        if (play?.Card == null)
        {
            return;
        }
        if (play.Card.TryPeekPlayedRound(out int round))
        {
            play.StampPlayedRound(round);
        }
    }

    /// <summary>手动给一次 CardPlay 盖回合戳（一般不用，<see cref="StampPendingRound"/> 已自动处理）。</summary>
    public static void StampPlayedRound(this CardPlay play, int round)
    {
        if (play == null)
        {
            return;
        }
        Stamped.AddOrUpdate(play, new StrongBox<int>(round));
    }

    /// <summary>这次出牌是否被盖过回合戳（即：由追忆打出）。</summary>
    public static bool TryGetPlayedRound(this CardPlay play, out int round)
    {
        round = 0;
        if (play == null || !Stamped.TryGetValue(play, out StrongBox<int>? box))
        {
            return false;
        }
        round = box.Value;
        return true;
    }

    /// <summary>
    /// 这次出牌**视为**在第几回合打出：被追忆打出 → 记住的那个回合；否则 → 当前 <see cref="ICombatState.RoundNumber"/>。
    /// 拿不到战斗上下文时回退到 0。
    /// </summary>
    public static int PlayedRound(this CardPlay play, ICombatState? combat = null)
    {
        if (play != null && play.TryGetPlayedRound(out int stamped))
        {
            return stamped;
        }
        return combat?.RoundNumber ?? 0;
    }
}
