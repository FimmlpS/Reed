using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Random;
using Reed.Scripts.Enums;
using Reed.Scripts.Patches;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「这张牌被记进回忆了」的飞卡特效：一张卡从**它被打出的地方**起飞，拖一条轨迹，
/// 飞向战斗界面上的**回忆按钮**。
///
/// <list type="bullet">
///   <item>动作与节奏照原版「卡牌飞进牌堆」的那一套（贝塞尔弧线 + 沿途加速 + 卡面变暗缩小 +
///     轨迹粒子 + 收尾自释放），外观是回忆自己的：**卡更小、轨迹更窄更淡、颜色改成浅青绿**
///     —— 调参与换色都在 <see cref="MemoryCardFlyVfx"/> 里。</item>
///   <item>飞的是**回忆里存的副本**（<see cref="RememberedCard.Card"/>），不是场上那张真卡 ——
///     飞卡特效最后会把卡节点归还池子，而场上那张还要被游戏继续使用；副本的卡面内容与打出那一刻
///     完全一致，正是「被记住的这张牌」。</item>
///   <item><b>起点</b>：被记录的牌此刻还留在 Play 堆（记录发生在它被移走之前），
///     <see cref="NCard.FindOnTable"/> 能拿到它的卡面节点；拿不到（自动打出 / 牌已不在场上）时
///     从屏幕中间附近的随机点起飞。</item>
///   <item><b>终点</b>：回忆按钮中心。整套是**独立实现**的 <see cref="MemoryCardFlyVfx"/>，
///     不复用也不改写原版 <see cref="NCardFlyVfx"/>（它的终点是私有字段、只认那几个牌堆按钮）：
///     于是「卡牌飞进其他牌堆」的观感与流程一点不受影响。</item>
///   <item>整体是**即发即忘的表现层**：挂在同步的 <see cref="MemorySystem.Remembered"/> 上，
///     一秒都别拖住出牌；出错只记一条 warning，绝不往上抛。</item>
/// </list>
/// </summary>
internal static class MemoryFlyVfx
{
    /// <summary>没有源卡时，起点在屏幕中央这个比例范围内随机抖动。</summary>
    private const float CenterJitterX = 0.12f;
    private const float CenterJitterY = 0.10f;

    /// <summary>
    /// 有牌被记进回忆：放一次飞卡特效。**任何异常都只记日志**——
    /// 这个函数是在出牌流程里被同步调用的，特效失败绝不能把出牌一起带崩。
    /// </summary>
    public static void Play(Player player, RememberedCard entry)
    {
        try
        {
            PlayCore(player, entry);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Memory] 回忆飞卡特效失败（已忽略）：{e}");
        }
    }

    private static void PlayCore(Player player, RememberedCard entry)
    {
        if (entry?.Card == null || player.Creature == null)
        {
            return;
        }

        // 终点 = 回忆按钮图标中心。按钮不存在（不在战斗界面）就没有「飞向回忆」可言。
        MemoryPileButton? button = MemoryPileButton.Instance;
        if (button == null || !GodotObject.IsInstanceValid(button) || !button.IsInsideTree())
        {
            return;
        }
        // 与原版牌堆目标点同款算路（button.GlobalPosition + Size * 0.5f）。
        Vector2 target = button.GlobalPosition + button.Size * 0.5f;

        // 容器 = 玩家生物自己的 VFX 容器（与原版 CardPileCmd 的战斗内飞卡一致）。
        Control? container = player.Creature.GetVfxContainer();
        if (container == null || !GodotObject.IsInstanceValid(container))
        {
            return;
        }

        // 轨迹场景来自角色；取不到就别飞了（宁可没有特效，也不要一个没轨迹、卡在半空的动画）。
        string trailPath = player.Character.TrailPath;
        if (string.IsNullOrEmpty(trailPath))
        {
            return;
        }

        Vector2 start = ResolveStart(entry, container);

        NCard? card = NCard.Create(entry.Card);
        if (card == null)
        {
            return; // TestMode 下原版工厂返回 null
        }

        bool handedOver = false;
        try
        {
            // 池里的卡可能带着上一次使用留下的 pivot（NCard 还池不复位它）：这张卡整个飞行过程都在
            // 旋转/缩放，脏 pivot 会让它绕着偏离卡心的点打转 —— 见 CardPoolHygienePatch。
            // 卡面比例不在这里设：那属于这次特效的观感，统一由 MemoryCardFlyVfx 决定。
            card.PivotOffset = Vector2.Zero;
            card.Rotation = 0f;
            card.Modulate = Colors.White;
            card.SelfModulate = Colors.White;
            card.Visible = true;
            card.MouseFilter = Control.MouseFilterEnum.Ignore; // 飞行途中别挡住底下的界面点击

            container.AddChildSafely(card);
            card.GlobalPosition = start; // 卡面根节点的原点就是卡牌中心
            card.UpdateVisuals(ReedPileType.Memory, CardPreviewMode.Normal); // 入树之后才有效

            MemoryCardFlyVfx vfx = MemoryCardFlyVfx.Create(card, start, target, trailPath);

            // 从这里起这张卡归 vfx 管：动画结束（或卡离开场景树）时由它把卡还池、并自释放。
            handedOver = true;
            container.AddChildSafely(vfx); // _Ready 里会建轨迹、定弧线方向，然后开始逐帧推进
        }
        catch
        {
            if (!handedOver && GodotObject.IsInstanceValid(card))
            {
                card.QueueFreeSafely();
            }
            throw;
        }
    }

    /// <summary>
    /// 起飞点：优先用**被记录的那张牌**此刻在场上（Play / Hand 堆）的卡面位置；
    /// 找不到就从屏幕中间附近的随机点起飞。
    /// </summary>
    private static Vector2 ResolveStart(RememberedCard entry, Control container)
    {
        // 回忆里存的是副本，副本不属于任何牌堆（Pile 为 null），FindOnTable 认不出它；
        // CloneOf 才是真正被打出的那张卡，此刻它还留在 Play 堆里。
        CardModel live = entry.Card.CloneOf ?? entry.Card;
        NCard? source = NCard.FindOnTable(live);
        if (source != null && GodotObject.IsInstanceValid(source))
        {
            return source.GlobalPosition;
        }

        Vector2 size = container.GetViewportRect().Size;
        return size * 0.5f + new Vector2(
            (float)GD.RandRange(-CenterJitterX, CenterJitterX) * size.X,
            (float)GD.RandRange(-CenterJitterY, CenterJitterY) * size.Y);
    }
}

