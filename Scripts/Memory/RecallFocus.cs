using System;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「正在追忆」的全局状态：<see cref="RecallCmd"/> 的选择界面打开期间，记下**正在追忆第几回合**的回忆。
///
/// <list type="bullet">
///   <item>选择界面自己用不到它 —— 那里的回合数直接写进了标题（见 <see cref="RecallCmd"/>）；</item>
///   <item>它真正的用处是<b>回忆界面</b>：追忆选择屏开着的时候顺手打开回忆界面，刻度列会把这一回合
///     用**蓝色**标出来（同「当前回合」的金色展示：完整标题 + 加粗刻度 + 圆点 + 箭头），
///     一眼就能看出这次追忆的是哪一回合的牌；若这一回合恰好就是当前回合，则蓝色**覆盖**金色。</item>
///   <item>追忆结束（选中、取消、异常）都会 <see cref="End"/>，界面据此把蓝色标记收回。</item>
/// </list>
///
/// <para>状态是全局静态的：同一时刻只可能有一次本地玩家在选牌。多人下用 <see cref="RoundFor"/>
/// 过滤掉别人的追忆，别人的回忆界面不会因为你在追忆而亮蓝标记。</para>
/// </summary>
public static class RecallFocus
{
    private static int? _round;
    private static Player? _player;

    /// <summary>追忆开始 / 结束时触发（<see cref="RememberedCard"/> 之类的内容没变，界面只需重画刻度）。</summary>
    public static event Action? Changed;

    /// <summary>正在追忆的回合数；没有在追忆时为 null。</summary>
    public static int? Round => _round;

    /// <summary>正在追忆的玩家；没有在追忆时为 null。</summary>
    public static Player? Player => _player;

    /// <summary>进入「正在追忆第 <paramref name="round"/> 回合」状态。</summary>
    public static void Begin(int round, Player player)
    {
        _round = round;
        _player = player;
        Changed?.Invoke();
    }

    /// <summary>离开追忆状态（幂等：本来就不在追忆时不会重复通知）。</summary>
    public static void End()
    {
        if (_round == null && _player == null)
        {
            return;
        }
        _round = null;
        _player = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// 给界面用：<paramref name="viewer"/> 是在看谁的回忆。不是这位玩家在追忆、或当前没人在追忆时返回 null。
    /// （<paramref name="viewer"/> 为空时不做过滤 —— 界面拿不到玩家时也该正常显示。）
    /// </summary>
    public static int? RoundFor(Player? viewer)
    {
        if (_round == null || _player == null)
        {
            return null;
        }
        if (viewer != null && viewer.NetId != _player.NetId)
        {
            return null;
        }
        return _round;
    }
}
