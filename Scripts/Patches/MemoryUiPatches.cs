using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Patches;

/// <summary>
/// 把「回忆」按钮注入战斗界面的牌堆栏（<see cref="NCombatPilesContainer"/>）。
///
/// 原版容器在 <c>_Ready</c> 里取 <c>%DrawPile / %DiscardPile / %ExhaustPile</c>，所以 postfix 里
/// 三个字段一定已就绪。按钮**不占用原版节点**，而是照抄 <c>draw_pile.tscn</c> 的结构用纯 C# 拼一个
/// 80x80 的 Control（图标 <c>combat_ui/draw_pile.png</c> + 计数气泡 <c>combat_ui/pile_button_count.png</c>
/// + kreon 字体）；X 坐标跟抽牌堆同列（屏幕左侧），Y 坐标沿用消耗牌堆再上移一格 —— 于是它和
/// 右下角的「消耗 / 弃牌」列**分居屏幕两侧**，纵向高度不变。
///
/// 只在战斗主界面注入：同一个容器在 <c>choose_a_card_selection_screen</c> /
/// <c>simple_card_select_screen</c> 里也被复用，那两处的 <c>Owner</c> 不是 <c>CombatUi</c>。
/// </summary>
[HarmonyPatch(typeof(NCombatPilesContainer), nameof(NCombatPilesContainer._Ready))]
public static class MemoryPileButtonPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatPilesContainer __instance)
    {
        if (!IsCombatUi(__instance))
        {
            return;
        }
        if (__instance.GetNodeOrNull<MemoryPileButton>(MemoryPileButton.NodeName) != null)
        {
            return;
        }
        NExhaustPileButton? exhaust = __instance.ExhaustPile;
        if (exhaust == null || !GodotObject.IsInstanceValid(exhaust))
        {
            return;
        }
        MemoryPileButton button = MemoryPileButton.Create(exhaust, __instance.DrawPile);
        __instance.AddChildSafely(button);
    }

    /// <summary>
    /// 只在战斗主界面注入：<c>combat_ui.tscn</c> 里这个容器的节点名是 <c>CombatPileContainer</c>，
    /// 而两个选牌界面（<c>choose_a_card_selection_screen</c> / <c>simple_card_select_screen</c>）
    /// 里复用的同名场景实例叫 <c>CombatPiles</c>。用节点名 + 所属场景双重判断。
    /// </summary>
    private static bool IsCombatUi(Node node)
        => node.Name == "CombatPileContainer" || node.Owner?.Name == "CombatUi";
}

/// <summary>容器 <c>Initialize(Player)</c> 时把本地玩家交给按钮（刷新计数 / 点开界面用）。</summary>
[HarmonyPatch(typeof(NCombatPilesContainer), nameof(NCombatPilesContainer.Initialize))]
public static class MemoryPileButtonInitPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatPilesContainer __instance, Player player)
    {
        __instance.GetNodeOrNull<MemoryPileButton>(MemoryPileButton.NodeName)?.Bind(player);
    }
}

/// <summary>
/// 战斗界面的「回忆」牌堆按钮：外观复刻原版牌堆按钮（同一批贴图 + kreon 字体 + 计数气泡），
/// 点击（或按快捷键）开关 <see cref="MemoryPileScreen"/>，悬停显示原版风格 tip 并放大图标。
/// </summary>
internal sealed partial class MemoryPileButton : Control
{
    public const string NodeName = "MemoryPileButton";

    private const string IconPath = "res://images/packed/combat_ui/draw_pile.png";
    private const string CountBubblePath = "res://images/packed/combat_ui/pile_button_count.png";
    private const string FontPath = "res://themes/kreon_bold_glyph_space_one.tres";

    /// <summary>与原版牌堆按钮同尺寸（<c>draw_pile.tscn</c> 的 80x80）。</summary>
    private static readonly Vector2 ButtonSize = new(80f, 80f);

    private static readonly Color MemoryTint = new(StsColors.aqua, 0.95f);
    private static readonly Color CountColor = new(1f, 0.964706f, 0.886275f);
    private static readonly Color CountOutline = new(0.203922f, 0.121569f, 0.184314f);

    private const float HoverScale = 1.25f;
    private const double HoverAnimDur = 0.05;
    private const double UnhoverAnimDur = 0.5;
    private static readonly Color DownColor = Colors.DarkGray;

