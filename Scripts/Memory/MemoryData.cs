using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 一条回忆记录：某个回合里打出过的一张牌（的副本快照）。
/// 记录的是**打出时那一刻**的卡：升级、附魔、被 buff 过的动态变量都会保留下来。
/// </summary>
public sealed class RememberedCard
{
    /// <summary>这张牌被打出时所在的回合数（<see cref="MegaCrit.Sts2.Core.Combat.ICombatState.RoundNumber"/>）。</summary>
    public int RoundNumber { get; init; }

    /// <summary>打出时的可变副本快照（不进入任何牌堆，也不会收到战斗 hook）。</summary>
    public CardModel Card { get; init; } = null!;
}

/// <summary>
/// 一名玩家在本场战斗中的回忆，按回合数聚合。
///
/// 回合桶采用**安全获取**：<see cref="GetRound"/> 在桶不存在时会实时创建并加入，
/// 因此「只有 1~5 回合数据时去取第 6 回合」不会抛异常，而是拿到一个空列表并记住这个桶。
/// 每回合开始时系统会预创建「本回合」与「下一回合」两个桶（见 <see cref="MemorySystem"/>）。
/// </summary>
public sealed class PlayerMemory
{
    private readonly SortedDictionary<int, List<RememberedCard>> _rounds = new();

    /// <summary>【安全获取】取指定回合的回忆列表；不存在时实时创建并加入。</summary>
    public List<RememberedCard> GetRound(int round)
    {
        if (!_rounds.TryGetValue(round, out List<RememberedCard>? list))
        {
            list = new List<RememberedCard>();
            _rounds[round] = list;
        }
        return list;
    }

    /// <summary>只读查询：不创建。没有该回合（或该回合为空）时返回空列表。</summary>
    public IReadOnlyList<RememberedCard> PeekRound(int round)
    {
        return _rounds.TryGetValue(round, out List<RememberedCard>? list)
            ? list
            : Array.Empty<RememberedCard>();
    }

    /// <summary>该回合的桶是否已经存在（哪怕它是空的）。</summary>
    public bool HasRound(int round) => _rounds.ContainsKey(round);

    /// <summary>所有已创建回合的升序集合（UI 时间刻度用）。</summary>
    public IEnumerable<int> Rounds => _rounds.Keys;

    /// <summary>回忆总条数。</summary>
    public int TotalCount
    {
        get
        {
            int n = 0;
            foreach (List<RememberedCard> list in _rounds.Values)
            {
                n += list.Count;
            }
            return n;
        }
    }

    /// <summary>
    /// 这张牌（**引用相等**）所在的回忆回合数；它不在回忆里时返回 null。
    ///
    /// <para>回忆界面展示的就是 <see cref="RememberedCard.Card"/> 那个副本本身，所以把界面上/预览里
    /// 拿到的 <see cref="CardModel"/> 直接丢进来即可得到「它属于第几回合」——追忆类卡牌据此把
    /// 「上一回合」解释成「它所在回合的上一回合」（连锁）。</para>
    /// </summary>
    public int? RoundOf(CardModel? card)
    {
        if (card == null)
        {
            return null;
        }
        foreach ((int round, List<RememberedCard> entries) in _rounds)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i].Card, card))
                {
                    return round;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 【便捷过滤器】取指定回合里**最新被添加**的至多 <paramref name="count"/> 张牌（按记录顺序倒着取，
    /// 也就是同回合内最后打出的那几张），可选再叠一层 <paramref name="filter"/> 筛选卡牌本身。
    ///
    /// <para>追忆类卡牌的悬浮预览默认就用它：<c>memory.LatestCards(round, c =&gt; c.Type == CardType.Attack)</c>。
    /// 回合桶不存在（没打过牌 / 还没到那个回合）时返回空列表，不创建桶。</para>
    /// </summary>
    public List<CardModel> LatestCards(int round, Func<CardModel, bool>? filter = null, int count = 5)
    {
        List<CardModel> result = new();
        if (count <= 0)
        {
            return result;
        }
        IReadOnlyList<RememberedCard> entries = PeekRound(round);
        for (int i = entries.Count - 1; i >= 0 && result.Count < count; i--)
        {
            CardModel? card = entries[i].Card;
            if (card != null && (filter == null || filter(card)))
            {
                result.Add(card);
            }
        }
        return result;
    }
}

/// <summary>一场战斗里所有玩家的回忆（按 <see cref="MegaCrit.Sts2.Core.Entities.Players.Player.NetId"/> 区分）。</summary>
public sealed class MemoryStore
{
    private readonly Dictionary<ulong, PlayerMemory> _byPlayer = new();

    public PlayerMemory Of(ulong playerNetId)
    {
        if (!_byPlayer.TryGetValue(playerNetId, out PlayerMemory? memory))
        {
            memory = new PlayerMemory();
            _byPlayer[playerNetId] = memory;
        }
        return memory;
    }

    public IEnumerable<PlayerMemory> All => _byPlayer.Values;
}
