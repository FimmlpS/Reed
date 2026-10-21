using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using ReedCharacter = Reed.Scripts.Characters.Reed;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 单个生物一场所携带的抗性数据（每场战斗的 Creature 都是新对象，天然每场重置为满 4）。
/// 数值只有满抗性是被动的、由 0 到 1 格；本数据默认上限 4，仅在生物 HP 条初始化（NHealthBar.SetCreature）时建立。
/// </summary>
public sealed class ResistanceData
{
    public const int DefaultMax = 4;

    public ResistanceData(Creature _creature)
    {
        Owner = _creature;
        Max = MaxForRoom(Owner.CombatState, _creature);
    }

    /// <summary>
    /// 按房间类型算出的抗性上限：普通 <see cref="DefaultMax"/>、精英 +1、首领 +2。
    /// 挂到 <see cref="ResistanceSystem.ResetForNewCombat"/> 上是为了让"跨战斗存活"的生物（玩家）
    /// 每场都按本场房间重算，而不是停在它第一次建数据时那场的房间类型上。
    ///
    /// <para>多人：再乘上场上苇草角色的数量（2 个苇草打普通/精英/首领 = 8/10/12）。
    /// 只乘敌人 —— 玩家自己的条长度不受影响（<see cref="Creature.IsPlayer"/>）。</para>
    /// </summary>
    public static int MaxForRoom(ICombatState? combat, Creature creature)
    {
        int max = DefaultMax;
        RoomType type = combat?.RunState.CurrentRoom?.RoomType ?? RoomType.Unassigned;
        if (type == RoomType.Elite)
        {
            max += 1;
        }
        else if (type == RoomType.Boss)
        {
            max += 2;
        }
        return max * ReedScale(combat, creature);
    }

    /// <summary>
    /// 敌人抗性上限要乘的倍数 = 场上苇草角色数量；玩家自己恒为 1 倍。
    /// 读不到战斗/玩家时也按 1 倍（与单人一致）。
    /// </summary>
    private static int ReedScale(ICombatState? combat, Creature creature)
    {
        if (creature.IsPlayer)
        {
            return 1;
        }
        int reeds = combat?.Players.Count(p => p.Character is ReedCharacter) ?? 1;
        return reeds < 1 ? 1 : reeds;
    }

    public Creature Owner;

    /// <summary>抗性上限。</summary>
    public int Max { get; internal set; } = DefaultMax;

    /// <summary>当前抗性值（0..Max）。</summary>
    public int Current { get; internal set; } = DefaultMax;

    /// <summary>是否处于燃烧状态（燃烧条为紫色）。</summary>
    public bool Burning { get; internal set; }

    /// <summary>本场战斗内抗性值是否发生过变化（用于“规则7：变化过则显示到战斗结束”）。</summary>
    public bool Changed { get; internal set; }

    /// <summary>附着的抗性条 UI（在生物有状态条时非空）。</summary>
    internal ResistanceBarVisual? Bar;

    /// <summary>燃烧时循环喷火的驱动器。</summary>
    internal BurningFireDriver? Fire;

    /// <summary>
    /// 火花牌：按火花 owner（Player.NetId）分组，每人一组。抗性条是全员共有的，但火花牌是
    /// 每个玩家各自绑定的 → 同一生物可被多名玩家各绑一组（多人模式）。
    /// </summary>
    internal readonly Dictionary<ulong, List<CardModel>> Sparks = new();

    /// <summary>
    /// 每组 owner 的火花上限（缺省用 ResistanceSystem.DefaultSparkMax）。想单独调某个生物/某玩家的
    /// 上限时通过 ResistanceSystem.SetSparkMax(c, owner, max) 写入，供规则/卡牌动态扩容或收紧。
    /// </summary>
    internal readonly Dictionary<ulong, int> SparkMaxes = new();
}
