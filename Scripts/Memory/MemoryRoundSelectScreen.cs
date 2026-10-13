using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「选择一个回忆回合」界面：双栏 —— 左边是**回合列表**（每行：回合号 + 张数 + 类型统计 + 最多 4 张卡面缩略图），
/// 右边是**该回合全部回忆**的卡牌网格（可点击放大查看）。给「英雄史诗」这类要按回合重现回忆的牌用。
///
/// <para><b>双重确认</b>：点左边某一行只是「选中」（行高亮 + 右边换成那一回合的预览），**不会**完成；
/// 必须再点右下角的确认按钮才完成 —— 那就是**复用原版** <c>res://scenes/ui/confirm_button.tscn</c> 的
/// <see cref="NConfirmButton"/>（<c>Enable()</c> 滑入 / <c>Disable()</c> 滑出，逻辑照抄原版选卡界面：
/// 没选中就禁用、选中了才亮）。再点一次已选中的行 = 取消选中。</para>
///
/// <para><b>及时提示</b>：每行立刻显示那一回合有哪些回忆（缩略图 + 张数 + 攻击/技能/能力统计）；
/// 鼠标悬停某行，右边**立刻**铺开那一回合的全部卡牌（松开鼠标后回到「已选中」那个回合的预览）。</para>
///
/// <para><b>宿主</b>：走 <see cref="NOverlayStack"/>（<see cref="IOverlayScreen"/>）而不是 capstone ——
/// 这与原版所有选卡界面完全一致（<c>CardSelectCmd</c> 全系列都这么弹），也是本 mod 里 <c>Poem</c>/<c>Ireland</c>
/// 等牌在 <c>OnPlay</c> 期间弹屏的既有路径；capstone 会 <c>CombatManager.Pause()</c>，不适合在出牌结算中间用。</para>
///
/// <para>可选回合 = 回忆里**确实有牌**的回合（含当前回合：记录发生在出牌结算之后，所以打出的这张牌自己
/// 还不在本回合的回忆里）。系统每回合开始会预创建「本回合 / 下一回合」的空桶，因此必须过滤掉空回合。</para>
///
/// <para><b>只认一部分牌</b>：可选的 <c>filter</c>（见 <see cref="Select"/>）把不该出现的回忆整批藏掉 ——
/// 藏掉之后张数归零的回合也不列出。界面据此承诺的一批牌，就是调用方随后真正会追忆的那一批。</para>
/// </summary>
internal sealed partial class MemoryRoundSelectScreen : Control, IOverlayScreen, IScreenContext
{
    // ---- 原版素材路径 ----
    private const string ScrollbarScenePath = "res://scenes/ui/scrollbar.tscn";
    private const string BackButtonScenePath = "res://scenes/ui/back_button.tscn";
    private const string ConfirmButtonScenePath = "res://scenes/ui/confirm_button.tscn";
    private const string TabBarTexturePath = "res://images/ui/color_tab_bar.png";
    private const string KreonRegularPath = "res://themes/kreon_regular_shared.tres";
    private const string KreonBoldPath = "res://themes/kreon_bold_shared.tres";

    // ---- 布局常量 ----
    private const float MarginLeft = 110f;
    private const float MarginRight = 110f;
    private const float MarginTop = 24f;
    private const float MarginBottom = 150f;   // 给底部两个按钮（返回 / 确认）留位
    private const float HeaderHeight = 64f;
    private const float PaneTop = MarginTop + HeaderHeight + 16f;

    /// <summary>左栏（回合列表）宽度。</summary>
    private const float LeftPaneWidth = 500f;
    private const float PaneGap = 28f;

    /// <summary>给右侧滚动条预留的宽度（列表内容右缩这么多，免得被条压住）。</summary>
    private const float ScrollbarGutter = 52f;

    private const float RowHeight = 84f;
    private const float RowGap = 12f;

    /// <summary>行内迷你卡面缩略图的尺寸（卡面比例 240 : 337.6，缩到高 48）。</summary>
    private const float ThumbHeight = 48f;
    private const float ThumbWidth = 34f;
    private const int MaxThumbs = 4;

    /// <summary>预览网格里卡与卡的间距（原版 <c>NCardGrid.CardPadding</c>）。</summary>
    private const float CardPadding = 40f;

    /// <summary>一张卡缩放后的占位尺寸（<c>NCard.defaultSize * NCardHolder.smallScale</c> = 0.8）。</summary>
    private static readonly Vector2 CardSize = NCard.defaultSize * NCardHolder.smallScale;

    private static readonly Color RowBg = new(0f, 0f, 0f, 0.35f);
    private static readonly Color RowBgHover = new(1f, 1f, 1f, 0.07f);
    private static readonly Color RowBgSelected = new(0.94f, 0.71f, 0f, 0.14f);

    // ---- 由调用方给定 ----
    private Player? _player;
    private TaskCompletionSource<int?> _result = null!;

