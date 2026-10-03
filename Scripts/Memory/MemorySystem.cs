using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「回忆」的数值层：记录玩家**手动**打出的攻击/技能/能力牌，按回合数（RoundNumber）聚合。
///
/// <para>生命周期：**仅本场战斗**。数据挂在 <see cref="ICombatState"/> 上（<see cref="SpireField{TKey,TVal}"/>
/// 由 ConditionalWeakTable 承载），战斗对象一换，回忆自然全新；另在 <c>CombatSetUp</c> 里显式清一次，
/// 防止同一 CombatState 实例被复用。</para>
///
/// <para>回合桶的**安全获取**：<see cref="PlayerMemory.GetRound"/> 在桶不存在时会实时创建，
/// 因此「只有 1~5 回合数据时去取第 6 回合」不会抛异常。每回合开始（<c>CombatManager.TurnStarted</c>，
/// 玩家侧）会预创建「本回合」与「下一回合」两个桶。</para>
///
/// 用法（记录由 <c>MemoryCapturePatch</c> 自动完成，这里只是入口）：
/// <code>
///     PlayerMemory memory = MemorySystem.Of(combatState, player);
///     IReadOnlyList&lt;RememberedCard&gt; lastRound = memory.PeekRound(combatState.RoundNumber - 1);
/// </code>
/// </summary>
public static class MemorySystem
{
    /// <summary>每场战斗一份回忆总表（内部再按 <see cref="Player.NetId"/> 区分玩家）。</summary>
    private static readonly SpireField<ICombatState, MemoryStore> _store = new(() => null);

    /// <summary>已挂钩的事件源；换局后 <see cref="CombatManager.Instance"/> 会换新，需要重新挂。</summary>
    private static CombatManager? _hookedTo;

    /// <summary>最近一次拿到的战斗状态，<see cref="ResolveLocalPlayer"/> 用它兜底找本地玩家。</summary>
    private static CombatState? _lastCombatState;

    /// <summary>某玩家的回忆发生任何变化（新增 / 移除）时广播，UI 据此刷新。</summary>
    public static event Action<Player>? MemoryChanged;

    /// <summary>一条新回忆入账：(玩家, 记录)。**同步**通知，用于 UI 刷新之类的即时反应。</summary>
    public static event Action<Player, RememberedCard>? Remembered;

    /// <summary>
    /// 一条新回忆入账的**异步**扩展点：(玩家, 记录, 出牌上下文)。
    ///
    /// <para>与 <see cref="Remembered"/> 的区别是它**会被 await**：<see cref="CaptureAsync"/> 在记录落账、
    /// 播完同步通知之后，会依次 <c>await</c> 每一个订阅者，全部跑完才让 <c>Hook.AfterCardPlayed</c> 返回。
    /// 需要在「这张牌刚被记住」这一刻做异步副作用（播 VFX、跑 <c>CardCmd.*</c>、弹选择）就挂在这里——
    /// 挂在 <see cref="Remembered"/> 上会变成即发即忘，异步逻辑会在战斗状态已经往前跑之后才收尾。</para>
    ///
    /// <para>单个订阅者抛异常只会被记录成警告，不影响其余的订阅者与战斗流程。</para>
    /// </summary>
    public static event Func<Player, RememberedCard, PlayerChoiceContext, Task>? RememberedAsync;

    /// <summary>一条回忆被移除（被追忆消耗等）：(玩家, 记录)。</summary>
    public static event Action<Player, RememberedCard>? Forgotten;

    // ============ 战斗事件 / 生命周期 ============

    private static void EnsureCombatHooked()
    {
        CombatManager? cm = CombatManager.Instance;
        if (cm == null || ReferenceEquals(cm, _hookedTo))
        {
            return;
        }
        if (_hookedTo != null)
        {
            // CombatManager 不是 GodotObject，没有“已释放”状态；旧实例即便废弃，退订也无副作用。
            _hookedTo.CombatSetUp -= OnCombatSetUp;
            _hookedTo.TurnStarted -= OnTurnStarted;
        }
        cm.CombatSetUp += OnCombatSetUp;
        cm.TurnStarted += OnTurnStarted;
        _hookedTo = cm;
    }

    private static void OnCombatSetUp(CombatState state)
    {
        _lastCombatState = state;
        _store[state] = new MemoryStore(); // 仅本场战斗：开局清空
        foreach (Player player in state.Players)
        {
            // 第 1 回合（当前）与第 2 回合（下一回合）先建好，UI 一进来就有桶可看。
            Of(state, player).GetRound(1);
            Of(state, player).GetRound(2);
        }
    }

    private static void OnTurnStarted(CombatState state)
    {
        _lastCombatState = state;
        if (state.CurrentSide != CombatSide.Player)
        {
            return; // 只在玩家侧回合开始时预创建，敌方回合不占用回合数
        }
        foreach (Player player in state.Players)
        {
            PlayerMemory memory = Of(state, player);
            memory.GetRound(state.RoundNumber);        // 本回合
            memory.GetRound(state.RoundNumber + 1);    // 下一回合（提前建好，越界请求也安全）
        }
    }

    // ============ 数据访问 ============

    /// <summary>取本场战斗的回忆总表；无战斗上下文时返回 null。</summary>
    public static MemoryStore? StoreOf(ICombatState? combat)
    {
        if (combat == null)
        {
            return null;
        }
        EnsureCombatHooked();
        return _store[combat] ??= new MemoryStore();
    }

