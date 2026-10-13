using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「记录」命令：把一张牌（的副本）**写进指定回合的回忆**——<see cref="RecallCmd"/> 的反向操作
/// （追忆是取出来并消耗，记录是放进去）。
///
/// 流式构造，基准回合与 <see cref="RecallCmd"/> 完全同一套，且可以叠加：
/// <code>
///     // 让玩家从手牌里挑一张，记进「上一回合」的回忆（打出的这张牌视为第 N 回合，所以 -1 是它之前那回合）
///     await RecordCmd.Record(choiceContext, me)
///         .FromPlay(cardPlay)     // 基准 = 这张牌被视为打出的回合
///         .Offset(-1)             // -1 = 上 1 回合；0 = 当前回合；+x = 下 x 回合
///         .Where(c =&gt; c.Type == CardType.Skill)
///         .Execute();
///
///     // 指定绝对回合（第 3 回合）
///     await RecordCmd.Record(choiceContext, me).FromRound(3).Execute();
///
///     // 不从手牌选，直接把 2 张「防御+」记进下一回合（远眺 Overlook 用的就是这条路径）
///     await RecordCmd.Record(choiceContext, me)
///         .FromPlay(cardPlay)
///         .Offset(1)
///         .Card(ModelDb.Card&lt;Defend&gt;())
///         .Upgraded()
///         .Count(2)
///         .Execute();
/// </code>
///
/// 记录细节：
/// <list type="bullet">
///   <item>与自动捕获（<see cref="MemorySystem.CaptureAsync"/>）共用同一条落账路径
///     （<see cref="MemorySystem.Record"/>），因此 <c>Remembered</c> / <c>MemoryChanged</c> 照常广播 ——
///     回忆按钮的计数、飞卡特效都会照常响应；每条记录还额外 await 一遍
///     <see cref="MemorySystem.RecordAsync"/> 里的 <see cref="IMemoryEventSubscriber"/>（博物那类 power 据此计数）；</item>
///   <item>写进去的永远是**副本**：手牌 / 场上的牌用 <see cref="CardModel.CreateClone"/> 留一份那一刻的快照
///     （原牌原地不动，不消耗、不移出手牌），规范卡则先在战斗里物化成可变副本；</item>
///   <item>**不设回合上界**：目标可以是还没到的回合（未来的桶由 <see cref="PlayerMemory.GetRound"/> 实时创建），
///     于是「把牌存到下一回合，之后再追忆出来」这种玩法成立；只有 <c>&lt; 1</c> 会被拒绝。</item>
/// </list>
/// </summary>
public sealed class RecordCmd
{
    private readonly PlayerChoiceContext? _choiceContext;
    private readonly Player _player;

    private CardPlay? _fromPlay;
    private int? _absoluteRound;
    private int _offset;

    private PileType _pile = PileType.Hand;
    private CardModel? _template;
    private bool _upgrade;
    private int _count = 1;

    private Func<CardModel, bool>? _filter;
    private LocString? _prompt;
    private AbstractModel? _source;

    private RecordCmd(PlayerChoiceContext? choiceContext, Player player)
    {
        _choiceContext = choiceContext;
        _player = player;
    }

    /// <summary>记录选择屏的默认标题（key 见 Reed/localization/zhs/static_hover_tips.json）。</summary>
    private static LocString DefaultPrompt => new LocString("static_hover_tips", "REED-RECORD.select.title");