    // ---- 节点 ----
    private Control _leftPane = null!;      // ScrollPane 的 Clip
    private Control _leftContent = null!;
    private ScrollPane _leftScroll = null!;
    private Control _previewPane = null!;   // ScrollPane 的 Clip
    private Control _previewContent = null!;
    private ScrollPane _previewScroll = null!;
    private Label _previewHeader = null!;
    private NConfirmButton _confirmButton = null!;
    private NBackButton _backButton = null!;

    // ---- 数据 ----
    private readonly List<Row> _rows = new();
    private readonly List<CardModel> _previewCards = new();
    private int _selected = -1;   // 已选中的回合（-1 = 没选）
    private int _hovered = -1;    // 鼠标正悬停的回合（-1 = 没悬停）
    private int _shownRound = -2; // 右侧当前正在展示的回合（-2 = 还没建过）
    private Func<CardModel, bool>? _filter; // 只认这些牌（null = 全认）；见 Select(...) 的说明
    private bool _closing;
    private bool _inspectOpen;
    private bool _previewDirty;
    private bool _needsPreviewRebuild;

    /// <summary>左栏里的一行 —— 回合 + 它的节点与样式（悬停/选中要就地改）。</summary>
    private sealed class Row
    {
        public int Round;
        public PanelContainer Root = null!;
        public StyleBoxFlat Style = null!;
        public Label Title = null!;
        public Label Stats = null!;
    }

    // ============ 打开 ============

    /// <summary>
    /// 弹出「选择一个回忆回合」界面，返回玩家选中的回合号；取消（返回键 / Esc）时返回 null。
    /// 只显示本地/该玩家确实有回忆的回合。
    ///
    /// <para><paramref name="filter"/> 决定**哪些牌算数**：被它筛掉的回忆既不显示（不算进张数/类型统计/缩略图/
    /// 右栏预览），也不参与「这个回合还算不算有回忆」的判断 —— 一整个回合的牌全被筛掉时，那一回合干脆不列出。
    /// 调用方拿它过滤「这个界面给得出什么」，而真正追忆时的筛选由调用方自己再传一次（两边用**同一个**谓词，
    /// 否则界面会承诺一些实际追忆不到、或漏掉一些实际会追忆到的牌）。传 null = 什么都不过滤。</para>
    /// </summary>
    public static Task<int?> Select(Player? player, Func<CardModel, bool>? filter = null)
    {
        TaskCompletionSource<int?> tcs = new();
        if (player == null || player.Creature?.CombatState == null || NOverlayStack.Instance == null)
        {
            tcs.SetResult(null);
            return tcs.Task;
        }
        MemoryRoundSelectScreen screen = new()
        {
            _player = player,
            _filter = filter,
            _result = tcs,
            Name = "MemoryRoundSelectScreen"
        };
        NOverlayStack.Instance.Push(screen);
        return tcs.Task;
    }

    // ============ IOverlayScreen / IScreenContext ============

    public NetScreenType ScreenType => NetScreenType.CardSelection;

    public bool UseSharedBackstop => true;

    // 手柄焦点：没选中回合之前不给锚点（确认键此刻是禁用的，把它当默认焦点会让焦点环卡在禁用控件上）；
    // 选中之后再把焦点交给确认键 —— 手柄玩家按 A 就是「第二次确认」。
    public Control? DefaultFocusedControl => _selected >= 1 ? _confirmButton : null;

    public Control? FocusedControlFromTopBar => _selected >= 1 ? _confirmButton : null;

    public void AfterOverlayOpened()
    {
        Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
    }

    public void AfterOverlayShown() => Visible = true;

    public void AfterOverlayHidden() => Visible = false;

    public void AfterOverlayClosed()
    {
        ClearPreview();
        Visible = false;
        this.QueueFreeSafely();
    }

    // ============ 生命周期 ============

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;

        BuildBackground();
        BuildHeader();
        BuildLeftPane();
        BuildPreviewPane();
        BuildConfirmButton();
        BuildBackButton();

        BuildRows();
        _needsPreviewRebuild = true;