    /// <summary>取某玩家在本场战斗中的回忆（惰性创建，永不返回 null）；无战斗上下文时返回一个空壳。</summary>
    public static PlayerMemory Of(ICombatState? combat, Player player)
    {
        return StoreOf(combat)?.Of(player.NetId) ?? Empty;
    }

    /// <summary>取某玩家当前战斗中的回忆；不在战斗中时返回空壳。</summary>
    public static PlayerMemory Of(Player player)
    {
        return Of(player?.Creature?.CombatState, player!);
    }

    /// <summary>无战斗上下文时的兜底空回忆（不落盘、不共享）。</summary>
    private static PlayerMemory Empty { get; } = new();

    /// <summary>
    /// 尽力找出**本地玩家**：UI（回忆按钮 / 回忆界面）万一没拿到出牌界面传来的玩家时用它兜底。
    /// 先看缓存的战斗状态，再退回 <c>CombatManager</c>；不依赖 <c>LocalContext.GetMe(ICombatState)</c>
    /// 那条会抛异常的路径，单人局最后兜到第一个玩家。
    /// </summary>
    public static Player? ResolveLocalPlayer()
    {
        EnsureCombatHooked();
        CombatState? state = _lastCombatState ?? CombatManager.Instance?.DebugOnlyGetState();
        if (state == null)
        {
            return null;
        }
        Player? fallback = null;
        foreach (Player player in state.Players)
        {
            if (LocalContext.NetId is { } netId && player.NetId == netId)
            {
                return player;
            }
            fallback ??= player;
        }
        return fallback;
    }

    // ============ 记录（回忆） ============

    /// <summary>
    /// 捕获一次出牌：只记**手动打出**的攻击/技能/能力牌，一张牌被 Replay N 次只记一条。
    ///
    /// <para>由 <c>Hook.AfterCardPlayed</c> 状态机里注入的新状态调用并 **await**（见 <c>MemoryCapturePatch</c>），
    /// 注入点是「最后一个 <c>AfterCardPlayedLate</c> 监听者之后、<c>OnPlayWrapper</c> 把牌移出 Play 堆之前」，
    /// 因此此刻 <see cref="CardModel.CreateClone"/> 依然合法。</para>
    ///
    /// <para>流程：先**同步**落账并播同步通知（UI 立刻能刷），再依次 <c>await</c>
    /// <see cref="RememberedAsync"/> 上的订阅者，跑完才让 hook 返回——需要 await 的副作用放这里，
    /// 不会出现「战斗已经往前走、异步逻辑才收尾」的错位。追忆打出的牌走 <c>CardCmd.AutoPlay</c>
    /// （<see cref="CardPlay.IsAutoPlay"/> 为 true），天然被排除。</para>
    /// </summary>
    public static async Task CaptureAsync(ICombatState combat, PlayerChoiceContext choiceContext, CardPlay play)
    {
        Player? owner;
        RememberedCard entry;
        try
        {
            if (combat == null || play?.Card == null || play.Player == null || !play.Player.Creature.IsPlayer)
            {
                return;
            }
            if (play.IsAutoPlay)
            {
                return; // 自动打出的牌不入回忆
            }
            if (!play.IsFirstInSeries)
            {
                return; // 同一张牌被重复打出（Replay）：只记第一条
            }

            CardModel card = play.Card;
            CardType type = card.Type;
            if (type != CardType.Attack && type != CardType.Skill && type != CardType.Power)
            {
                return; // 只记攻击 / 技能 / 能力
            }
            if (!card.IsMutable)
            {
                return; // 规范卡没有“那一刻的状态”可留
            }

            // 快照：保留打出时的升级 / 附魔 / 动态变量当前值。副本不进任何牌堆，因此不显示、不收战斗 hook。
            CardModel snapshot = card.CreateClone();

            int round = play.PlayedRound(combat);
            owner = play.Player;
            entry = new RememberedCard { RoundNumber = round, Card = snapshot };
            Of(combat, owner).GetRound(round).Add(entry);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MemorySystem] 记录回忆失败：{e.Message}");
            return;
        }

        // 先播同步通知：回忆已经落账，UI 可以立刻刷新。
        Remembered?.Invoke(owner, entry);
        MemoryChanged?.Invoke(owner);

        // 再依次 await 异步订阅者。它们抛异常只记警告，不打断其余订阅者，更不能把异常抛回
        // Hook.AfterCardPlayed —— 那会直接中断整次出牌的收尾流程。
        if (RememberedAsync is { } asyncHandlers)
        {
            foreach (Func<Player, RememberedCard, PlayerChoiceContext, Task> handler in asyncHandlers.GetInvocationList())
            {
                try
                {
                    await handler(owner, entry, choiceContext);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"[MemorySystem] 回忆异步副作用失败：{e.Message}");
                }
            }
        }
    }

    /// <summary>把一条回忆从对应回合移除（追忆消耗掉它时调用）。成功返回 true。</summary>
    public static bool Forget(ICombatState? combat, Player player, RememberedCard entry)
    {
        if (combat == null || player == null || entry == null)
        {
            return false;
        }
        PlayerMemory memory = Of(combat, player);
        bool removed = memory.PeekRound(entry.RoundNumber) is List<RememberedCard> list && list.Remove(entry);
        if (removed)
        {
            Forgotten?.Invoke(player, entry);
            MemoryChanged?.Invoke(player);
        }
        return removed;
    }

    /// <summary>手动通知 UI 刷新（自定义逻辑改动了回忆内容后调用）。</summary>
    public static void NotifyChanged(Player player)
    {
        MemoryChanged?.Invoke(player);
    }
}
