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