        Resized += OnResized;
    }

    private void OnResized() => _needsPreviewRebuild = true;

    public override void _Process(double delta)
    {
        _leftScroll?.Tick(delta);
        _previewScroll?.Tick(delta);

        if (_previewDirty)
        {
            _previewDirty = false;
            RefreshPreview();
        }
        if (_needsPreviewRebuild)
        {
            _needsPreviewRebuild = false;
            LayoutPreview();
        }
    }

    /// <summary>
    /// ↑↓ 换回合。走 <c>_Input</c> 而不是 <c>_UnhandledInput</c>：<c>ui_up</c>/<c>ui_down</c> 同时是 Godot
    /// 内置的**焦点导航**动作，会被 GUI 焦点系统在更早的阶段吃掉，放 <c>_UnhandledInput</c> 里很可能收不到。
    /// 本屏是全屏模态，抢占这两个键没有副作用；处理完 <c>SetInputAsHandled()</c> 掐断后续导航。
    ///
    /// <para>游标语义与鼠标一致：**没选中时 ↑↓ 只改预览**（回车才选中），**已选中时 ↑↓ 直接改选**。
    /// 这样不会出现「游标在第 5 行、按下却是第 1 行确认」的割裂。</para>
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        // _Input 是全局的：被别的覆盖层盖住（Visible=false）或放大查看开着时必须闭嘴，
        // 否则玩家在看放大卡面时上下翻页，背后的回合选中会跟着乱跳。
        if (_closing || !Visible || _inspectOpen || _rows.Count == 0)
        {
            return;
        }
        int step = @event.IsActionPressed(MegaInput.up) ? -1
            : @event.IsActionPressed(MegaInput.down) ? 1 : 0;
        if (step == 0)
        {
            return;
        }
        MoveCursor(step);
        GetViewport()?.SetInputAsHandled();
    }

    /// <summary>Esc 取消。返回键本来也带 cancel 热键，这里再兜一层；两者都走 <see cref="Complete"/>，靠 <c>_closing</c> 幂等。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_closing || !Visible || _inspectOpen)
        {
            return;
        }
        if (@event.IsActionPressed(MegaInput.cancel))
        {
            Complete(null);
            GetViewport()?.SetInputAsHandled();
            return;
        }
        // 确认键被禁用时，回车用来「选中光标行」——按钮的热键此刻没生效，事件能落到这里，
        // 所以不会和按钮的确认动作重复触发。
        if (@event.IsActionPressed(MegaInput.confirm) && _selected < 1 && _hovered >= 1)
        {
            SelectRound(_hovered, scrollIntoView: false);
            GetViewport()?.SetInputAsHandled();
        }
    }

    private void MoveCursor(int step)
    {
        int index = Mathf.Clamp((IndexOfRound(_selected) ?? IndexOfRound(_hovered) ?? 0) + step, 0, _rows.Count - 1);
        int round = _rows[index].Round;
        _hovered = round;
        if (_selected >= 1)
        {
            _selected = round; // 已选中：方向键直接改选，不需要再点一次
            _confirmButton.Enable();
        }
        ApplyRowStyles();
        RefreshPreview();
        ScrollRowIntoView(round);
    }

    // ============ 组装 ============

    private void BuildBackground()
    {
        ColorRect background = new()
        {
            Name = "Background",
            Color = new Color(0f, 0f, 0f, 0.752941f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        this.AddChildSafely(background);
    }

    private void BuildHeader()
    {
        Control header = new()
        {
            Name = "Header",
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft = MarginLeft,
            OffsetTop = MarginTop,
            OffsetRight = -MarginRight,
            OffsetBottom = MarginTop + HeaderHeight,
        };
        this.AddChildSafely(header);

        TextureRect bg = new()
        {
            Name = "HeaderBg",
            Texture = PreloadManager.Cache.GetAsset<Texture2D>(TabBarTexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChildSafely(bg);

        Label title = new()
        {
            Name = "HeaderTitle",
            Text = LocOrDefault("REED-HERO_EPIC.select.title", "追忆一段过往"),
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 28f,
            OffsetTop = 0f,
            OffsetRight = -28f,
            OffsetBottom = 0f,
        };
        ApplyFont(title, bold: true, size: 30, StsColors.gold);
        header.AddChildSafely(title);
    }

    private void BuildLeftPane()
    {
        _leftScroll = new ScrollPane("RoundList");
        _leftPane = _leftScroll.Clip;
        _leftContent = _leftScroll.Content;
        _leftPane.AnchorLeft = 0f;
        _leftPane.AnchorRight = 0f;
        _leftPane.AnchorTop = 0f;
        _leftPane.AnchorBottom = 1f;
        _leftPane.OffsetLeft = MarginLeft;
        _leftPane.OffsetRight = MarginLeft + LeftPaneWidth;
        _leftPane.OffsetTop = PaneTop;
        _leftPane.OffsetBottom = -MarginBottom;
        this.AddChildSafely(_leftPane);
    }

    private void BuildPreviewPane()
    {
        _previewScroll = new ScrollPane("Preview");
        _previewPane = _previewScroll.Clip;
        _previewContent = _previewScroll.Content;
        _previewPane.AnchorLeft = 0f;
        _previewPane.AnchorRight = 1f;
        _previewPane.AnchorTop = 0f;
        _previewPane.AnchorBottom = 1f;
        _previewPane.OffsetLeft = MarginLeft + LeftPaneWidth + PaneGap;
        _previewPane.OffsetRight = -MarginRight;
        _previewPane.OffsetTop = PaneTop;
        _previewPane.OffsetBottom = -MarginBottom;
        this.AddChildSafely(_previewPane);

        _previewHeader = new Label
        {
            Name = "PreviewHeader",
            Text = "",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -ScrollbarGutter,
            OffsetTop = 0f,
            OffsetBottom = 44f,
        };
        ApplyFont(_previewHeader, bold: true, size: 24, StsColors.cream);
        _previewContent.AddChildSafely(_previewHeader);
    }

    private void BuildConfirmButton()
    {
        PackedScene scene = PreloadManager.Cache.GetScene(ConfirmButtonScenePath);
        _confirmButton = scene.Instantiate<NConfirmButton>(PackedScene.GenEditState.Disabled);
        _confirmButton.Name = "Confirm";
        // 必须直接挂全屏根节点下：NConfirmButton._Ready 用 NGame.Instance.Size - _posOffset 自己定位，
        // 前提是父节点铺满屏幕且原点在 (0, 0)。入树后它的 _Ready 已经把自己置为禁用（滑出屏幕）。
        this.AddChildSafely(_confirmButton);
        _confirmButton.Disable();
        _confirmButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Complete(_selected >= 1 ? _selected : null)));
    }

    private void BuildBackButton()
    {
        PackedScene scene = PreloadManager.Cache.GetScene(BackButtonScenePath);
        _backButton = scene.Instantiate<NBackButton>(PackedScene.GenEditState.Disabled);
        _backButton.Name = "BackButton";
        // 位置沿用场景自带锚点（左下角，与原版 CardPileScreen 的 BackButton 一致）。
        _backButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Complete(null)));
        this.AddChildSafely(_backButton);
        _backButton.Enable();
    }

    // ============ 左栏：回合行 ============

    private void BuildRows()
    {
        PlayerMemory? memory = MemoryOf();
        if (memory == null)
        {
            return;
        }

        // 先把每一回合**过滤后**的回忆算出来：同一个谓词要同时决定「这一回合列不列」和「这一回合里有什么」，
        // 分开算两遍容易算出不一致的结果。只列过滤后仍有牌的回合（原本就空、或全被筛掉的都不列）。
        List<(int Round, List<RememberedCard> Entries)> rounds = new();
        foreach (int r in memory.Rounds)
        {
            List<RememberedCard> entries = FilteredEntries(r);
            if (entries.Count > 0)
            {
                rounds.Add((r, entries));
            }
        }
        // 新 → 旧：最近打过的回合在最上面。
        rounds.Sort((a, b) => b.Round.CompareTo(a.Round));

        if (rounds.Count == 0)
        {
            Label empty = MakeLabel(LocOrDefault("REED-HERO_EPIC.select.empty", "没有可以重现的回忆。"), 24, new Color(StsColors.cream, 0.6f), bold: false);
            empty.Position = new Vector2(8f, 8f);
            empty.Size = new Vector2(LeftPaneWidth - ScrollbarGutter - 16f, 60f);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _leftContent.AddChildSafely(empty);
            _leftScroll.SetContentHeight(76f);
            return;
        }

        for (int i = 0; i < rounds.Count; i++)
        {
            _rows.Add(BuildRow(rounds[i].Round, rounds[i].Entries, i));
        }
        _leftScroll.SetContentHeight(_rows.Count * RowHeight + (_rows.Count - 1) * RowGap + 4f);

        // 只有一个可选的回合：先替玩家选上（确认键随之亮起），但**不**替他确认 —— 双重确认这一层不动。
        if (_rows.Count == 1)
        {
            SelectRound(_rows[0].Round, scrollIntoView: false);
        }
    }

    /// <summary>
    /// 第 <paramref name="round"/> 回合里**这个界面认**的回忆：<see cref="_filter"/> 说不算的牌直接不出现，
    /// 于是它不进张数、不进类型统计、不出缩略图、不进右栏预览 —— 界面展示的与实际会追忆的始终是同一批牌。
    /// 用 <see cref="PlayerMemory.PeekRound"/>（只读不建桶），没这个回合就是空列表。
    /// </summary>
    private List<RememberedCard> FilteredEntries(int round)
    {
        PlayerMemory? memory = MemoryOf();
        if (memory == null || round < 1)
        {
            return new List<RememberedCard>();
        }
        return memory.PeekRound(round)
            .Where(e => e.Card != null && (_filter == null || _filter(e.Card)))
            .ToList();
    }

    private Row BuildRow(int round, List<RememberedCard> entries, int index)
    {
        StyleBoxFlat style = new()
        {
            BgColor = RowBg,
            BorderColor = new Color(StsColors.gold, 0.18f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 14f,
            ContentMarginRight = 14f,
            ContentMarginTop = 8f,
            ContentMarginBottom = 8f,
        };

        PanelContainer root = new()
        {
            Name = $"Round{round}",
            // 必须是 Pass 而不是 Stop：Stop 会把事件**截断在这一层**，父节点（负责滚轮的那个 Clip）
            // 就再也收不到滚轮了 —— 鼠标停在某一行上时列表就滚不动。Pass 让行先处理点击/悬停，
            // 再把未消费的事件继续往上交给 Clip。
            MouseFilter = MouseFilterEnum.Pass,
        };
        root.AddThemeStyleboxOverride("panel", style);
        root.AnchorLeft = 0f;
        root.AnchorRight = 1f;
        root.AnchorTop = 0f;
        root.AnchorBottom = 0f;
        root.OffsetLeft = 0f;
        root.OffsetRight = -ScrollbarGutter;
        root.OffsetTop = index * (RowHeight + RowGap);
        root.OffsetBottom = index * (RowHeight + RowGap) + RowHeight;

        HBoxContainer line = new()
        {
            Name = "Line",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.AddThemeConstantOverride("separation", 10);
        root.AddChildSafely(line);

        VBoxContainer text = new()
        {
            Name = "Text",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        text.AddThemeConstantOverride("separation", 2);
        line.AddChildSafely(text);

        Label title = MakeLabel(RoundHeaderText(round), 26, StsColors.cream, bold: true);
        title.MouseFilter = MouseFilterEnum.Ignore;
        // ClipText：标签的最小宽度不再跟文字走，于是长文案也不会把整行撑宽（撑宽会被裁剪列截掉缩略图）。
        title.ClipText = true;
        text.AddChildSafely(title);

        Label stats = MakeLabel($"{CountText(entries.Count)} · {TypeSummary(entries)}", 17, new Color(StsColors.cream, 0.62f), bold: false);
        stats.MouseFilter = MouseFilterEnum.Ignore;
        stats.ClipText = true;
        text.AddChildSafely(stats);

        HBoxContainer thumbs = new()
        {
            Name = "Thumbs",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        thumbs.AddThemeConstantOverride("separation", 4);
        line.AddChildSafely(thumbs);
        FillThumbs(thumbs, entries);

        Row row = new() { Round = round, Root = root, Style = style, Title = title, Stats = stats };
        int captured = round;
        root.MouseEntered += () => OnRowHover(captured, true);
        root.MouseExited += () => OnRowHover(captured, false);
        root.GuiInput += e => OnRowInput(captured, e);
        _leftContent.AddChildSafely(root);
        return row;
    }

    /// <summary>一行里最多摆 <see cref="MaxThumbs"/> 张卡面缩略图，多出来的用「+N」表示。</summary>
    private static void FillThumbs(HBoxContainer thumbs, List<RememberedCard> entries)
    {
        int shown = Mathf.Min(MaxThumbs, entries.Count);
        for (int i = 0; i < shown; i++)
        {
            Texture2D? portrait = entries[i].Card?.Portrait;
            if (portrait == null)
            {
                continue;
            }
            TextureRect rect = new()
            {
                Name = $"Thumb{i}",
                Texture = portrait,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(ThumbWidth, ThumbHeight),
            };
            thumbs.AddChildSafely(rect);
        }
        if (entries.Count > MaxThumbs)
        {
            Label more = MakeLabel($"+{entries.Count - MaxThumbs}", 18, new Color(StsColors.gold, 0.85f), bold: true);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.MouseFilter = MouseFilterEnum.Ignore;
            thumbs.AddChildSafely(more);
        }
    }

    // ============ 选中 / 悬停 ============

    private void OnRowInput(int round, InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            SelectRound(round, scrollIntoView: false);
            _leftPane.AcceptEvent();
        }
    }

    private void OnRowHover(int round, bool entered)
    {
        _hovered = entered ? round : (_hovered == round ? -1 : _hovered);
        // 不立刻重建：鼠标从 A 行滑到 B 行会先 Exit(A) 再 Enter(B)，同一帧里重建两次白费一趟
        // （每次都要造/回收一整屏卡牌节点）。交给 _Process 合并，一帧最多重建一次。
        _previewDirty = true;
    }

    /// <summary>选中一个回合（再点一次同一个 = 取消选中）。选中只改高亮与预览，**不**完成。</summary>
    private void SelectRound(int round, bool scrollIntoView)
    {
        _selected = _selected == round ? -1 : round;
        ApplyRowStyles();
        RefreshPreview();

        if (_selected >= 1)
        {
            _confirmButton.Enable();
            if (scrollIntoView)
            {
                ScrollRowIntoView(_selected);
            }
        }
        else
        {
            _confirmButton.Disable();
        }
    }

    private void ApplyRowStyles()
    {
        foreach (Row row in _rows)
        {
            bool selected = row.Round == _selected;
            bool hovered = row.Round == _hovered;
            row.Style.BgColor = selected ? RowBgSelected : hovered ? RowBgHover : RowBg;
            row.Style.BorderColor = selected
                ? StsColors.gold
                : new Color(StsColors.gold, hovered ? 0.5f : 0.18f);
            row.Style.BorderWidthLeft = selected ? 3 : 2;
            row.Style.BorderWidthTop = selected ? 3 : 2;
            row.Style.BorderWidthRight = selected ? 3 : 2;
            row.Style.BorderWidthBottom = selected ? 3 : 2;
            row.Title.AddThemeColorOverride("font_color", selected ? StsColors.gold : StsColors.cream);
        }
    }

    private void ScrollRowIntoView(int round)
    {
        int? index = IndexOfRound(round);
        if (index == null)
        {
            return;
        }
        float top = index.Value * (RowHeight + RowGap);
        _leftScroll.ScrollIntoView(top, RowHeight);
    }

    private int? IndexOfRound(int round)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Round == round)
            {
                return i;
            }
        }
        return null;
    }

    // ============ 右栏：预览 ============

    /// <summary>要展示的回合：优先鼠标悬停的那行（「及时提示」），否则已选中的那行，都没有就不展示。</summary>
    private int PreviewRound => _hovered >= 1 ? _hovered : _selected;

    private void RefreshPreview()
    {
        if (PreviewRound == _shownRound)
        {
            return;
        }
        LayoutPreview();
    }

    private void LayoutPreview()
    {
        int round = PreviewRound;
        ClearPreview(); // 注意：它会把 _shownRound 归零，必须在下面那行**之前**调用
        _shownRound = round;
        _previewScroll.ResetScroll();

        // 与左栏同源：只展示 _filter 认的牌，张数（表头「共 N 张」）也跟着一起变。
        List<RememberedCard> entries = FilteredEntries(round);

        if (entries.Count == 0)
        {
            _previewHeader.Text = "";
            Label hint = MakeLabel(
                LocOrDefault("REED-HERO_EPIC.select.preview.empty", "把鼠标移到左边的回合上，这里会显示那一回合的全部回忆。"),
                22,
                new Color(StsColors.cream, 0.45f),
                bold: false);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.Position = new Vector2(4f, 60f);
            hint.Size = new Vector2(Mathf.Max(200f, _previewPane.Size.X - ScrollbarGutter - 8f), 120f);
            _previewContent.AddChildSafely(hint);
            _previewScroll.SetContentHeight(190f);
            return;
        }

        _previewHeader.Text = PreviewHeaderText(round, entries.Count);

        float width = Mathf.Max(240f, _previewPane.Size.X - ScrollbarGutter);
        int cols = Mathf.Max(1, (int)((width + CardPadding) / (CardSize.X + CardPadding)));
        int rows = (entries.Count + cols - 1) / cols;
        // 60 表头 + 40 上边距 + 各行 + 末尾给「第 N 张」序号留 48（序号挂在卡下方，不留就会被裁掉）。
        float contentHeight = 100f + rows * CardSize.Y + Mathf.Max(0, rows - 1) * CardPadding + 48f;

        for (int i = 0; i < entries.Count; i++)
        {
            int col = i % cols;
            int rowIndex = i / cols;
            Vector2 center = new(
                CardPadding + CardSize.X * 0.5f + col * (CardSize.X + CardPadding),
                60f + CardPadding + CardSize.Y * 0.5f + rowIndex * (CardSize.Y + CardPadding));
            CreatePreviewCard(entries[i].Card, center, i + 1);
        }

        _previewScroll.SetContentHeight(contentHeight);
    }

    private void CreatePreviewCard(CardModel? model, Vector2 center, int order)
    {
        if (model == null)
        {
            return;
        }
        NCard? card = NCard.Create(model, ModelVisibility.Visible);
        if (card == null)
        {
            return;
        }
        NGridCardHolder? holder = NGridCardHolder.Create(card);
        if (holder == null)
        {
            return;
        }
        holder.Name = $"PreviewCard{_previewCards.Count}";
        holder.Position = center; // holder 与卡一样：Position 是卡牌中心
        holder.MouseFilter = MouseFilterEnum.Pass;
        _previewContent.AddChildSafely(holder);
        holder.Connect(NCardHolder.SignalName.Pressed, Callable.From<NCardHolder>(OnPreviewCardPressed));
        holder.Connect(NCardHolder.SignalName.AltPressed, Callable.From<NCardHolder>(OnPreviewCardPressed));
        // 必须在入树之后：NCard.UpdateVisuals 内部有 IsNodeReady() 前置判断。
        card.UpdateVisuals(ReedPileType.Memory, CardPreviewMode.Normal);
        _previewCards.Add(model);

        // 卡下方的「第 N 张」序号：告诉玩家重现顺序（左上到右下的阅读顺序 = 打出顺序）。
        Label badge = MakeLabel(order.ToString(), 20, new Color(StsColors.gold, 0.9f), bold: true);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.MouseFilter = MouseFilterEnum.Ignore;
        badge.Position = new Vector2(center.X - 20f, center.Y + CardSize.Y * 0.5f + 4f);
        badge.Size = new Vector2(40f, 28f);
        _previewContent.AddChildSafely(badge);
    }

    private void OnPreviewCardPressed(NCardHolder holder)
    {
        CardModel? model = holder.CardModel;
        if (model == null || _previewCards.Count == 0 || NGame.Instance is not { } game)
        {
            return;
        }
        // 与 NCardGridSelectionScreen.ShowCardDetail 同款判断：手柄方向导航时不弹放大查看，
        // 免得和焦点移动抢输入。
        if (NControllerManager.Instance.IsUsingDirectionalNavigation)
        {
            return;
        }
        int index = _previewCards.IndexOf(model);
        if (index < 0)
        {
            return;
        }
        _inspectOpen = true;
        _backButton.Disable();
        _confirmButton.Disable(); // 放大查看期间别让确认键还浮在那儿被误点（关掉后按当前状态恢复）
        NInspectCardScreen inspect = game.GetInspectCardScreen();
        inspect.Open(_previewCards.ToList(), index, false);
        // 与回忆牌堆界面同一套写法：一次性回调，关闭放大查看后把返回键亮回来。
        Callable callable = Callable.From(OnInspectHidden);
        if (inspect.IsConnected(CanvasItem.SignalName.VisibilityChanged, callable))
        {
            inspect.Disconnect(CanvasItem.SignalName.VisibilityChanged, callable);
        }
        inspect.Connect(CanvasItem.SignalName.VisibilityChanged, callable, (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void OnInspectHidden()
    {
        _inspectOpen = false;
        if (_backButton != null && GodotObject.IsInstanceValid(_backButton))
        {
            _backButton.Enable();
        }
        // 确认键只在「确实选中了某个回合」时才收回来的位置上；取消选中过就不该再亮。
        if (_confirmButton != null && GodotObject.IsInstanceValid(_confirmButton) && _selected >= 1)
        {
            _confirmButton.Enable();
        }
    }

    private void ClearPreview()
    {
        foreach (Node child in _previewContent.GetChildren())
        {
            if (child == _previewHeader)
            {
                continue; // 表头是常驻节点，只清卡与序号
            }
            _previewContent.RemoveChildSafely(child);
            child.QueueFreeSafely();
        }
        _previewCards.Clear();
        _shownRound = -2;
    }

    // ============ 完成 ============

    private void Complete(int? round)
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        _result.TrySetResult(round);
        if (NOverlayStack.Instance is { } stack)
        {
            stack.Remove(this);
        }
        else
        {
            this.QueueFreeSafely();
        }
    }

    public override void _ExitTree()
    {
        Resized -= OnResized;
        // 兜底：界面被外力拆掉（战斗结束、覆盖层清空……）时也要让 await 的那一方醒过来 ——
        // 否则「英雄史诗」的 OnPlay 会永远挂在这里。已完成时 TrySetResult 是空操作。
        _result?.TrySetResult(null);
    }

    // ============ 数据 / 文案 ============

    private PlayerMemory? MemoryOf()
    {
        Player? player = _player;
        if (player?.Creature?.CombatState is not { } combat)
        {
            return null;
        }
        return MemorySystem.Of(combat, player);
    }

    private static string PreviewHeaderText(int round, int count)
    {
        LocString loc = new("static_hover_tips", "REED-HERO_EPIC.select.preview.header");
        loc.Add("Round", round);
        loc.Add("Count", count);
        return FormatLoc(loc, $"第 {round} 回合 · 共 {count} 张 · 按打出顺序重现");
    }

    private static string RoundHeaderText(int round)
    {
        LocString loc = new("static_hover_tips", "REED-RECALL.round.header");
        loc.Add("Round", round);
        return FormatLoc(loc, $"第 {round} 回合");
    }

    private static string CountText(int count)
    {
        LocString loc = new("static_hover_tips", "REED-HERO_EPIC.row.count");
        loc.Add("Count", count);
        return FormatLoc(loc, $"{count} 张");
    }

    /// <summary>「攻击 2 · 技能 1 · 能力 1」—— 一行说清这个回合里都是些什么牌。</summary>
    private static string TypeSummary(List<RememberedCard> entries)
    {
        int attack = 0, skill = 0, power = 0;
        foreach (RememberedCard entry in entries)
        {
            switch (entry.Card?.Type)
            {
                case CardType.Attack: attack++; break;
                case CardType.Skill: skill++; break;
                case CardType.Power: power++; break;
            }
        }

        List<string> parts = new();
        if (attack > 0)
        {
            parts.Add($"{LocOrDefault("REED-HERO_EPIC.type.attack", "攻击")} {attack}");
        }
        if (skill > 0)
        {
            parts.Add($"{LocOrDefault("REED-HERO_EPIC.type.skill", "技能")} {skill}");
        }
        if (power > 0)
        {
            parts.Add($"{LocOrDefault("REED-HERO_EPIC.type.power", "能力")} {power}");
        }
        return string.Join(" · ", parts);
    }

    private static Label MakeLabel(string text, int size, Color color, bool bold)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ApplyFont(label, bold, size, color);
        return label;
    }

    private static void ApplyFont(Label label, bool bold, int size, Color color)
    {
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(bold ? KreonBoldPath : KreonRegularPath));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }

    private static string LocOrDefault(string key, string fallback)
    {
        try
        {
            string text = new LocString("static_hover_tips", key).GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MemoryRoundSelectScreen] 本地化文案缺失（{e.Message}），已用兜底文本。");
            return fallback;
        }
    }

    private static string FormatLoc(LocString loc, string fallback)
    {
        try
        {
            string text = loc.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[MemoryRoundSelectScreen] 本地化文案缺失（{e.Message}），已用兜底文本。");
            return fallback;
        }
    }

    // ============ 滚动容器 ============

    /// <summary>
    /// 一个「裁剪窗口 + 可滚动内容 + 原版滚动条」的小封装（两栏各一个）。
    /// 内容坐标里自己摆子节点，<see cref="Content"/> 的 Y 就是滚动位置；滚轮直接推目标值，
    /// 每帧向目标缓动 —— 与回忆牌堆界面同一套手感。
    /// </summary>
    private sealed class ScrollPane
    {
        private const float WheelStep = 120f;
        private const float LerpSpeed = 15f;

        public readonly Control Clip;
        public readonly Control Content;

        private readonly NScrollbar _bar;
        private float _scroll;
        private float _target;
        private float _contentHeight;
        private bool _barPressed;

        public ScrollPane(string name)
        {
            Clip = new Control
            {
                Name = name + "Clip",
                ClipContents = true,
                MouseFilter = MouseFilterEnum.Stop,
            };
            Content = new Control
            {
                Name = name + "Content",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            Content.Position = Vector2.Zero;
            Clip.AddChildSafely(Content);
            Clip.GuiInput += OnWheel;

            PackedScene scene = PreloadManager.Cache.GetScene(ScrollbarScenePath);
            _bar = scene.Instantiate<NScrollbar>(PackedScene.GenEditState.Disabled);
            _bar.Name = name + "Scrollbar";
            _bar.AnchorLeft = 1f;
            _bar.AnchorRight = 1f;
            _bar.AnchorTop = 0f;
            _bar.AnchorBottom = 1f;
            _bar.OffsetLeft = -46f;
            _bar.OffsetRight = -6f;
            _bar.OffsetTop = 0f;
            _bar.OffsetBottom = 0f;
            _bar.MinValue = 0.0;
            _bar.MaxValue = 100.0;
            _bar.Step = 0.01;
            _bar.Page = 0.0;
            _bar.Value = 0.0;
            _bar.Visible = false;
            Clip.AddChildSafely(_bar);

            _bar.Connect(NScrollbar.SignalName.MousePressed, Callable.From<InputEvent>(_ => _barPressed = true));
            _bar.Connect(NScrollbar.SignalName.MouseReleased, Callable.From<InputEvent>(_ => _barPressed = false));
            _bar.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnBarValueChanged));
        }

        /// <summary>内容的实际高度（用来判断是否需要滚动、以及算滚动上限）。</summary>
        public void SetContentHeight(float height)
        {
            _contentHeight = height;
            SyncContentSize();
            ClampScroll();
            SyncBar();
        }

        public void ResetScroll()
        {
            _scroll = 0f;
            _target = 0f;
            Content.Position = Vector2.Zero;
            SyncBar();
        }

        /// <summary>
        /// 内容节点自己的尺寸必须跟着裁剪窗口走：<see cref="Content"/> 是**普通 Control**（不是 Container），
        /// 尺寸为 0 的话，子节点里那些 <c>AnchorRight = 1</c>（比如每行、右侧表头）会以宽度 0 为基准算出负宽度，
        /// 整列就没了。宽 = 窗口宽；高取 max(窗口高, 内容高)，让超出部分照常绘制、由 ClipContents 裁掉。
        /// </summary>
        private void SyncContentSize()
        {
            Content.Size = new Vector2(Clip.Size.X, Mathf.Max(Clip.Size.Y, _contentHeight));
        }

        /// <summary>把某个「内容坐标里的矩形」滚进可视区（键盘切回合时用）。</summary>
        public void ScrollIntoView(float top, float height)
        {
            float bottom = top + height;
            if (top < -_target)
            {
                _target = -top;
            }
            else if (bottom > -_target + Clip.Size.Y)
            {
                _target = Clip.Size.Y - bottom;
            }
            ClampScroll();
        }

        public void Tick(double delta)
        {
            float limit = ScrollLimit;
            if (_barPressed)
            {
                _target = Mathf.Clamp(_target, limit, 0f);
            }
            if (Mathf.Abs(_scroll - _target) > 0.1f)
            {
                _scroll = Mathf.Lerp(_scroll, _target, Mathf.Clamp((float)delta * LerpSpeed, 0f, 1f));
                if (Mathf.Abs(_scroll - _target) < 0.5f)
                {
                    _scroll = _target;
                }
                Content.Position = new Vector2(0f, _scroll);
            }
            SyncContentSize(); // 窗口尺寸随窗口变化，每帧对齐一次即可（几十个节点，开销可忽略）
            SyncBar();
        }

        private float ScrollLimit => Mathf.Min(0f, Clip.Size.Y - _contentHeight);

        private void ClampScroll()
        {
            float limit = ScrollLimit;
            _target = Mathf.Clamp(_target, limit, 0f);
            _scroll = Mathf.Clamp(_scroll, limit, 0f);
            Content.Position = new Vector2(0f, _scroll);
        }

        private void OnWheel(InputEvent e)
        {
            if (e is not InputEventMouseButton mb || !mb.Pressed)
            {
                return;
            }
            float delta = mb.ButtonIndex switch
            {
                MouseButton.WheelUp => WheelStep,
                MouseButton.WheelDown => -WheelStep,
                _ => 0f,
            };
            if (delta == 0f)
            {
                return;
            }
            _target = Mathf.Clamp(_target + delta, ScrollLimit, 0f);
            Clip.AcceptEvent();
        }

        private void OnBarValueChanged(double value)
        {
            if (!_barPressed)
            {
                return; // 我们自己 SetValueWithoutAnimation 造成的回环，忽略
            }
            _target = ScrollLimit * (float)(value / 100.0);
        }

        private void SyncBar()
        {
            float limit = ScrollLimit;
            bool overflow = limit < -0.5f;
            _bar.Visible = overflow;
            if (overflow && !_barPressed)
            {
                _bar.SetValueWithoutAnimation(Mathf.Clamp(_scroll / limit, 0f, 1f) * 100f);
            }
        }
    }
}
