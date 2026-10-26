using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Relics;

/// <summary>
/// 无名诗集：把上一场战斗最后一回合的回忆搬进下一场战斗，成为「第 0 回合」。
///
/// <para>它是**唯一**会创建第 0 回合的东西：<see cref="MemorySystem.Record"/> 与 <c>RecordCmd</c>
/// 都只接受 <c>round &gt;= 1</c>，所以没有这件遗物时，第 0 回合的桶根本不存在，
/// 追忆的越界闸门（<see cref="MemorySystem.MinRound"/>）也就照旧停在第 1 回合。</para>
///
/// <para>「最后一回合」取战斗结束那一刻的 <see cref="ICombatState.RoundNumber"/>，而不是回忆里最大的回合号 ——
/// 系统会在每个回合开始预建「下一回合」的空桶，未来回合的桶可能存在但根本没有牌。</para>
/// </summary>
[Pool(typeof(ReedRelicPool))]
public class NamelessPoem : AbstractReedRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory),
        HoverTipFactory.FromKeyword(ReedKeywords.Recall)
    ];

    /// <summary>
    /// 上一场战斗最后一回合的回忆快照（战斗结束时导出、战斗开始时重建）。
    /// 落了 <c>[SavedProperty]</c>：中途存档退出再回来，这首诗里夹着的牌不会丢
    /// （<see cref="List{SerializableCard}"/> 是 BaseLib 明确支持的存档值类型）。
    /// </summary>
    [SavedProperty]
    public List<SerializableCard> Carried
    {
        get => _carried;
        set => _carried = value ?? new();
    }

    private List<SerializableCard> _carried = new();

    public override Task AfterCombatEnd(CombatRoom room)
    {
        ICombatState? combat = Owner.Creature?.CombatState;
        if (combat == null)
        {
            Carried = new();
            return Task.CompletedTask;
        }
        // 回合数取「结束战斗时所在的」那个回合：未来回合的空桶不算数。
        Carried = MemorySystem.ExportRound(combat, Owner, combat.RoundNumber);
        return Task.CompletedTask;
    }

    public override Task BeforeCombatStartLate()
    {
        // 这时 CombatSetUp 已经清过场，把快照重建进本场战斗的第 0 回合。
        MemorySystem.RestoreCarriedRound(Owner.Creature?.CombatState, Owner, Carried);
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 只认自己的回合：多人局里别人的回合开始也会广播到这个 hook。
        if (player != Owner)
        {
            return;
        }
        // TurnNumber 是「这名玩家在本场战斗里打的第几个回合」，== 1 只在开局那一次成立
        // （额外回合、后续回合都会 ++），不需要再加标志位。
        if (player.PlayerCombatState?.TurnNumber != 1)
        {
            return;
        }

        // 第 0 回合没有攻击/技能牌时这里直接 no-op（候选为 0 张 → 不弹屏、不打出）。
        await RecallCmd.Recall(choiceContext, Owner)
            .FromRound(MemorySystem.CarriedRound)
            .Where(c => c.Type == CardType.Attack || c.Type == CardType.Skill)
            .Execute(choiceContext);
    }
}
