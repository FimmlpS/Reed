using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「追忆」命令：从回忆里挑一张牌，打出它的复制品（属于**自动打出**），并把那条回忆**移除**。
///
/// 流式构造，基准回合有三种给法，且可以叠加：
/// <code>
///     // 从「上一回合」的回忆里追忆一张攻击牌（自动打出）——支持连锁：
///     // cardPlay 若本身是被追忆打出的，PlayedRound 仍是它被记住的那个回合。
///     await RecallCmd.Recall(choiceContext, me)
///         .FromPlay(cardPlay)     // 基准 = 这张牌被视为打出的回合
///         .Offset(-1)             // -1 = 上 1 回合；0 = 当前回合；+x = 下 x 回合
///         .Where(c =&gt; c.Type == CardType.Attack)
///         .Execute();
///
///     // 指定绝对回合（第 3 回合）
///     await RecallCmd.Recall(choiceContext, me).FromRound(3).Execute();
///
///     // 当前回合
///     await RecallCmd.Recall(choiceContext, me).Offset(0).Execute();
/// </code>
///
/// 打出细节：
/// <list type="bullet">
///   <item>先 <b>移除</b>该条回忆，再打出，避免打出过程中被自己再次追忆到；</item>
///   <item>用 <see cref="CardModel.CreateDupe"/> 造复制品 —— 复制品（IsDupe）打出后结果堆为
///     <see cref="PileType.None"/>，即<b>直接消失</b>，不进入抽牌/弃牌/消耗/手牌任何牌堆；</item>
///   <item>打出前给复制品 <see cref="MemoryPlayRound.MarkPlayedRound"/> 预盖回合，于是它「视为在第 N 回合打出」，
///     它自己若也带追忆效果，就能继续向 N-1 追溯（连锁）；</item>
///   <item>走 <see cref="CardCmd.AutoPlay"/>，因此 <see cref="CardPlay.IsAutoPlay"/> 为 true，不会被回忆再次记录。</item>
/// </list>
/// </summary>
public sealed class RecallCmd
{
    private readonly PlayerChoiceContext? _choiceContext;
    private readonly Player _player;

    private CardPlay? _fromPlay;
    private int? _absoluteRound;
    private int _offset;
    private Func<CardModel, bool>? _filter;
    private LocString? _prompt;
    private Creature? _target;
    private bool _keepInMemory;

    private RecallCmd(PlayerChoiceContext? choiceContext, Player player)
    {
        _choiceContext = choiceContext;
        _player = player;
    }

    /// <summary>追忆选择屏的默认标题（key 见 Reed/localization/zhs/static_hover_tips.json）。</summary>
    private static LocString DefaultPrompt => new LocString("static_hover_tips", "REED-RECALL.select.title");

    /// <summary>开始构造一次追忆。</summary>
    public static RecallCmd Recall(PlayerChoiceContext? choiceContext, Player player) => new(choiceContext, player);

    // ============ 基准回合 ============

    /// <summary>
    /// 基准回合 = 这次出牌「视为打出」的回合（见 <see cref="MemoryPlayRound.PlayedRound"/>）。
    /// 传当前正在执行的 cardPlay，就能正确地「上一回合」；被追忆打出的牌也能链式向上追溯。
    /// </summary>
    public RecallCmd FromPlay(CardPlay? play)
    {
        _fromPlay = play;
        return this;
    }

    /// <summary>基准回合 = 指定的绝对回合数（第 1 回合 = 1）。</summary>
    public RecallCmd FromRound(int round)
    {
        _absoluteRound = round;
        return this;
    }

    /// <summary>在基准回合上偏移：<c>-1</c> 上 1 回合 / <c>0</c> 当前回合 / <c>+x</c> 下 x 回合。</summary>
    public RecallCmd Offset(int offset)
    {
        _offset = offset;
        return this;
    }

    // ============ 候选筛选 / 表现 ============

    /// <summary>进一步筛选候选（在回合过滤之后应用）。</summary>
    public RecallCmd Where(Func<CardModel, bool> predicate)
    {
        _filter = predicate;
        return this;
    }

    /// <summary>只追忆指定类型的牌（Attack / Skill / Power）。</summary>
    public RecallCmd OfType(CardType type) => Where(c => c.Type == type);