    private TextureRect _icon = null!;
    private Control _countContainer = null!;
    private Label _countLabel = null!;
    private NHoverTipSet? _tipSet;
    private Tween? _bumpTween;

    private Player? _player;
    private int _shownCount = -1;

    /// <summary>
    /// 当前战斗界面上的回忆按钮。追忆预览（<see cref="Memory.MemoryPreviewOverlay"/>）拿它定位
    /// 「扇形环绕的圆心」——按钮不在场（不在战斗界面）时预览会退回被悬浮卡牌自身的位置。
    /// </summary>
    internal static MemoryPileButton? Instance { get; private set; }

    /// <summary>
    /// 摆放：X 照抄**抽牌堆**（<c>combat_piles_container.tscn</c> 里它锚在左下角，offset 15~95），
    /// Y 照抄消耗牌堆并整体上移一格（offset -370 ~ -290）。
    ///
    /// <para>于是按钮落到屏幕**左侧**、纵向高度与改动前完全一致：右下角那列留给原版的
    /// 「消耗 → 弃牌」，回忆独立待在对面。</para>
    /// </summary>
    public static MemoryPileButton Create(Control exhaustPile, Control? drawPile)
    {
        // 刻意不设 MouseDefaultCursorShape：回忆牌堆不是「可点选的牌」，悬停时保持默认箭头，
        // 与原版抽牌堆/弃牌堆按钮（同样是纯查看入口）的观感区分开。
        MemoryPileButton button = new()
        {
            Name = NodeName,
            MouseFilter = MouseFilterEnum.Stop,
            // 回忆界面打开时战斗被 CombatManager 暂停；挂 Always 保证快捷键在界面里也能收得到，
            // 于是同一个键可以「开 → 关」。
            ProcessMode = ProcessModeEnum.Always,
        };

        if (drawPile != null && GodotObject.IsInstanceValid(drawPile))
        {
            button.AnchorLeft = drawPile.AnchorLeft;
            button.AnchorRight = drawPile.AnchorRight;
            button.OffsetLeft = drawPile.OffsetLeft;
            button.OffsetRight = drawPile.OffsetRight;
        }
        else
        {
            // 兜底：抽牌堆不在时贴左边缘摆，宽度保持 80。
            button.AnchorLeft = 0f;
            button.AnchorRight = 0f;
            button.OffsetLeft = 15f;
            button.OffsetRight = 15f + ButtonSize.X;
        }

        // Y：与 %ExhaustPile 同锚点（底部），整块上移一格 —— 和原位置一模一样。
        button.AnchorTop = exhaustPile.AnchorTop;
        button.AnchorBottom = exhaustPile.AnchorBottom;
        button.OffsetTop = exhaustPile.OffsetTop - 90f;
        button.OffsetBottom = exhaustPile.OffsetBottom - 90f;
        button.PivotOffset = ButtonSize * 0.5f;

        return button;
    }

