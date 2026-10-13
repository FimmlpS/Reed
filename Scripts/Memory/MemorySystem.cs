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

    // ============ 快捷查询 ============

    /// <summary>
    /// 【最常用的一行】这张牌**视为打出**的那一回合里，其主人回忆了多少张牌。
    ///
    /// <para>「回忆中当前回合每有一张牌，就……」这类卡（如
    /// <see cref="Reed.Scripts.Cards.Skill.WithYou"/>）写算式乘数时用它就是一行：</para>
    /// <code>
    ///     new CalculatedBlockFromExtraVar(ValueProp.Move)
    ///         .WithMultiplier(static (card, _) =&gt; MemorySystem.CountInPlayedRound(card));
    /// </code>
    ///
    /// <para>回合基准取 <see cref="MemoryPlayRound.PlayedRound(CardModel, ICombatState)"/>：
    /// 普通打出 = 场上当前回合；被追忆打出 = **它被记住的那个回合**（链式追忆靠这个对齐）。
    /// 只 <c>Peek</c> 不创建（桶不存在按 0 张算），拿不到战斗上下文（例如在卡库/奖励里看牌）时返回 0。</para>
    ///
    /// <para>在 <c>OnPlay</c> 里拿到 <c>cardPlay</c> 时，等价写法也可以直接用
    /// <c>Of(cardPlay.Player).PeekRound(cardPlay.PlayedRound(combat)).Count</c> —— 两者同一个回合数。</para>
    /// </summary>
    public static int CountInPlayedRound(CardModel? card)
    {
        if (card == null)
        {
            return 0;
        }
        Player? owner = card.Owner; // 规范卡（卡库/奖励里浏览时）可能还没有主人
        ICombatState? combat = card.CombatState ?? owner?.Creature?.CombatState;
        return combat == null || owner == null ? 0 : Of(combat, owner).PeekRound(card.PlayedRound(combat)).Count;
    }

    /// <summary>
    /// 这张牌**此刻是否躺在玩家的回忆里**；是的话顺带给出它所在的那个回合数。
    ///
    /// <para>「如果此牌在回忆中，就……」这类条件的**自证入口**：<see cref="DispatchAsync"/> 的广播会同时送到
    /// 牌堆里的那张牌（<c>IterateHookListeners</c>）与回忆里的那份快照，想只认「在回忆中」的那一份就用它挡一下。</para>
    ///
    /// <para>按**引用相等**判断（<see cref="PlayerMemory.RoundOf"/>）：回忆里存的是快照副本，
    /// 手牌 / 抽牌堆里的同名牌与它不是同一个对象，因此拿手牌去问会返回 false —— 正是想要的语义。</para>
    /// </summary>
    public static bool IsInMemory(Player? player, CardModel? card, out int round)
    {
        round = 0;
        ICombatState? combat = player?.Creature?.CombatState ?? card?.CombatState;
        if (player == null || card == null || combat == null)
        {
            return false;
        }
        int? found = Of(combat, player).RoundOf(card);
        if (found == null)
        {
            return false;
        }
        round = found.Value;
        return true;
    }

    // ============ 记录（回忆） ============

    /// <summary>
    /// 造一条回忆记录，并**顺手给快照盖上它所在回合的回合戳**
    /// （<see cref="MemoryPlayRound.MarkPlayedRound"/>，两条记录途径都走这里）。
    ///
    /// <para>于是回忆里的牌自己就知道「我是第几回合的」：<c>AfterRecalled</c> 收到的 <c>card</c> 正是这张快照，
    /// 而那一刻它那条记录**已经被消耗掉**了（<see cref="RecallCmd"/> 先移除再打出），查回忆查不到 ——
    /// 有了这个戳就能直接 <c>card.PlayedRound(combat)</c> 读出它来自哪一回合
    /// （心焯的「当前回合的其他牌」就是靠它成立的），不用给接口再加一个回合参数。</para>
    ///
    /// <para>与追忆方盖的戳天然一致：<see cref="RecallCmd"/> 给复制品盖的正是这条记录的
    /// <see cref="RememberedCard.RoundNumber"/>，所以「回忆里的牌」和「它被追忆打出的复制品」
    /// 报出的是同一个回合数。</para>
    /// </summary>
    private static RememberedCard MakeEntry(CardModel snapshot, int round)
    {
        snapshot.MarkPlayedRound(round);
        return new RememberedCard { RoundNumber = round, Card = snapshot };
    }

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
            entry = MakeEntry(snapshot, round);
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

        // 最后广播「有牌进回忆」给 IMemoryEventSubscriber（power 的结算层，如博物）。
        await NotifyRememberedAsync(choiceContext, owner, entry.Card);
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

    // ============ 手动记录（RecordCmd / 卡牌效果） ============

    /// <summary>
    /// 【手动记录】把一张牌的**副本**写进**指定回合**的回忆；成功返回这条新记录，写不进去时返回 null。
    ///
    /// <list type="bullet">
    ///   <item>与 <see cref="CaptureAsync"/> 的关系：那条路只认「玩家手动打出的牌」，
    ///     这里不认识出牌事件，目标回合由调用方给定 —— 于是可以记到**还没到的回合**（未来的桶实时创建）；</item>
    ///   <item>入账后照常广播同步的 <see cref="Remembered"/> / <see cref="MemoryChanged"/>，
    ///     所以回忆按钮计数、飞卡特效（<c>MemoryFlyVfx</c>）都会像自动记录那样响应。
    ///     （<see cref="RememberedAsync"/> 不在此列：它是「出牌流程内会被 await」的扩展点，手动记录并不在出牌流程里。）</item>
    ///   <item>写进去的永远是 <see cref="Snapshot"/> 出来的副本，原牌不受影响。</item>
    /// </list>
    /// </summary>
    public static RememberedCard? Record(ICombatState? combat, Player? player, int round, CardModel? card)
    {
        if (combat == null || player == null || card == null || round < 1 || !card.CanRecord())
        {
            return null;
        }

        RememberedCard entry;
        try
        {
            CardModel snapshot = card.IsMutable ? card.CreateClone() : combat.CreateCard(card, player);
            entry = MakeEntry(snapshot, round);
            Of(combat, player).GetRound(round).Add(entry);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MemorySystem] 记录回忆失败（{card.Id} → 第 {round} 回合）：{e.Message}");
            return null;
        }

        Remembered?.Invoke(player, entry);
        MemoryChanged?.Invoke(player);
        return entry;
    }

    /// <summary>手动通知 UI 刷新（自定义逻辑改动了回忆内容后调用）。</summary>
    public static void NotifyChanged(Player player)
    {
        MemoryChanged?.Invoke(player);
    }

    /// <summary>
    /// 【手动记录 + 事件】<see cref="Record"/> 的异步版：写进回忆之后再 await 一遍
    /// <see cref="IMemoryEventSubscriber.AfterRemembered"/>。
    ///
    /// <para>记录**发生在出牌流程里**（卡牌效果 / power 效果）时用它，power 才能在「这张牌刚被记下」的
    /// 那一刻结算（如博物抽牌）。拿不到 <see cref="PlayerChoiceContext"/>（比如在战斗外补记）就用同步的
    /// <see cref="Record"/> —— 那种情形下没有可 await 的流程，订阅者也就无从触发。</para>
    /// </summary>
    public static async Task<RememberedCard?> RecordAsync(
        PlayerChoiceContext? choiceContext,
        ICombatState? combat,
        Player? player,
        int round,
        CardModel? card)
    {
        RememberedCard? entry = Record(combat, player, round, card);
        if (entry != null && player != null)
        {
            await NotifyRememberedAsync(choiceContext, player, entry.Card);
        }
        return entry;
    }

    // ============ 事件分发：牌进出回忆（IMemoryEventSubscriber） ============

    /// <summary>
    /// 广播「一张牌被写进回忆」。由 <see cref="CaptureAsync"/>（手动打出的自动记录）与
    /// <see cref="RecordAsync"/>（效果记录）调用 —— 也就是 <see cref="Record"/> 的同步路径**不会**触发它。
    /// </summary>
    public static Task NotifyRememberedAsync(PlayerChoiceContext? choiceContext, Player? player, CardModel? card)
        => DispatchAsync(recalled: false, choiceContext, player, card);

    /// <summary>
    /// 广播「一张牌被追忆打出」。由 <see cref="RecallCmd"/> 每打完一张复制品时调用 ——
    /// 与 <c>CardPlay.IsFromRecall</c> 覆盖的范围完全一致（含连锁追忆与 <c>Card()</c> 的递归支点）。
    /// </summary>
    public static Task NotifyRecalledAsync(PlayerChoiceContext? choiceContext, Player? player, CardModel? card)
        => DispatchAsync(recalled: true, choiceContext, player, card);

    /// <summary>
    /// 遍历**本次事件要通知的所有模型**（见 <see cref="CollectSubscribers"/>），挨个 await 实现了
    /// <see cref="IMemoryEventSubscriber"/> 的那个方法。
    ///
    /// <list type="bullet">
    ///   <item>**不注册**：和 <c>IFireFlowerSubscriber</c> 一样每次现查 —— power 被移除后自然就遍历不到了，
    ///     不用管订阅 / 退订的生命周期；</item>
    ///   <item>单个订阅者抛异常只记警告，不影响其余的订阅者，更不能把异常抛回出牌流程；</item>
    ///   <item>拿不到上下文（不在战斗里 / 没有 <see cref="PlayerChoiceContext"/>）时什么都不做。</item>
    /// </list>
    /// </summary>
    private static async Task DispatchAsync(bool recalled, PlayerChoiceContext? choiceContext, Player? player, CardModel? card)
    {
        if (choiceContext == null || player == null || card == null)
        {
            return;
        }
        foreach (AbstractModel model in CollectSubscribers(player))
        {
            if (model is not IMemoryEventSubscriber subscriber)
            {
                continue;
            }
            try
            {
                if (recalled)
                {
                    await subscriber.AfterRecalled(choiceContext, player, card);
                }
                else
                {
                    await subscriber.AfterRemembered(choiceContext, player, card);
                }
            }
            catch (Exception e)
            {
                GD.PushWarning($"[MemorySystem] 回忆事件订阅者失败（{model.Id}）：{e.Message}");
            }
        }
    }

    /// <summary>
    /// 收集本次事件要通知的所有模型 = **战斗里所有 hook 监听者** + **回忆里保存的每一张牌**。
    ///
    /// <para>为什么必须补上后一份：回忆里的牌是**快照副本，不进任何牌堆**，
    /// 因此 <c>ICombatState.IterateHookListeners()</c> 永远看不到它们（那个方法只扫牌堆、power、遗物……）。
    /// 「躺」在回忆里的牌要能订阅回忆事件（心焯的后半段就是「此牌在回忆中时才生效」），
    /// 只能在这里显式展开。</para>
    ///
    /// <para><b>一张牌连它的痛苦与附魔一起算</b> —— 两边都照 <c>IterateHookListeners</c> 的规矩来：
    /// 那里对牌堆里的每张牌也是「牌 → <c>Affliction</c> → <c>Enchantment</c>」三件套，
    /// 所以回忆那一份同样展开，附魔 / 痛苦模型（<c>EnchantmentModel</c> / <c>AfflictionModel</c>）也是合法的订阅者。</para>
    ///
    /// <para>按**引用**去重，同一个模型只通知一次。注意「同一张牌的多个副本」不算重复：
    /// 手牌里的那张与回忆里的快照是两个对象，会各收到一次广播 —— 只在「此牌在回忆中」时生效的效果
    /// 用 <see cref="IsInMemory"/> 自证即可。</para>
    /// </summary>
    private static List<AbstractModel> CollectSubscribers(Player player)
    {
        List<AbstractModel> subscribers = new();
        HashSet<AbstractModel> added = new(ReferenceEqualityComparer.Instance);

        foreach (AbstractModel model in player.Creature?.CombatState?.IterateHookListeners() ?? [])
        {
            if (added.Add(model))
            {
                subscribers.Add(model);
            }
        }
        foreach (AbstractModel model in CollectMemoryModels(player))
        {
            if (added.Add(model))
            {
                subscribers.Add(model);
            }
        }
        return subscribers;
    }

    /// <summary>
    /// 某玩家回忆里保存的每一张牌，连同它的**痛苦**（<c>Affliction</c>）与**附魔**（<c>Enchantment</c>）
    /// —— 与 <c>IterateHookListeners</c> 对牌堆里的牌做的事情一模一样（那里也是 牌 → Affliction → Enchantment）。
    ///
    /// <para>列表**先攒满再返回**，不返回惰性枚举：通知过程中回忆条目会被追忆消耗掉
    /// （<see cref="RecallCmd"/> 是「先移除、再打出」，而广播正好发在打出之后），
    /// 边遍历边改集合会直接抛异常。同理 <see cref="DispatchAsync"/> 也是先取齐列表再逐个 await。</para>
    /// </summary>
    private static List<AbstractModel> CollectMemoryModels(Player player)
    {
        List<AbstractModel> models = new();
        ICombatState? combat = player.Creature?.CombatState;
        if (combat == null)
        {
            return models;
        }

        PlayerMemory memory = Of(combat, player);
        foreach (int round in memory.Rounds)
        {
            foreach (RememberedCard entry in memory.PeekRound(round))
            {
                CardModel? card = entry.Card;
                if (card == null)
                {
                    continue;
                }
                models.Add(card);
                if (card.Affliction != null)
                {
                    models.Add(card.Affliction);
                }
                if (card.Enchantment != null)
                {
                    models.Add(card.Enchantment);
                }
            }
        }
        return models;
    }
}