/// <summary>
/// 回忆飞卡本身：运动照抄原版 <see cref="NCardFlyVfx"/>（同一条混沌流取随机量、同样的贝塞尔弧线、
/// 同样的加速与收尾），三处按回忆的观感重调：
///
/// <list type="number">
///   <item>飞的那张卡比原版小一圈（<see cref="CardScale"/>）；</item>
///   <item>轨迹比原版窄一圈、淡一半（<see cref="TrailWidthScale"/> / <see cref="TrailAlphaScale"/>）；</item>
///   <item>轨迹整棵树换成浅青绿（<see cref="TrailColor"/> / <see cref="TrailInnerColor"/>）。</item>
/// </list>
///
/// <para>它是**独立的一个类**，终点由调用方给定 —— 与「卡牌飞进其他牌堆」的原版流程彻底分开，
/// 原版那边的尺寸、颜色、终点一个字节都不动。</para>
/// </summary>
internal sealed partial class MemoryCardFlyVfx : Node2D
{
    /// <summary>卡面比例（原版飞卡是 1.0，这里小一圈）。</summary>
    private const float CardScale = 0.7f;

    /// <summary>轨迹宽度比例（原版外层 96 → 57.6、内层 64 → 38.4）。</summary>
    private const float TrailWidthScale = 0.6f;

    /// <summary>轨迹透明度总系数：原版整条轨迹偏亮，这里整体压到一半（各层之间原本的浓淡比例不变）。</summary>
    private const float TrailAlphaScale = 0.5f;

    /// <summary>轨迹主色：浅青绿。</summary>
    private static readonly Color TrailColor = new(0.49f, 1f, 0.77f);

    /// <summary>较淡那一层（内层轨迹、小的卡形轮廓）的颜色：更浅、偏白的青绿。</summary>
    private static readonly Color TrailInnerColor = new(0.78f, 1f, 0.92f);

    /// <summary>alpha 到这个值以上的轨迹层算「实层」，用主色；更淡的用浅色（原版就是靠 alpha 分层的）。</summary>
    private const float PaleAlphaCutoff = 0.6f;

    private NCard _card = null!;
    private string _trailPath = "";
    private Vector2 _startPos;
    private Vector2 _endPos;

    private NCardTrailVfx? _vfx;
    private bool _vfxFading;