    /// <summary>自定义选择屏标题。</summary>
    public RecallCmd WithPrompt(LocString prompt)
    {
        _prompt = prompt;
        return this;
    }

    /// <summary>指定打出目标（不指定时由 <see cref="CardCmd.AutoPlay"/> 自行随机）。</summary>
    public RecallCmd Targeting(Creature? target)
    {
        _target = target;
        return this;
    }

    /// <summary>追忆后**保留**这条回忆（不消耗）。默认是消耗掉。</summary>
    public RecallCmd KeepInMemory()
    {
        _keepInMemory = true;
        return this;
    }

    // ============ 执行 ============

    /// <summary>执行追忆。返回实际打出的那张复制品；无可选 / 无法打出时返回 null。</summary>
    public async Task<CardModel?> Execute(PlayerChoiceContext? choiceContext = null)
    {
        PlayerChoiceContext? context = choiceContext ?? _choiceContext;
        ICombatState? combat = _player?.Creature?.CombatState;
        if (context == null || combat == null || _player == null)
        {
            return null;
        }

        // 回合解析：绝对回合优先，否则以「这次出牌视为打出的回合」为基准；最后统一叠加偏移。
        int baseRound = _absoluteRound ?? (_fromPlay != null ? _fromPlay.PlayedRound(combat) : combat.RoundNumber);
        int round = baseRound + _offset;
        if (round < 1)
        {
            return null; // 越界（比如第 1 回合再往上追忆）
        }

        PlayerMemory memory = MemorySystem.Of(combat, _player);

        // 用 PeekRound（不创建）取候选：该回合没有任何回忆时直接 no-op。
        List<RememberedCard> entries = memory.PeekRound(round)
            .Where(e => e.Card != null && (_filter == null || _filter(e.Card)))
            .ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        List<CardModel> cards = entries.Select(e => e.Card).ToList();

        // 只选 1 张；候选恰好 1 张时 FromSimpleGrid 会直接选中、免弹屏。
        CardSelectorPrefs prefs = new CardSelectorPrefs(_prompt ?? DefaultPrompt, 1);
        IEnumerable<CardModel> picked = await CardSelectCmd.FromSimpleGrid(context, cards, _player, prefs);
        CardModel? selected = picked.FirstOrDefault();
        if (selected == null)
        {
            return null; // 玩家取消
        }
        int index = IndexOfReference(cards, selected);
        if (index < 0)
        {
            return null;
        }
        RememberedCard entry = entries[index];

        // 先移除回忆，再打出：防止打出过程中的其它追忆又选中同一条。
        if (!_keepInMemory)
        {
            MemorySystem.Forget(combat, _player, entry);
        }

        return await Play(combat, entry, round, context);
    }

    /// <summary>把一条回忆打成「直接消失」的自动出牌，并预盖它视为被打出的回合。</summary>
    private async Task<CardModel?> Play(ICombatState combat, RememberedCard entry, int round, PlayerChoiceContext context)
    {
        CardModel? copy = MakePlayableCopy(combat, entry.Card);
        if (copy == null)
        {
            return null;
        }

        // 预盖回合：Hook.BeforeCardPlayed 会把它盖到这次 CardPlay 上 → 卡牌自己的「上一回合」以它为基准。
        copy.MarkPlayedRound(round);

        Creature? target = _target;
        await CardCmd.AutoPlay(context, copy, target, AutoPlayType.Default);
        return copy;
    }

    /// <summary>
    /// 造一张「打出后直接消失」的可玩副本：<see cref="CardModel.CreateDupe"/> 会 clone 并把 IsDupe 置位，
    /// 而 <c>GetResultLocationForCardPlay</c> 对 IsDupe 返回 <see cref="PileType.None"/> → 打出后
    /// <c>CardPileCmd.RemoveFromCombat</c>，不进任何牌堆。取不到时退回规范卡实例化。
    /// </summary>
    private CardModel? MakePlayableCopy(ICombatState combat, CardModel source)
    {
        try
        {
            if (source.IsMutable)
            {
                return source.CreateDupe(_player);
            }
            return combat.CreateCard(source, _player);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecallCmd] 无法复制回忆牌 {source?.Id}：{e.Message}");
            return null;
        }
    }

    private static int IndexOfReference(List<CardModel> cards, CardModel target)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], target))
            {
                return i;
            }
        }
        return -1;
    }
}