    public override void _Ready()
    {
        Instance = this;

        _icon = new TextureRect
        {
            Name = "Icon",
            Texture = PreloadManager.Cache.GetTexture2D(IconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = MemoryTint,
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _icon.PivotOffset = ButtonSize * 0.5f;
        this.AddChildSafely(_icon);

        BuildCountBubble();

        // 显式打开「未消费按键」的处理开关（重写了 _UnhandledKeyInput 时引擎通常会自动开，
        // 这里写死更稳：快捷键是入口的一部分，不能被引擎版本差异吃掉）。
        SetProcessUnhandledKeyInput(true);

        MemorySystem.MemoryChanged += OnMemoryChanged;
        MemorySystem.Remembered += OnRemembered;
        MouseEntered += OnHoverStart;
        MouseExited += OnHoverEnd;

        RefreshCount();
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
        MemorySystem.MemoryChanged -= OnMemoryChanged;
        MemorySystem.Remembered -= OnRemembered;
        MouseEntered -= OnHoverStart;
        MouseExited -= OnHoverEnd;
        HideTip();
    }

    /// <summary>复刻 <c>draw_pile.tscn</c> 的计数气泡：48x48 气泡贴图 + 居中数字。</summary>
    private void BuildCountBubble()
    {
        _countContainer = new Control
        {
            Name = "CountContainer",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _countContainer.AnchorLeft = 0.5f;
        _countContainer.AnchorTop = 0.5f;
        _countContainer.AnchorRight = 0.5f;
        _countContainer.AnchorBottom = 0.5f;
        _countContainer.OffsetLeft = 8f;
        _countContainer.OffsetTop = -4f;
        _countContainer.OffsetRight = 56f;
        _countContainer.OffsetBottom = 44f;
        _countContainer.PivotOffset = new Vector2(24f, 24f);
        this.AddChildSafely(_countContainer);

        TextureRect bubble = new()
        {
            Name = "Background",
            Texture = PreloadManager.Cache.GetTexture2D(CountBubblePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bubble.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _countContainer.AddChildSafely(bubble);

        _countLabel = new Label
        {
            Name = "Count",
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _countLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _countLabel.OffsetLeft = 12f;
        _countLabel.OffsetTop = -26f;
        _countLabel.OffsetRight = -12f;
        _countLabel.OffsetBottom = 26f;
        _countLabel.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(FontPath));
        _countLabel.AddThemeFontSizeOverride("font_size", 26);
        _countLabel.AddThemeColorOverride("font_color", CountColor);
        _countLabel.AddThemeColorOverride("font_outline_color", CountOutline);
        _countLabel.AddThemeConstantOverride("outline_size", 12);
        _countLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.12549f));
        _countLabel.AddThemeConstantOverride("shadow_offset_x", 2);
        _countLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _countContainer.AddChildSafely(_countLabel);
    }

    /// <summary>由 <c>NCombatPilesContainer.Initialize</c> 注入本地玩家。</summary>
    public void Bind(Player player)
    {
        _player = player;
        RefreshCount();
    }

    // ============ 计数 ============

    private void OnMemoryChanged(Player player)
    {
        if (_player != null && player.NetId != _player.NetId)
        {
            return;
        }
        RefreshCount();
    }

    /// <summary>
    /// 有牌被记进回忆：让它飞向本按钮（复刻原版「卡牌拖着轨迹飞进牌堆」的特效，见
    /// <see cref="MemoryFlyVfx"/>）。
    ///
    /// <para>挂在**同步的** <see cref="MemorySystem.Remembered"/> 而不是
    /// <see cref="MemorySystem.RememberedAsync"/>：这是纯表现，必须即发即忘 —— 那个事件会被 await，
    /// 挂上去等于让回忆动画把出牌拖住。按钮只在战斗界面存在，而回忆只在战斗中记录，时机天然对得上；
    /// 按钮又是特效的终点，起点存在与否它都最先知道。</para>
    /// </summary>
    private void OnRemembered(Player player, RememberedCard entry)
    {
        // 按钮显示的是本地玩家的回忆，飞卡也只飞本地玩家的：联机时队友打出的牌不该飞向我的图标。
        Player? owner = ResolvePlayer();
        if (owner != null && owner.NetId != player.NetId)
        {
            return;
        }
        MemoryFlyVfx.Play(player, entry);
    }

    /// <summary>还没拿到 <see cref="Bind"/> 的玩家时自己兜底找一次（出牌界面重建 / 读档进战斗都可能错过注入）。</summary>
    private Player? ResolvePlayer()
    {
        if (_player == null)
        {
            _player = MemorySystem.ResolveLocalPlayer();
        }
        return _player;
    }

    private void RefreshCount()
    {
        if (_countLabel == null)
        {
            return;
        }
        int count = ResolvePlayer() is { } owner ? MemorySystem.Of(owner).TotalCount : 0;
        if (count == _shownCount)
        {
            return;
        }
        _shownCount = count;
        _countLabel.Text = count.ToString();

        // 有新回忆时抖一下计数气泡（同原版 AddCard 的观感）。
        if (count > 0 && _countContainer != null)
        {
            _countContainer.PivotOffset = _countContainer.Size * 0.5f;
            _bumpTween?.Kill();
            _bumpTween = CreateTween();
            _countContainer.Scale = Vector2.One * HoverScale;
            _bumpTween.TweenProperty(_countContainer, "scale", Vector2.One, 0.5)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Expo);
        }
    }

    // ============ 交互 ============

    /// <summary>
    /// 快捷键开关回忆牌堆。按钮只活在战斗界面，于是「监听窗口」天然等于战斗中的界面。
    ///
    /// <list type="bullet">
    ///   <item>用 <c>_UnhandledKeyInput</c>：已经被 UI 消费的按键（在输入框里打字、ESC 关界面等）不会误触；</item>
    ///   <item>按键来自 <see cref="ReedModConfig.MemoryToggleKey"/>，玩家可在「模组设置」里改成任意键；</item>
    ///   <item>同时比对 <c>Keycode</c> 与 <c>PhysicalKeycode</c>，非 QWERTY 布局下按物理键也能触发。</item>
    /// </list>
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        Key toggle = ReedModConfig.MemoryToggleKeycode;
        if (key.Keycode != toggle && key.PhysicalKeycode != toggle)
        {
            return;
        }
        GetViewport()?.SetInputAsHandled();
        OpenScreen();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            return;
        }
        if (button.Pressed)
        {
            AcceptEvent();
            _bumpTween?.Kill();
            _bumpTween = CreateTween().SetParallel();
            _bumpTween.TweenProperty(_icon, "scale", Vector2.One, 0.25);
            _bumpTween.TweenProperty(_icon, "modulate", DownColor, 0.25);
            return;
        }

        AcceptEvent();
        _bumpTween?.Kill();
        _bumpTween = CreateTween().SetParallel();
        _bumpTween.TweenProperty(_icon, "scale", Vector2.One, 0.05);
        _bumpTween.TweenProperty(_icon, "modulate", MemoryTint, UnhoverAnimDur)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        OpenScreen();
    }