    private float _controlPointOffset;
    private float _duration;
    private float _speed;
    private float _accel;
    private float _arcDir;

    private readonly CancellationTokenSource _cancelToken = new();

    public static MemoryCardFlyVfx Create(NCard card, Vector2 startPos, Vector2 endPos, string trailPath)
    {
        card.Scale = Vector2.One * CardScale; // 比原版飞卡小一圈
        return new MemoryCardFlyVfx
        {
            Name = "MemoryCardFlyVfx",
            _card = card,
            _startPos = startPos,
            _endPos = endPos,
            _trailPath = trailPath,
        };
    }

    public override void _Ready()
    {
        _vfx = NCardTrailVfx.Create(_card, _trailPath);
        if (_vfx != null)
        {
            // 入树之前改完：轨迹自己的 _Ready 会去动 Sprites 容器的 modulate / scale，
            // 我们只碰**子节点**的 modulate 与宽度，两边互不冲突。
            RestyleTrail(_vfx);
            GetParent().AddChildSafely(_vfx); // 与原版一样挂成同级：轨迹不跟着飞卡节点的变换走
        }

        // 与原版同一套随机量与节奏。
        _controlPointOffset = Rng.Chaotic.NextFloat(100f, 400f);
        _speed = Rng.Chaotic.NextFloat(1.1f, 1.25f);
        _accel = Rng.Chaotic.NextFloat(2f, 2.5f);
        _arcDir = _endPos.Y < GetViewportRect().Size.Y * 0.5f ? -500f : 500f + _controlPointOffset;
        _duration = Rng.Chaotic.NextFloat(1f, 1.75f);

        // 卡一旦离开场景树（动画收尾把它还池、或界面被拆掉），轨迹与本节点一并收掉。
        _card.Connect(Node.SignalName.TreeExited, Callable.From(OnCardExitedTree));
        TaskHelper.RunSafely(PlayAnim());
    }

    public override void _ExitTree()
    {
        _cancelToken.Cancel();
    }

    private void OnCardExitedTree()
    {
        try
        {
            _vfx?.QueueFreeSafely();
        }
        catch (ObjectDisposedException)
        {
        }
        this.QueueFreeSafely();
    }

    /// <summary>飞行 + 收尾两段（与原版逐字一致的节奏），中间只换了配色与尺寸。</summary>
    private async Task PlayAnim()
    {
        // 副本不属于任何牌堆，所以没有原版那句「按牌堆播 swoosh」：回忆飞卡是无声的。
        float time = 0f;
        while (time / _duration <= 1f)
        {
            await this.AwaitProcessFrameNonThrowing(_cancelToken);
            if (_cancelToken.IsCancellationRequested || !GodotObject.IsInstanceValid(_card))
            {
                Abort();
                return;
            }

            float delta = (float)GetProcessDeltaTime();
            time += _speed * delta;
            _speed += _accel * delta;
            StepAlongArc(time, delta);
        }
        _card.GlobalPosition = _endPos;

        // 到站：轨迹淡出、卡面缩到没有，最后把卡还池（TreeExited 会收掉轨迹与本节点）。
        time = 0f;
        while (time / _duration <= 1f)
        {
            await this.AwaitProcessFrameNonThrowing(_cancelToken);
            if (_cancelToken.IsCancellationRequested || !GodotObject.IsInstanceValid(_card))
            {
                Abort();
                return;
            }

            float delta = (float)GetProcessDeltaTime();
            time += _speed * delta;
            if (time / _duration > 0.25f && !_vfxFading)
            {
                if (_vfx != null)
                {
                    // 轨迹自己淡出并自释放（RunSafely 返回的是被包住的 Task，这里就是不等它）。
                    _ = TaskHelper.RunSafely(_vfx.FadeOut());
                }
                _vfxFading = true;
            }
            _card.Body.Scale = Vector2.One * Mathf.Max(Mathf.Lerp(0.1f, -0.15f, time / _duration), 0f);
        }
        _card.QueueFreeSafely();
    }

