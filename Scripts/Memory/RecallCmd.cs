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
///
///     // 【指定卡牌】不查回忆、不弹选择屏：把「此牌」视为上一回合追忆出来（递归追忆的支点）
///     await RecallCmd.Recall(choiceContext, me).FromPlay(cardPlay).Offset(-1).Card(this).Execute();
/// </code>
///
/// 打出细节：
/// <list type="bullet">
///   <item>先 <b>移除</b>该条回忆，再打出，避免打出过程中被自己再次追忆到；</item>
///   <item>用 <see cref="CardModel.CreateDupe"/> 造复制品 —— 复制品（IsDupe）打出后结果堆为
///     <see cref="PileType.None"/>，即<b>直接消失</b>，不进入抽牌/弃牌/消耗/手牌任何牌堆；</item>
///   <item>打出前给复制品 <see cref="MemoryPlayRound.MarkPlayedRound"/> 预盖回合，于是它「视为在第 N 回合打出」，
///     它自己若也带追忆效果，就能继续向 N-1 追溯（连锁）；</item>
///   <item>走 <see cref="CardCmd.AutoPlay"/>，因此 <see cref="CardPlay.IsAutoPlay"/> 为 true，不会被回忆再次记录；</item>
///   <item>换成 <see cref="Card"/>（指定牌）时跳过前两条：不查回忆、不消耗回忆，只造副本 + 盖回合戳
///     —— 于是可以「追忆」一张回忆里根本没有的牌，用它驱动递归（复制品的 <c>OnPlay</c> 里再调一次）。</item>
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
    private CardModel? _template;

    private RecallCmd(PlayerChoiceContext? choiceContext, Player player)
    {
        _choiceContext = choiceContext;
        _player = player;
    }

    /// <summary>追忆选择屏的默认标题（key 见 Reed/localization/zhs/static_hover_tips.json）。</summary>
    private static LocString DefaultPrompt => new LocString("static_hover_tips", "REED-RECALL.select.title");

    /// <summary>
    /// 选择屏标题：默认带上「正在追忆第几回合」（玩家在选之前就知道这一屏给的是哪一回合的回忆）。
    /// 带回合数的文案 key 若还没进 pck（没重导），就退回老的、不带回合数的 key —— 宁可少一句话，
    /// 也不能让缺文案把整次追忆搞崩。
    /// </summary>
    private LocString PromptFor(int round)
    {
        if (_prompt != null)
        {
            return _prompt; // 卡牌自己指定了标题：尊重它
        }

        LocString? prompt = null;
        try
        {
            prompt = LocString.GetIfExists("static_hover_tips", "REED-RECALL.select.round.title");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecallCmd] 读取追忆标题失败：{e.Message}");
        }
        if (prompt == null)
        {
            return DefaultPrompt;
        }
        prompt.Add("Round", round);
        return prompt;
    }

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

    /// <summary>
    /// 【指定卡牌】不弹选牌屏，直接把这张牌当作「第 <c>baseRound + offset</c> 回合的回忆」追忆出来
    /// —— 与 <see cref="RecordCmd.Card"/> 对称：那边指定往回忆里**写**哪张牌，这边指定从回忆里**取**哪张牌。
    ///
    /// <para><b>「视为」的含义</b>：目标回合的回忆里**不要求**真有这张牌（真有也不会被消耗）——
    /// 打出的是一张现场造的可玩副本（dupe，打出后直接消失，不弹选择屏、不查回忆），
    /// 并盖上「视为在第 <c>baseRound + offset</c> 回合打出」的回合戳（见
    /// <see cref="MemoryPlayRound.MarkPlayedRound"/>）。所以它自己若也带追忆效果，
    /// 就会以那个回合为基准继续往前追 —— 这正是**递归追忆**的支点：</para>
    /// <code>
    ///     // EmperorTravel：追忆完攻击牌后，把「此牌」视为上一回合再追忆一次。
    ///     // 复制品里再走一遍同样的逻辑 → 回合数逐层 -1，直到没有牌可追忆（回合 &lt; 1）自然停下。
    ///     await RecallCmd.Recall(choiceContext, Owner)
    ///         .FromPlay(cardPlay)
    ///         .Offset(-1)
    ///         .Card(this)
    ///         .Execute(choiceContext);
    /// </code>
    /// </summary>
    public RecallCmd Card(CardModel template)
    {
        _template = template;
        return this;
    }

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

        // 【指定卡牌】不查回忆、不弹选择屏：直接造一份副本打成「视为第 round 回合打出」。
        // 递归追忆的终止条件就藏在上面那句 round < 1 里。
        if (_template != null)
        {
            return await Play(combat, _template, round, context);
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
        CardSelectorPrefs prefs = new CardSelectorPrefs(PromptFor(round), 1);
        // 与 FromSimpleGrid 内部「是否真的要弹选择屏」同一条判断 —— 只有真弹屏期间才算「正在追忆」，
        // 于是回忆界面（此间可打开）能把这一回合标成蓝色。
        bool showsSelector = prefs.RequireManualConfirmation || cards.Count > prefs.MinSelect;
        if (showsSelector)
        {
            RecallFocus.Begin(round, _player);
        }

        IEnumerable<CardModel> picked;
        try
        {
            picked = await CardSelectCmd.FromSimpleGrid(context, cards, _player, prefs);
        }
        finally
        {
            // 选中 / 取消 / 抛异常都要收回标记，否则回忆界面上会一直留着一根蓝色刻度。
            if (showsSelector)
            {
                RecallFocus.End();
            }
        }

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

        return await Play(combat, entry.Card, round, context);
    }

    // ============ 整回合追忆 ============

    /// <summary>
    /// 把第 <paramref name="round"/> 回合回忆里的**所有**牌按**打出顺序**依次追忆（自动打出复制品），
    /// 每追忆一条就从回忆里移除它。返回实际打出的张数。
    ///
    /// <list type="bullet">
    ///   <item><b>先快照</b>：每追忆一条都会改动那个回合的列表（<see cref="MemorySystem.Forget"/>），
    ///     所以先把候选拷进新列表，绝不边枚举边改活体集合；</item>
    ///   <item><b>逐条重新确认</b>：被追忆打出的牌自己可能也带追忆效果（链式），会把**同一回合里排在后面的**
    ///     回忆提前吃掉 —— 于是每条打出前都以 <see cref="MemorySystem.Forget"/> 的返回值为闸门：
    ///     只有「确实由这一趟移除成功」的那条才打，已经被连锁吃掉的直接跳过（而不是重复打一遍）；</item>
    ///   <item>复用 <see cref="Play"/> 的既有路径：复制品（<c>IsDupe</c>）打出后结果堆为
    ///     <see cref="PileType.None"/> → **直接消失**，并盖上「视为第 <paramref name="round"/> 回合打出」的戳，
    ///     于是连锁追忆能继续往前追溯；走 <c>AutoPlay</c> 因此也不会被回忆再次记录。</item>
    /// </list>
    /// </summary>
    public static async Task<int> RecallAll(
        PlayerChoiceContext? choiceContext,
        Player player,
        int round,
        Func<CardModel, bool>? filter = null)
    {
        if (choiceContext == null || player == null || round < 1)
        {
            return 0;
        }
        ICombatState? combat = player.Creature?.CombatState;
        if (combat == null)
        {
            return 0;
        }

        // 快照：PeekRound 只读不建桶；ToList 之后列表再被改也不影响这一趟的遍历。
        List<RememberedCard> snapshot = MemorySystem.Of(combat, player)
            .PeekRound(round)
            .Where(e => e.Card != null && (filter == null || filter(e.Card)))
            .ToList();
        if (snapshot.Count == 0)
        {
            return 0;
        }

        RecallCmd cmd = new(choiceContext, player);
        int played = 0;
        foreach (RememberedCard entry in snapshot)
        {
            // 闸门：Forget 返回 false = 这条回忆已经不在了（被本趟里更早那张的连锁追忆提前消耗），跳过。
            if (!MemorySystem.Forget(combat, player, entry))
            {
                continue;
            }
            if (await cmd.Play(combat, entry.Card, round, choiceContext) != null)
            {
                played++;
            }
        }
        return played;
    }

    /// <summary>
    /// 把一张牌（回忆里的那张，或 <see cref="Card"/> 指定的那张）打成「直接消失」的自动出牌，
    /// 并预盖它视为被打出的回合。
    /// </summary>
    private async Task<CardModel?> Play(ICombatState combat, CardModel source, int round, PlayerChoiceContext context)
    {
        CardModel? copy = MakePlayableCopy(combat, source);
        if (copy == null)
        {
            return null;
        }

        // 预盖回合：Hook.BeforeCardPlayed 会把它盖到这次 CardPlay 上 → 卡牌自己的「上一回合」以它为基准。
        copy.MarkPlayedRound(round);
        // 同时标记「这一趟是追忆打出的」：只认追忆（而不是全体自动打出）的效果据此判断。
        copy.MarkFromRecall();

        Creature? target = _target;
        await CardCmd.AutoPlay(context, copy, target, AutoPlayType.Default);

        // 广播「这张牌是被追忆打出来的」—— power 的结算层据此计数（博物）。
        // 放在打出**之后**：这张牌自己的效果先结算完（含它引发的连锁追忆），再回头记这一笔。
        await MemorySystem.NotifyRecalledAsync(context, _player, source);
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