    /// <summary>
    /// 选择屏标题：默认带上「记录到第几回合」。带回合数的文案 key 还没进 pck 时退回不带回合数的那条 ——
    /// 宁可少一句话，也不能让缺文案把整次记录搞崩（与 <see cref="RecallCmd"/> 同款兜底）。
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
            prompt = LocString.GetIfExists("static_hover_tips", "REED-RECORD.select.round.title");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecordCmd] 读取记录标题失败：{e.Message}");
        }
        if (prompt == null)
        {
            return DefaultPrompt;
        }
        prompt.Add("Round", round);
        return prompt;
    }

    /// <summary>开始构造一次记录。</summary>
    public static RecordCmd Record(PlayerChoiceContext? choiceContext, Player player) => new(choiceContext, player);

    // ============ 目标回合 ============

    /// <summary>
    /// 基准回合 = 这次出牌「视为打出」的回合（见 <see cref="MemoryPlayRound.PlayedRound"/>）。
    /// 传当前正在执行的 cardPlay，就能正确地「上一回合 / 下一回合」；被追忆打出的牌也能链式追溯。
    /// </summary>
    public RecordCmd FromPlay(CardPlay? play)
    {
        _fromPlay = play;
        return this;
    }

    /// <summary>基准回合 = 指定的绝对回合数（第 1 回合 = 1）。</summary>
    public RecordCmd FromRound(int round)
    {
        _absoluteRound = round;
        return this;
    }

    /// <summary>在基准回合上偏移：<c>-1</c> 上 1 回合 / <c>0</c> 当前回合 / <c>+x</c> 下 x 回合。</summary>
    public RecordCmd Offset(int offset)
    {
        _offset = offset;
        return this;
    }

    // ============ 要记录的牌 ============

    /// <summary>从**手牌**里让玩家挑（默认来源）。</summary>
    public RecordCmd FromHand()
    {
        _template = null;
        _pile = PileType.Hand;
        return this;
    }

    /// <summary>改从某个牌堆里挑（<see cref="PileType.Hand"/> = 与 <see cref="FromHand"/> 等价）。</summary>
    public RecordCmd FromPile(PileType pile)
    {
        _template = null;
        _pile = pile;
        return this;
    }

    /// <summary>
    /// 不做选择，直接记录这张牌（可以是规范卡，也可以是任何可变副本）——记录的是它的**副本**，
    /// 传进来的模板本身不会被改动。配合 <see cref="Upgraded"/> / <see cref="Count"/> 用。
    /// </summary>
    public RecordCmd Card(CardModel template)
    {
        _template = template;
        return this;
    }

    /// <summary>记录的副本按**已升级**处理（只在 <see cref="Card"/> 给的是规范卡时有意义）。</summary>
    public RecordCmd Upgraded(bool upgrade = true)
    {
        _upgrade = upgrade;
        return this;
    }

    /// <summary>记录几张（默认 1）。每一张都是**独立**的副本。</summary>
    public RecordCmd Count(int count)
    {
        _count = Math.Max(1, count);
        return this;
    }

    // ============ 候选筛选 / 表现 ============

    /// <summary>进一步筛选候选（从牌堆里挑时生效）。</summary>
    public RecordCmd Where(Func<CardModel, bool> predicate)
    {
        _filter = predicate;
        return this;
    }

    /// <summary>只从指定类型的牌里挑（Attack / Skill / Power）。</summary>
    public RecordCmd OfType(CardType type) => Where(c => c.Type == type);

    /// <summary>自定义选择屏标题。</summary>
    public RecordCmd WithPrompt(LocString prompt)
    {
        _prompt = prompt;
        return this;
    }

    /// <summary>给手牌选择界面用的「发起者」（日志 / 界面记录用），不给就依次退回本牌的 <see cref="CardPlay"/>、角色。</summary>
    public RecordCmd WithSource(AbstractModel source)
    {
        _source = source;
        return this;
    }

    // ============ 执行 ============

    /// <summary>执行记录。返回实际写进回忆的那些记录；无可选 / 越界时返回空列表。</summary>
    public async Task<List<RememberedCard>> Execute(PlayerChoiceContext? choiceContext = null)
    {
        List<RememberedCard> recorded = new();
        if (_player == null)
        {
            return recorded;
        }

        PlayerChoiceContext? context = choiceContext ?? _choiceContext;
        ICombatState? combat = _player.Creature?.CombatState;
        if (combat == null)
        {
            return recorded; // 回忆只活在战斗里
        }

        // 回合解析：绝对回合优先，否则以「这次出牌视为打出的回合」为基准；最后统一叠加偏移。
        // 与 RecallCmd 的区别是**不设上界**：目标可以是还没到的回合（比如 Overlook 的「下一回合」）。
        int baseRound = _absoluteRound ?? (_fromPlay != null ? _fromPlay.PlayedRound(combat) : combat.RoundNumber);
        int round = baseRound + _offset;
        if (round < 1)
        {
            return recorded; // 越界（比如第 1 回合再往上记录）
        }

        List<CardModel> picked;
        try
        {
            if (_template != null)
            {
                CardModel? template = Materialize(combat);
                if (template == null)
                {
                    return recorded;
                }
                // 同一个模板重复 N 次：Record 每次都克隆出一份**独立**的副本（回忆里改一张不牵连别的）。
                picked = Enumerable.Repeat(template, _count).ToList();
            }
            else
            {
                picked = await PickAsync(context, round);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecordCmd] 选择要记录的牌失败：{e.Message}");
            return recorded;
        }

        foreach (CardModel card in picked)
        {
            if (await MemorySystem.RecordAsync(context, combat, _player, round, card) is { } entry)
            {
                recorded.Add(entry);
            }
        }
        return recorded;
    }

    /// <summary>从手牌 / 指定牌堆里挑出要记录的那几张（数量见 <see cref="Count"/>）。</summary>
    private async Task<List<CardModel>> PickAsync(PlayerChoiceContext? context, int round)
    {
        List<CardModel> picked = new();
        if (context == null)
        {
            return picked;
        }

        // 只选 Count 张；候选恰好够数时 FromHand / FromSimpleGrid 会直接选中、免弹屏。
        CardSelectorPrefs prefs = new(PromptFor(round), _count);

        if (_pile == PileType.Hand)
        {
            // 手牌走 FromHand：候选就是手牌本身、选中的牌直接高亮（与 Poem 那类牌的选牌手势一致）。
            // source 只用于选牌界面的记录，取本牌 → 本牌的 cardPlay → 角色，逐级兜底。
            AbstractModel? source = _source ?? (AbstractModel?)_fromPlay?.Card ?? _player.Character;
            IEnumerable<CardModel> fromHand = await CardSelectCmd.FromHand(context, _player, prefs, _filter, source);
            picked.AddRange(fromHand);
            return picked;
        }

        IReadOnlyList<CardModel> candidates = CardsInPile(_pile);
        if (candidates.Count == 0)
        {
            return picked;
        }
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(context, candidates, _player, prefs);
        picked.AddRange(chosen);
        return picked;
    }

    /// <summary>取某个牌堆里可选的牌（按 <see cref="Where"/> 过滤）。取不到牌堆时返回空。</summary>
    private IReadOnlyList<CardModel> CardsInPile(PileType pile)
    {
        try
        {
            return pile.GetPile(_player).Cards
                .Where(c => c != null && (_filter == null || _filter(c)))
                .ToList();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecordCmd] 取 {pile} 牌堆失败：{e.Message}");
            return Array.Empty<CardModel>();
        }
    }

    /// <summary>
    /// 把「要记录的牌」（<see cref="Card"/> 给的模板）变成一份**可变**实例：规范卡先在战斗里物化，
    /// 可变副本则再克隆一次（免得「升级」反噬到调用方那张卡上）。升级只在这里做一次，
    /// 之后每一份记录副本都是它的克隆。
    /// </summary>
    private CardModel? Materialize(ICombatState combat)
    {
        CardModel? template = _template;
        if (template == null)
        {
            return null;
        }
        try
        {
            CardModel card = template.IsMutable ? template.CreateClone() : combat.CreateCard(template, _player);
            if (_upgrade && !card.IsUpgraded)
            {
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
            }
            return card;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RecordCmd] 无法物化要记录的牌 {template.Id}：{e.Message}");
            return null;
        }
    }
}