    /// <summary>沿弧线推进一步：定位置、按切线转角、卡面变暗缩小（与原版逐字一致）。</summary>
    private void StepAlongArc(float time, float delta)
    {
        Vector2 arcControl = _startPos + (_endPos - _startPos) * 0.5f;
        arcControl.Y -= _arcDir;
        Vector2 ahead = MathHelper.BezierCurve(_startPos, _endPos, arcControl, (time + 0.05f) / _duration);
        _card.GlobalPosition = MathHelper.BezierCurve(_startPos, _endPos, arcControl, time / _duration);

        // 卡面底边朝向飞行方向；父节点自身带旋转时要减掉，否则会多转一次。
        float angle = (ahead - _card.GlobalPosition).Angle() + Mathf.Pi / 2f;
        Node parent = _card.GetParent();
        if (parent is Control control)
        {
            angle -= control.Rotation;
        }
        else if (parent is Node2D node2D)
        {
            angle -= node2D.Rotation;
        }
        _card.Rotation = Mathf.LerpAngle(_card.Rotation, angle, delta * 12f);

        float darken = Mathf.Clamp(time * 3f / _duration, 0f, 1f);
        _card.Body.Modulate = Colors.White.Lerp(Colors.Black, darken);
        _card.Body.Scale = Vector2.One * Mathf.Lerp(1f, 0.1f, darken);
    }

    /// <summary>
    /// 中途收摊（战斗界面被拆 / 本节点被移除 / 卡提前还池）：把卡还池 —— 卡离开场景树会连锁触发
    /// <see cref="OnCardExitedTree"/>，轨迹与本节点一并收掉；卡已经不在树上时自己收尾。
    /// </summary>
    private void Abort()
    {
        if (GodotObject.IsInstanceValid(_card))
        {
            bool wasInTree = _card.IsInsideTree();
            _card.QueueFreeSafely();
            if (wasInTree)
            {
                return; // TreeExited → OnCardExitedTree 负责收掉轨迹与本节点
            }
        }
        try
        {
            _vfx?.QueueFreeSafely();
        }
        catch (ObjectDisposedException)
        {
        }
        if (GodotObject.IsInstanceValid(this))
        {
            this.QueueFreeSafely();
        }
    }

    /// <summary>
    /// 把整棵原版轨迹树重新上色 + 收窄。**按节点类型**处理而不是按名字 / 路径，
    /// 于是换一个角色的 trail 场景（结构可能不同）也照样能用。
    ///
    /// <para>只替换 RGB、保留每个节点**原本的 alpha**：原版靠两层不同透明度的轨迹叠出层次，
    /// 保留 alpha 就是保留那套层次，只是换了色相。</para>
    /// </summary>
    private static void RestyleTrail(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            switch (child)
            {
                case Line2D line:
                    line.Width *= TrailWidthScale;
                    line.Modulate = Retint(line.Modulate);
                    break;
                case Sprite2D sprite: // 轨迹上的小卡形轮廓
                    sprite.Modulate = Retint(sprite.Modulate);
                    break;
                case CpuParticles2D particles:
                    particles.ColorRamp = Retint(particles.ColorRamp);
                    break;
            }
            RestyleTrail(child);
        }
    }

    /// <summary>保留 alpha 的浓淡关系、换成回忆配色，并整体压暗一档（<see cref="TrailAlphaScale"/>）。</summary>
    private static Color Retint(Color vanilla)
    {
        Color tint = vanilla.A >= PaleAlphaCutoff ? TrailColor : TrailInnerColor;
        return new Color(tint.R, tint.G, tint.B, vanilla.A * TrailAlphaScale);
    }

    /// <summary>
    /// 粒子色带的同款换色：每个色标的**亮度**留在原来的水平（取 RGB 最大通道当系数）、色相换成主色、
    /// alpha 按 <see cref="TrailAlphaScale"/> 整体压一半 —— 于是「亮 → 暗 → 透明」的衰减节奏不变，
    /// 只是从橙金变成浅青绿、并且更淡。
    ///
    /// <para>必须**新建**一个 <see cref="Gradient"/> 赋给这个实例：场景里的子资源是所有实例共享的，
    /// 就地改会把原版飞卡（乃至所有正在飞的卡）一起染绿。</para>
    /// </summary>
    private static Gradient? Retint(Gradient? vanilla)
    {
        if (vanilla == null)
        {
            return null;
        }
        int count = vanilla.GetPointCount();
        Color[] colors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            Color c = vanilla.GetColor(i);
            float brightness = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
            colors[i] = new Color(
                TrailColor.R * brightness,
                TrailColor.G * brightness,
                TrailColor.B * brightness,
                c.A * TrailAlphaScale);
        }
        return new Gradient
        {
            InterpolationMode = vanilla.InterpolationMode,
            Offsets = vanilla.Offsets,
            Colors = colors,
        };
    }
}
