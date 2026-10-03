using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「能发起追忆的卡牌」实现的接口：把「这张牌可以追忆哪些牌」的预览逻辑告诉 UI。
///
/// <para>卡牌在<see cref="CardModel"/>上实现它即可（<c>CardModel</c> 是 class，mod 的卡都能 implements）：</para>
/// <code>
///     public sealed class RecallLastTurnCard : CardModel, IMemorySubscriber
///     {
///         public IReadOnlyList&lt;CardModel&gt; GetRecallPreview(MemoryPreviewContext ctx)
///             =&gt; ctx.Latest(-1, c =&gt; c.Type == CardType.Attack);   // 上一回合最新的 5 张攻击牌
///     }
/// </code>
///
/// <para>鼠标悬浮这张牌时，<c>MemoryPreviewOverlay</c> 会调用本方法，并把返回的牌在**回忆牌堆图标**
/// 处按扇形环绕展开（从正上方起顺时针、卡底抵住图标、小比例）。返回空列表 = 不显示预览。本方法
/// 必须是**同步、无副作用**的（只读回忆，别在这里改战斗状态）。</para>
/// </summary>
public interface IMemorySubscriber
{
    /// <summary>
    /// 悬浮这张牌时要在图标处预览的候选牌（按展示顺序，从「最先被追忆到的」开始）。
    /// 一般直接用 <see cref="MemoryPreviewContext.Latest"/> / <see cref="MemoryPreviewContext.LatestAt"/> 取。
    /// </summary>
    IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context);
}

/// <summary>
/// 一次「追忆预览」的上下文：把取候选牌需要的所有东西（回忆记录、真实当前回合、这张牌的基准回合）
/// 一次交给卡牌自己的预览逻辑。
///
/// <list type="bullet">
///   <item><see cref="CurrentRound"/> **永远是战斗的真实当前回合**（<c>CombatState.RoundNumber</c>）；</item>
///   <item><see cref="BaseRound"/> 是「悬浮的这张牌以哪个回合为基准」：它本身来自回忆时 = 它所在的那个
///     回忆回合，否则 = 当前回合。于是「追忆上一回合」在回忆界面里悬浮时会取「它所在回合的上一回合」，
///     连锁追忆（追忆出的牌自己再上一回合）语义自然成立；</item>
///   <item>回忆**只含本地玩家（LocalPlayer）**那一份，与回忆界面完全一致。</item>
/// </list>
/// </summary>
public sealed class MemoryPreviewContext
{
    private MemoryPreviewContext(
        CardModel card,
        Player? player,
        PlayerMemory? memory,
        int currentRound,
        int baseRound,
        bool fromMemory)
    {
        Card = card;
        Player = player;
        Memory = memory;
        CurrentRound = currentRound;
        BaseRound = baseRound;
        FromMemory = fromMemory;
    }

    /// <summary>正在被悬浮的那张牌本身（实现了 <see cref="IMemorySubscriber"/> 的那张）。</summary>
    public CardModel Card { get; }

    /// <summary>本地玩家（拿不到时 null）。</summary>
    public Player? Player { get; }

    /// <summary>本地玩家本场战斗的回忆（拿不到时 null）。</summary>
    public PlayerMemory? Memory { get; }

    /// <summary>战斗的真实当前回合数（没有战斗上下文时为 0）。</summary>
    public int CurrentRound { get; }

    /// <summary>
    /// 这张牌的**基准回合**：它来自回忆 → 它所在的那个回忆回合；否则 → <see cref="CurrentRound"/>。
    /// 「上一回合 / 下 x 回合」都以它为基准（追忆连锁就靠这个）。
    /// </summary>
    public int BaseRound { get; }

    /// <summary>被悬浮的这张牌是否来自回忆（此时 <see cref="BaseRound"/> 才是「它所在回合」）。</summary>
    public bool FromMemory { get; }

    /// <summary>候选牌总数为 0 / 拿不到回忆时为 false（卡牌可以据此提前返回空）。</summary>
    public bool HasMemory => Memory != null && Player != null;

    // ============ 便捷取牌 ============

    /// <summary>基准回合 + <paramref name="offset"/> 的回合数（-1 = 上一回合，0 = 基准回合，+x = 之后 x 回合）。</summary>
    public int RoundAt(int offset) => BaseRound + offset;

    /// <summary>
    /// 【便捷过滤器】取「基准回合 + <paramref name="offset"/>」这一回合里**最新被添加**的
    /// 至多 <paramref name="count"/> 张牌（默认 5 张），再可选叠一层卡牌自身的筛选。
    /// </summary>
    public List<CardModel> Latest(int offset, Func<CardModel, bool>? filter = null, int count = 5)
        => LatestAt(RoundAt(offset), filter, count);

    /// <summary>【便捷过滤器】取指定**绝对回合**里最新被添加的至多 <paramref name="count"/> 张牌。</summary>
    public List<CardModel> LatestAt(int round, Func<CardModel, bool>? filter = null, int count = 5)
        => Memory?.LatestCards(round, filter, count) ?? new List<CardModel>();

    /// <summary>为某张悬浮的牌建上下文；牌/战斗上下文缺失时返回 null。</summary>
    public static MemoryPreviewContext? FromCard(CardModel? card)
    {
        if (card == null)
        {
            return null;
        }
        // 回忆只展示本地玩家那一份（回忆界面、回忆按钮计数都是这个口径）。
        Player? player = MemorySystem.ResolveLocalPlayer();
        PlayerMemory? memory = player == null ? null : MemorySystem.Of(player);
        int currentRound = player?.Creature?.CombatState?.RoundNumber ?? 0;
        int? memoryRound = memory?.RoundOf(card);
        return new MemoryPreviewContext(
            card,
            player,
            memory,
            currentRound,
            memoryRound ?? currentRound,
            memoryRound.HasValue);
    }
}
