using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「常驻第 0 回合」的**玩家级账本**：不挂在任何卡牌 / 遗物实例上，跟着 <see cref="Player"/> 跨战斗留下来，
/// 每场战斗开始时重建进本场的 <see cref="MemorySystem.CarriedRound"/> 回忆。
///
/// <para><b>为什么不是 <c>[SavedProperty]</c></b>：它只认 <c>RelicModel</c> / <c>CardModel</c> 这类 Mutable 模型，
/// <see cref="Player"/> 不在其列。BaseLib 给「非游戏内模型」准备的通道是
/// <see cref="SavedSpireField{TKey,TVal}"/>：静态字段会在 <c>LatePostInit</c> 被反射取一次值（触发静态构造），
/// 随后注册成 <see cref="Player"/> 的扩展存档，跟着存档 / 多人同步一起走。</para>
///
/// <para><b>为什么是 <c>SerializableCard[]</c></b>：BaseLib 只给数组配了网络序列化器
/// （<c>List&lt;SerializableCard&gt;</c> 能写进存档但发不出包，注册时会直接报错），所以账本用数组、写入时整段替换。</para>
///
/// <para>与「无名诗集」的 <c>Carried</c> 互不相干：前者搬<b>上一场战斗最后一回合</b>，本账本记<b>手动指定</b>的牌。
/// 两者都写进同一个第 0 回合桶，<see cref="MemorySystem.MinRound"/> 因此自动为 0。</para>
/// </summary>
public static class RoundZeroLedger
{
    /// <summary>
    /// 账本本体。存档键是 <c>Player_round_zero_records</c>（BaseLib 会自动加上 <see cref="Player"/> 的类型名前缀）。
    /// </summary>
    private static readonly SavedSpireField<Player, SerializableCard[]> Records =
        new(() => [], "round_zero_records");

    /// <summary>已经重建过账本的那场战斗（防重复：<c>SetUpCombat</c> 万一被调两次）。</summary>
    private static CombatState? _restoredFor;

    /// <summary>账本里记着的牌（顺序 = 记下的顺序）；没有记录时是空表。</summary>
    public static IReadOnlyList<SerializableCard> Get(Player? player)
    {
        return player == null ? [] : Records.Get(player) ?? [];
    }

    /// <summary>
    /// 把一张牌记进账本（同一张牌只记一次），并**立刻**写进本场战斗的第 0 回合。
    /// 返回 true 表示这次确实往本场第 0 回合加了一张（本场已经有了时返回 false）。
    ///
    /// <para>这是玩家主动「记录」，所以与 <see cref="RecordCmd"/> 同口径：加完之后补一遍
    /// <see cref="MemorySystem.NotifyRememberedAsync"/>，博物那类订阅者才算得到这次记录。</para>
    /// </summary>
    public static async Task<bool> AddAsync(PlayerChoiceContext? choiceContext, Player? player, ICombatState? combat, CardModel? card)
    {
        if (player == null || card == null)
        {
            return false;
        }

        SerializableCard save;
        try
        {
            save = card.ToSerializable();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[RoundZeroLedger] 无法保存 {card.Id}：{e.Message}");
            return false;
        }

        SerializableCard[] current = Records.Get(player) ?? [];
        if (!current.Any(s => Same(s, save)))
        {
            SerializableCard[] next = new SerializableCard[current.Length + 1];
            current.CopyTo(next, 0);
            next[^1] = save;
            Records.Set(player, next);
        }

        // 本场战斗立刻生效：玩家打出这张牌的这一场，第 0 回合里就该有它。
        // 战斗开始时已经由 Restore 写过一遍，这里只补「本场还没有的那几张」。
        if (HasInCarriedRound(combat, player, save))
        {
            return false;
        }

        List<RememberedCard> added = MemorySystem.RestoreCarriedRound(combat, player, [save]);
        foreach (RememberedCard entry in added)
        {
            await MemorySystem.NotifyRememberedAsync(choiceContext, player, entry.Card);
        }
        return added.Count > 0;
    }

    /// <summary>把账本里的牌全部重建进本场战斗的第 0 回合。返回实际重建的张数。</summary>
    public static int Restore(ICombatState? combat, Player? player)
    {
        return player == null ? 0 : MemorySystem.RestoreCarriedRound(combat, player, Records.Get(player)).Count;
    }

    /// <summary>
    /// 对战斗里的每个玩家重建一遍账本。由 <c>CombatSetupPatch</c> 在 <c>SetUpCombat</c> 之后调用
    /// （那时回忆表刚被清空，此刻写进去的不会被打扫掉，且早于 <c>BeforeCombatStart</c>）。
    /// </summary>
    public static void RestoreAll(CombatState? state)
    {
        // 同一场战斗只跑一次（SetUpCombat 若被重复调用，账本会被灌两遍）。
        if (state == null || ReferenceEquals(state, _restoredFor))
        {
            return;
        }
        _restoredFor = state;
        foreach (Player player in state.Players)
        {
            try
            {
                Restore(state, player);
            }
            catch (Exception e)
            {
                GD.PushWarning($"[RoundZeroLedger] 第 0 回合重建失败（{player?.NetId}）：{e.Message}");
            }
        }
    }

    /// <summary>本场战斗的第 0 回合里是否已经有一张「同样的牌」。</summary>
    private static bool HasInCarriedRound(ICombatState? combat, Player player, SerializableCard save)
    {
        if (combat == null)
        {
            return false;
        }
        foreach (RememberedCard entry in MemorySystem.Of(combat, player).PeekRound(MemorySystem.CarriedRound))
        {
            CardModel? card = entry.Card;
            if (card != null
                && Equals(card.Id, save.Id)
                && card.CurrentUpgradeLevel == save.CurrentUpgradeLevel
                && Equals(card.Enchantment?.Id, save.Enchantment?.Id)
                && (card.Enchantment?.Amount ?? 0) == (save.Enchantment?.Amount ?? 0))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>账本去重口径：同名 + 同升级 + 同附魔（动态数值不算——跨战斗只认「同一张牌」）。</summary>
    private static bool Same(SerializableCard a, SerializableCard b)
    {
        return a != null
            && b != null
            && Equals(a.Id, b.Id)
            && a.CurrentUpgradeLevel == b.CurrentUpgradeLevel
            && Equals(a.Enchantment?.Id, b.Enchantment?.Id)
            && (a.Enchantment?.Amount ?? 0) == (b.Enchantment?.Amount ?? 0);
    }
}