    private void OnHoverStart()
    {
        ShowTip();
        if (_icon == null)
        {
            return;
        }
        _bumpTween?.Kill();
        _bumpTween = CreateTween();
        _bumpTween.TweenProperty(_icon, "scale", Vector2.One * HoverScale, HoverAnimDur);
    }

    private void OnHoverEnd()
    {
        HideTip();
        if (_icon == null)
        {
            return;
        }
        _bumpTween?.Kill();
        _bumpTween = CreateTween().SetParallel();
        _bumpTween.TweenProperty(_icon, "scale", Vector2.One, UnhoverAnimDur)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        _bumpTween.TweenProperty(_icon, "modulate", MemoryTint, UnhoverAnimDur)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
    }

    private void OpenScreen()
    {
        NCapstoneContainer? container = NCapstoneContainer.Instance;
        if (container == null)
        {
            return;
        }
        // 再点一次即收起，和原版牌堆按钮的开关手感一致。
        if (container.CurrentCapstoneScreen is MemoryPileScreen)
        {
            container.Close();
            return;
        }
        container.Open(MemoryPileScreen.Create(ResolvePlayer()));
    }

    // ============ 悬停 tip ============

    private void ShowTip()
    {
        LocString title = new("static_hover_tips", "REED-RECALL.pile.title");
        string description = FormatLoc(
            new LocString("static_hover_tips", "REED-RECALL.pile.description"),
            "本场战斗里你手动打出过的牌。按回合归档，仅供查看。");

        string hotkey = ReedModConfig.MemoryToggleKeyText;
        if (!string.IsNullOrEmpty(hotkey))
        {
            LocString hint = new("static_hover_tips", "REED-RECALL.pile.hotkey");
            hint.Add("Hotkey", hotkey);
            description = description + "\n" + FormatLoc(hint, $"快捷键：{hotkey}");
        }

        // 按钮挪到屏幕左侧后，tip 的方向也必须跟着变 —— 直接问原版要判断结果（右 25% 才往左弹）。
        HoverTipAlignment alignment = HoverTip.GetHoverTipAlignment(this, 0.5f);
        _tipSet = NHoverTipSet.CreateAndShow(this, new HoverTip(title, description), alignment);
    }

    /// <summary>本地化文案取不到就退回兜底文本（文案在 pck 里，没重导也不会把界面搞崩）。</summary>
    private static string FormatLoc(LocString loc, string fallback)
    {
        try
        {
            string text = loc.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MemoryPileButton] 本地化文案缺失（{e.Message}），已用兜底文本。");
            return fallback;
        }
    }

    private void HideTip()
    {
        if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
        {
            NHoverTipSet.Remove(this);
        }
        _tipSet = null;
    }
}
