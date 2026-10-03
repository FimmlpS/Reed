using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Assets;
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
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「回忆牌堆」查看界面：像抽牌堆/弃牌堆一样打开，但回忆是**按回合数聚合**的，
/// 所以没有复用原版的 <c>NCardPileScreen</c>（它源码里明说“别拿它做别的事”）或 <c>NCardsViewScreen</c>
/// （它的 <c>_Ready</c> 会 throw，且强绑定扁平 <c>NCardGrid</c>，塞不进回合标题）。
///
/// <para><b>布局与手感全部照抄原版 deck（<c>deck_view_screen.tscn</c>）：</b>
/// 卡牌不是自己摆 <see cref="NCard"/>，而是走 <see cref="NGridCardHolder"/>——
/// 和 <c>NCardGrid</c> 里 <c>InitGrid</c> 的做法一字不差（<c>NCard.Create</c> → <c>NGridCardHolder.Create</c>
/// → 入树 → <c>UpdateVisuals(PileType, CardPreviewMode.Normal)</c>）。
/// 这样「悬停放大到 1.0、移开 0.5s 缓动回 0.8、悬停 tip」全部由 <see cref="NCardHolder"/> 自带
/// （<c>DoCardHoverEffects</c> / <c>NHoverTipSet</c>），尺寸也与牌堆界面同为 0.8（240 × 337.6）。</para>
///
/// <list type="bullet">
///   <item>背景：黑色 75% <see cref="ColorRect"/>（同 <c>card_pile_screen.tscn</c>）；</item>
///   <item>顶部一行 header：<c>images/ui/color_tab_bar.png</c> 色带 + 标题 + 排序按钮
///     （<c>deck_view_sort_button.tscn</c> 的 <see cref="NCardViewSortButton"/>，与 deck 同一个场景）；</item>
///   <item>返回键：<c>res://scenes/ui/back_button.tscn</c>；</item>
///   <item>滚动条：<c>res://scenes/ui/scrollbar.tscn</c>，左邻一列**固定不动**的回合刻度，可点击跳转；</item>
///   <item>点击卡牌放大查看：走原版 <c>NGame.GetInspectCardScreen().Open(...)</c>（与 deck 的
///     <c>NCardsViewScreen.ShowCardDetail</c> 同一 API，含左右翻页）；</item>
///   <item>字体：<c>res://themes/kreon_*_shared.tres</c>。</item>
/// </list>
///
/// <para><b>摆放坐标：<see cref="NCard"/> 与 <see cref="NCardHolder"/> 的 <c>Position</c> 都是「卡牌中心」</b>
/// （<c>card.tscn</c> 的视觉节点绕原点居中，<c>grid_card_holder.tscn</c> 的 <c>%Hitbox</c> 也是
/// -150/-211…150/211 居中），所以一律按左上角算格子、摆之前加半个卡身。</para>
///
/// <para>回合按**新 → 旧**自上而下排列（排序按钮可切回旧 → 新）；所有「已经建出来的回合」（含空回合）
/// 都会出现；打开时自动滚到当前回合。</para>
///
/// 打开方式：<c>NCapstoneContainer.Instance.Open(MemoryPileScreen.Create(player))</c>。
/// </summary>
internal sealed partial class MemoryPileScreen : Control, ICapstoneScreen, IScreenContext
{
    // ---- 原版素材路径 ----
    private const string ScrollbarScenePath = "res://scenes/ui/scrollbar.tscn";
    private const string BackButtonScenePath = "res://scenes/ui/back_button.tscn";
    private const string SortButtonScenePath = "res://scenes/screens/deck_view_screen/deck_view_sort_button.tscn";
    private const string TabBarTexturePath = "res://images/ui/color_tab_bar.png";
    private const string KreonRegularPath = "res://themes/kreon_regular_shared.tres";
    private const string KreonBoldPath = "res://themes/kreon_bold_shared.tres";

    // ---- 布局常量 ----
    private const float ViewLeft = 150f;       // 内容区左内缩（同 card_grid.tscn）
    /// <summary>
    /// 右侧留给「刻度列 + 滚动条」的宽度。**必须容得下刻度的整条标签**（「第 12 回合」约 130px）：
    /// 标签是画在刻度列里的，若列比标签窄，文字就会溢到卡牌区上 —— 那些溢出去的字**点不到**
    /// （拾取只认控件自己的矩形），看着就像「回合按钮点不动」。
    /// </summary>
    private const float ViewRight = 330f;
    private const float ViewBottom = 130f;
    private const float HeaderTop = 20f;       // header 色带的上边距
    private const float HeaderHeight = 64f;
    private const float ViewTop = HeaderTop + HeaderHeight + 20f; // 内容从 header 下方开始
    private const float ScrollbarLeft = -100f; // 右对齐下的偏移（同 card_grid.tscn）
    private const float ScrollbarRight = -50f;
    private const float TickColumnLeft = -310f;  // 刻度列宽度：刻度线（14）+ 箭头空隙（18）+ 最长的「第 NN 回合」标签
    private const float TickColumnRight = -112f;
    private const float SortButtonWidth = 250f;  // 与 deck 的排序按钮同尺寸
    private const float SortButtonHeight = 42f;

    private const float SectionGap = 54f;      // 回合区块间距
    private const float HeaderHeightInSection = 56f; // 回合标题行高
    private const float ContentPaddingTop = 24f;

    /// <summary>
    /// 内容末尾的留白：底部说明条是**悬浮**在内容之上的（同原版），所以必须留够高度，
    /// 让滚到底时最后一行卡牌停在说明条上方、而不是被它压住。
    /// </summary>
    private const float ContentPaddingBottom = 130f;
    private const float EmptyHintHeight = 60f;   // 空回忆提示语的行高
    private const float EmptyRoundHeight = 40f;  // 空回合区块（只有标题）的高度

    /// <summary>卡与卡之间的间距：与 <c>NCardGrid.CardPadding</c> 一致，观感才和 deck 一样。</summary>
    private const float CardPadding = 40f;

    /// <summary>底部说明条的兜底文案（本地化 key 没进 pck 时用；格式同原版三个牌堆的 INFO）。</summary>
    private const string DefaultInfoText =
        "回忆：本场战斗中你[gold]手动打出[/gold]过的牌，按回合归档。\n"
        + "[gold]（点击卡牌可放大查看，点右侧刻度可跳到该回合）[/gold]";

    /// <summary>一张卡缩放后的占位尺寸（<c>NCard.defaultSize * NCardHolder.smallScale</c>）。</summary>
    private static readonly Vector2 CardSize = NCard.defaultSize * NCardHolder.smallScale;

    private readonly struct Section
    {
        public Section(int round, float y, int count, Label? header = null, ColorRect? line = null, bool empty = false)
        {
            Round = round;
            Y = y;
            Count = count;
            Header = header;
            Line = line;
            Empty = empty;
        }

        public int Round { get; }
        public float Y { get; }      // 内容坐标系里的区块顶部
        public int Count { get; }

        /// <summary>区块标题「第 N 回合」；「正在追忆哪一回合」变化时要就地重刷它的颜色，所以留个引用。</summary>
        public Label? Header { get; }

        /// <summary>标题下的分隔线（空回合没有）。</summary>
        public ColorRect? Line { get; }

        public bool Empty { get; }
    }

    // ---- 节点 ----
    private Control _header = null!;
    private Control _viewport = null!;
    private Control _content = null!;
    private NScrollbar _scrollbar = null!;
    private MemoryRoundTicks _ticks = null!;
    private NBackButton _backButton = null!;
    private NCardViewSortButton _sortButton = null!;

    // ---- 数据 ----
    private Player? _player;
    private readonly List<CardModel> _cards = new();               // 展示顺序的扁平卡表（放大查看用）
    private readonly List<Section> _sections = new();
    private float _contentHeight;
    private int _currentRound;

    /// <summary>排序方向：true = 时间逆序（新 → 旧，默认，也是打开时的观感）。</summary>
    private bool _sortDescending = true;

    /// <summary>已经写进排序按钮的方向（按钮要等它自己 <c>_Ready</c> 后才能设，所以要记一份）。</summary>
    private bool _sortApplied;
    private bool _appliedSortDescending;

    /// <summary>布局尚未落定（首次 <c>_Ready</c> 里子节点还是「延迟入树」状态），<see cref="_Process"/> 里每帧重试。</summary>
    private bool _needsRebuild;

    /// <summary>打开界面后要自动滚到当前回合；等布局尺寸落定后再执行。</summary>
    private bool _pendingJumpToCurrent;

    // ---- 滚动状态 ----
    private float _scroll;
    private float _targetScroll;
    private bool _dragging;
    private float _dragStartMouseY;
    private float _dragStartScroll;
    private float _dragDistance;
    private bool _scrollbarPressed;

    /// <summary>刻度点按时用来平滑滚动的强度（越大越快）。</summary>
    private const float ScrollLerpSpeed = 15f;

    // ============ 打开 ============

    /// <summary>为某位玩家造一个回忆牌堆界面（尚未入树）。</summary>
    public static MemoryPileScreen Create(Player? player)
    {
        return new MemoryPileScreen { _player = player, Name = "MemoryPileScreen" };
    }

    // ============ ICapstoneScreen / IScreenContext ============

    public NetScreenType ScreenType => NetScreenType.SimpleCardsView;

    public bool UseSharedBackstop => true;

    public Control? DefaultFocusedControl => _backButton;

    public Control? FocusedControlFromTopBar => _backButton;

    // ============ 生命周期 ============

    public override void _Ready()
    {
        // 全屏：根节点铺满 <c>NCapstoneContainer</c>（它自己不设尺寸，一切由屏幕负责）。
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
        BuildViewport();
        BuildScrollbar();
        BuildTicks();
        BuildBackButton();
        BuildBottomTip();

        MemorySystem.MemoryChanged += OnMemoryChanged;
        RecallFocus.Changed += OnRecallFocusChanged;

        Resized += Rebuild;
        _viewport.Resized += Rebuild;
        _pendingJumpToCurrent = true;
        Rebuild();
    }

    public override void _ExitTree()
    {
        MemorySystem.MemoryChanged -= OnMemoryChanged;
        RecallFocus.Changed -= OnRecallFocusChanged;
    }

    public void AfterCapstoneOpened()
    {
        Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        _pendingJumpToCurrent = true;
        Rebuild();
    }

    public void AfterCapstoneClosed()
    {
        // 先把手上的 holder/card 交还对象池（它们都实现 IPoolable），再整屏释放。
        ClearContent();
        Visible = false;
        this.QueueFreeSafely();
    }

    // ============ 组装 ============

    private void BuildBackground()
    {
        ColorRect background = new ColorRect
        {
            Name = "Background",
            Color = new Color(0f, 0f, 0f, 0.752941f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        this.AddChildSafely(background);
    }

    /// <summary>顶部一行 header：色带 + 标题 + 排序按钮（结构照抄 deck 的 <c>SortingOptions</c>）。</summary>
    private void BuildHeader()
    {
        _header = new Control
        {
            Name = "Header",
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft = ViewLeft,
            OffsetTop = HeaderTop,
            OffsetRight = -ViewRight,
            OffsetBottom = HeaderTop + HeaderHeight,
        };
        this.AddChildSafely(_header);

        TextureRect bg = new TextureRect
        {
            Name = "HeaderBg",
            Texture = PreloadManager.Cache.GetAsset<Texture2D>(TabBarTexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _header.AddChildSafely(bg);

        Label title = new Label
        {
            Name = "HeaderTitle",
            Text = LocOrDefault("REED-RECALL.pile.title", "回忆"),
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 28f,
            OffsetTop = 0f,
            OffsetRight = -(SortButtonWidth + 40f),
            OffsetBottom = 0f,
        };
        title.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(KreonBoldPath));
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", StsColors.gold);
        title.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
        title.AddThemeConstantOverride("outline_size", 8);
        _header.AddChildSafely(title);

        PackedScene scene = PreloadManager.Cache.GetScene(SortButtonScenePath);
        _sortButton = scene.Instantiate<NCardViewSortButton>(PackedScene.GenEditState.Disabled);
        _sortButton.Name = "SortButton";
        _sortButton.CustomMinimumSize = new Vector2(SortButtonWidth, SortButtonHeight);
        _sortButton.AnchorLeft = 1f;
        _sortButton.AnchorRight = 1f;
        _sortButton.AnchorTop = 0.5f;
        _sortButton.AnchorBottom = 0.5f;
        _sortButton.OffsetLeft = -(SortButtonWidth + 16f);
        _sortButton.OffsetRight = -16f;
        _sortButton.OffsetTop = -SortButtonHeight * 0.5f;
        _sortButton.OffsetBottom = SortButtonHeight * 0.5f;
        _sortButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ToggleSort()));
        _header.AddChildSafely(_sortButton);
    }

    private void BuildViewport()
    {
        _viewport = new Control
        {
            Name = "Viewport",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _viewport.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _viewport.OffsetLeft = ViewLeft;
        _viewport.OffsetTop = ViewTop;
        _viewport.OffsetRight = -ViewRight;
        // 一直铺到屏幕底边：原版的 CardGrid 也是「顶边留 80、底边不留」，底部说明条只是浮在卡牌之上。
        // 之前这里留了 ViewBottom 的空白，下方的卡牌区域够不到屏幕底部，看着像没对齐、最后一行也容易被裁掉。
        _viewport.OffsetBottom = 0f;
        _viewport.GuiInput += OnViewportInput;
        this.AddChildSafely(_viewport);

        _content = new Control
        {
            Name = "Content",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _content.Position = Vector2.Zero;
        _viewport.AddChildSafely(_content);
    }

    private void BuildScrollbar()
    {
        PackedScene scene = PreloadManager.Cache.GetScene(ScrollbarScenePath);
        _scrollbar = scene.Instantiate<NScrollbar>(PackedScene.GenEditState.Disabled);
        _scrollbar.Name = "Scrollbar";
        _scrollbar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        _scrollbar.AnchorTop = 0f;
        _scrollbar.AnchorBottom = 1f;
        _scrollbar.OffsetLeft = ScrollbarLeft;
        _scrollbar.OffsetRight = ScrollbarRight;
        _scrollbar.OffsetTop = ViewTop;
        _scrollbar.OffsetBottom = -ViewBottom;
        _scrollbar.MinValue = 0.0;
        _scrollbar.MaxValue = 100.0;
        _scrollbar.Step = 0.01;
        _scrollbar.Page = 0.0;
        _scrollbar.Value = 0.0;
        this.AddChildSafely(_scrollbar);

        _scrollbar.Connect(NScrollbar.SignalName.MousePressed, Callable.From<InputEvent>(_ => _scrollbarPressed = true));
        _scrollbar.Connect(NScrollbar.SignalName.MouseReleased, Callable.From<InputEvent>(_ => _scrollbarPressed = false));
        _scrollbar.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnScrollbarValueChanged));
    }

    private void BuildTicks()
    {
        _ticks = new MemoryRoundTicks
        {
            Name = "RoundTicks",
            MouseFilter = MouseFilterEnum.Stop,
        };
        _ticks.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        _ticks.AnchorTop = 0f;
        _ticks.AnchorBottom = 1f;
        _ticks.OffsetLeft = TickColumnLeft;
        _ticks.OffsetRight = TickColumnRight;
        _ticks.OffsetTop = ViewTop;
        _ticks.OffsetBottom = -ViewBottom;
        _ticks.OnTickPressed = JumpToRound;
        this.AddChildSafely(_ticks);
    }

    private void BuildBackButton()
    {
        PackedScene scene = PreloadManager.Cache.GetScene(BackButtonScenePath);
        _backButton = scene.Instantiate<NBackButton>(PackedScene.GenEditState.Disabled);
        _backButton.Name = "BackButton";
        // 位置沿用场景自带锚点（左下角，与原版 CardPileScreen 的 BackButton 完全一致）。
        _backButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => NCapstoneContainer.Instance?.Close()));
        this.AddChildSafely(_backButton);
        _backButton.Enable();
    }

    /// <summary>
    /// 底部说明条：和抽牌堆/弃牌堆界面一样，用一句话告诉玩家这个界面是干什么的。
    ///
    /// <para>**悬浮**在内容之上：它是根节点的**兄弟**节点（不参与内容布局），底部锚定（anchors 0.5/1），
    /// 于是卡牌区（<c>_viewport</c>）可以一直铺到屏幕底边，说明条只是盖在它上面 —— 与抽牌堆/弃牌堆界面
    /// 的观感一致（那里的 CardGrid 也是直接铺到屏幕底、说明条浮在卡上）。</para>
    ///
    /// <para>节点结构与 <c>card_pile_screen.tscn</c> 的 <c>BottomText</c> 一字不差：底部居中（anchors 0.5/1）、
    /// 黑底 75% <see cref="ColorRect"/> + 内缩 16/6 的 <see cref="MarginContainer"/> + 居中的
    /// <see cref="MegaRichTextLabel"/>（kreon 22、cream、阴影 3/2、行距 -3；<c>[gold]</c> 靠它注册的自定义
    /// bbcode 特效着色，用普通 <see cref="RichTextLabel"/> 会丢掉金色）。</para>
    ///
    /// <para><c>GrowHorizontal = Both</c> 是关键：文案比初始的 124px 宽时，容器以中心为基准向两边长开，
    /// 黑色条才真的包住文字并保持居中（同原版 <c>grow_horizontal = 2</c>）。全部节点 MouseFilter = Ignore，
    /// 不抢下方区域的输入。</para>
    /// </summary>
    private void BuildBottomTip()
    {
        MarginContainer bottomText = new()
        {
            Name = "BottomText",
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f,
            AnchorTop = 1f,
            AnchorRight = 0.5f,
            AnchorBottom = 1f,
            OffsetLeft = -62f,
            OffsetTop = -57f,
            OffsetRight = 62f,
            OffsetBottom = -17f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Begin,
        };
        this.AddChildSafely(bottomText);

        ColorRect background = new()
        {
            Name = "ColorRect",
            Color = new Color(0f, 0f, 0f, 0.752941f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bottomText.AddChildSafely(background);

        MarginContainer padding = new()
        {
            Name = "MarginContainer",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        padding.AddThemeConstantOverride("margin_left", 16);
        padding.AddThemeConstantOverride("margin_top", 6);
        padding.AddThemeConstantOverride("margin_right", 16);
        padding.AddThemeConstantOverride("margin_bottom", 6);
        bottomText.AddChildSafely(padding);

        // 必须是原版的 MegaRichTextLabel（card_pile_screen.tscn 的 BottomLabel 挂的就是它）：
        // 自定义 bbcode 特效（[gold] 等）由它在 CustomEffects 里注册，普通 RichTextLabel 只会把
        // [gold] 当未知标签忽略掉 —— 这就是之前 tip 里 [gold] 不生效的原因；顺带它还按语言换字体
        // （中文会换成 CJK 字体）。注意它的 _Ready 里会 AssertThemeFontOverride("normal_font")，
        // 没有字体覆盖会直接抛异常，所以字体必须先设好再入树（下面的 AddChildSafely 在最后）。
        MegaRichTextLabel label = new()
        {
            Name = "BottomLabel",
            BbcodeEnabled = true,
            FitContent = true,
            // 原版同样关掉自动缩放（与 FitContent 互斥，开着会打警告并按内容改字号）。
            AutoSizeEnabled = false,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            // 同原版（size_flags = 4）：按内容收缩并在容器里居中，黑色条才刚好包住文字。
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        label.AddThemeFontOverride("normal_font", PreloadManager.Cache.GetAsset<Font>(KreonRegularPath));
        label.AddThemeFontOverride("bold_font", PreloadManager.Cache.GetAsset<Font>(KreonBoldPath));
        label.AddThemeFontSizeOverride("normal_font_size", 22);
        label.AddThemeFontSizeOverride("bold_font_size", 22);
        label.AddThemeFontSizeOverride("italics_font_size", 22);
        label.AddThemeFontSizeOverride("bold_italics_font_size", 22);
        label.AddThemeFontSizeOverride("mono_font_size", 22);
        label.AddThemeColorOverride("default_color", StsColors.cream);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25098f));
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        label.AddThemeConstantOverride("line_separation", -3);
        // 放在字体之后：MegaRichTextLabel.Text 的 setter 会顺带装特效 / 解析 bbcode。
        // [center] 不加闭合标签 → 整段（含换行后的第二行）都居中，同原版 NCardPileScreen 的写法。
        label.Text = "[center]" + LocOrDefault("REED-RECALL.pile.info", DefaultInfoText);
        padding.AddChildSafely(label);

        // 入树之后再确认一遍整棵说明条都不参与鼠标拾取：它浮在内容/刻度之上，
        // 只要有一层是默认的 Stop，就会挡住下方 UI 的点击（原版那一行就是默认 Stop，
        // 那里下面没有可点的东西所以无所谓；我们这里下面有刻度，不能不管）。
        MakeMouseTransparent(bottomText);
    }

    /// <summary>把一棵子树上的所有 <see cref="Control"/> 都设成 <c>Ignore</c>（不接收、也不阻挡鼠标事件）。</summary>
    private static void MakeMouseTransparent(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
        }
        foreach (Node child in node.GetChildren())
        {
            MakeMouseTransparent(child);
        }
    }

    private void OnMemoryChanged(Player player)
    {
        if (_player != null && player.NetId != _player.NetId)
        {
            return; // 只关心自己那份回忆
        }
        Rebuild();
    }

    /// <summary>排序按钮：在「时间逆序（新 → 旧，默认）」与「时间顺序（旧 → 新）」之间切换。</summary>
    private void ToggleSort()
    {
        _sortDescending = !_sortDescending;
        _pendingJumpToCurrent = true; // 换方向后把视线带回当前回合
        Rebuild();
    }

    private string SortButtonText()
    {
        return _sortDescending
            ? LocOrDefault("REED-RECALL.sort.desc", "时间逆序")
            : LocOrDefault("REED-RECALL.sort.asc", "时间顺序");
    }

    /// <summary>把方向与文案写进排序按钮（按钮必须已经 <c>_Ready</c>，所以拖到 <see cref="Rebuild"/> 里做）。</summary>
    private void ApplySortButtonState()
    {
        if (_sortButton == null || !GodotObject.IsInstanceValid(_sortButton) || !_sortButton.IsNodeReady())
        {
            return;
        }
        if (_sortApplied && _appliedSortDescending == _sortDescending)
        {
            return;
        }
        _sortApplied = true;
        _appliedSortDescending = _sortDescending;
        _sortButton.IsDescending = _sortDescending; // 会翻转箭头图标
        _sortButton.SetLabel(SortButtonText());
    }

    // ============ 内容构建 ============

    /// <summary>
    /// 按当前回忆重建整个内容区（回合区块 + 卡牌 + 刻度）。
    ///
    /// <para>两种情况会挂起 <see cref="_needsRebuild"/> 交给 <see cref="_Process"/> 每帧重试：
    /// ① 视口宽度还没落定（首帧）；② <c>_content</c> 还没真正入树——本界面是 <c>_Ready</c> 里拼出来的，
    /// <c>AddChildSafely</c> 对「已入树但尚未 ready」的父节点走的是 <c>CallDeferred</c>，
    /// 此时若把卡直接塞进去，卡自己的 <c>_Ready</c> 没跑，<c>NCard.UpdateVisuals</c> 会被
    /// <c>IsNodeReady()</c> 挡掉（卡面文字/数值全是空的）。等一帧就都对了。</para>
    ///
    /// <para>除这两种情况外，这个方法**必须走完**：拿不到玩家就按空回忆渲染，
    /// 否则界面会一片空白、连提示都没有。</para>
    /// </summary>
    private void Rebuild()
    {
        if (_viewport == null || _content == null)
        {
            return;
        }
        if (!_content.IsInsideTree() || !_viewport.IsInsideTree())
        {
            _needsRebuild = true;
            return;
        }

        float width = _viewport.Size.X;
        if (width <= 1f)
        {
            // 布局还没算完：退回自身尺寸（同一次布局里已可用）。
            width = Size.X - ViewLeft - ViewRight;
        }
        if (width <= 1f)
        {
            _needsRebuild = true;
            return;
        }
        _needsRebuild = false;

        ClearContent();
        ApplySortButtonState();

        Player? player = ResolvePlayer();
        PlayerMemory? memory = player == null ? null : MemorySystem.Of(player);
        _currentRound = player?.Creature?.CombatState?.RoundNumber ?? 0;

        List<int> rounds = memory == null ? new List<int>() : memory.Rounds.ToList();
        if (_sortDescending)
        {
            rounds.Reverse(); // 新 → 旧（默认）
        }

        // 「正在追忆的回合」在整屏只算一次：区块标题与刻度列共用同一个值（两处必须一致）。
        int? focusRound = RecallRound();

        float y = ContentPaddingTop;
        foreach (int round in rounds)
        {
            IReadOnlyList<RememberedCard> items = memory!.PeekRound(round);
            if (!_sortDescending && items.Count > 1)
            {
                items = items.Reverse().ToList(); // 同一回合内也按时间顺序
            }
            y = BuildSection(round, items, y, width, round == _currentRound, round == focusRound);
        }

        _contentHeight = y + ContentPaddingBottom;

        if (rounds.Count == 0)
        {
            // 空回忆：把内容区撑到视口高，提示语垂直居中，读起来不像「界面坏了」。
            float viewHeight = _viewport.Size.Y > 1f ? _viewport.Size.Y : 600f;
            _contentHeight = Mathf.Max(_contentHeight, viewHeight);
            BuildEmptyHint(width, Mathf.Max(ContentPaddingTop, (_contentHeight - EmptyHintHeight) * 0.5f));
        }

        _content.Size = new Vector2(width, _contentHeight);
        _content.Position = new Vector2(0f, _scroll);

        _ticks?.SetSections(_sections, _currentRound, _contentHeight, focusRound);
        ClampScroll();
    }

    /// <summary>
    /// 正在被追忆的回合（追忆选择屏开着时才有值），用于把那一回合的刻度标成蓝色。
    /// 不是本地玩家在追忆就是 null（多人下别人的追忆不该点亮你的回忆界面）。
    /// </summary>
    private int? RecallRound() => RecallFocus.RoundFor(ResolvePlayer());

    /// <summary>
    /// 追忆开始 / 结束：回忆的**内容**没变（变化的只是「哪一回合正在被追忆」），所以不重建卡牌，
    /// 只把刻度列重画一遍 + 就地重刷各区块标题的颜色即可。
    /// </summary>
    private void OnRecallFocusChanged()
    {
        int? focusRound = RecallRound();
        foreach (Section section in _sections)
        {
            if (section.Header is { } header && GodotObject.IsInstanceValid(header))
            {
                ApplyHeaderStyle(header, section.Round == _currentRound, section.Round == focusRound, section.Empty);
            }
            if (section.Line is { } line && GodotObject.IsInstanceValid(line))
            {
                line.Color = HeaderLineColor(section.Round == _currentRound, section.Round == focusRound);
            }
        }
        if (_ticks != null && GodotObject.IsInstanceValid(_ticks))
        {
            _ticks.SetSections(_sections, _currentRound, _contentHeight, focusRound);
        }
    }

    /// <summary>界面拿到的玩家：优先用打开时传入的，没有就自己兜底找一次（读档 / 重建场景都可能漏传）。</summary>
    private Player? ResolvePlayer()
    {
        if (_player == null)
        {
            _player = MemorySystem.ResolveLocalPlayer();
        }
        return _player;
    }

    /// <summary>构建一个回合区块（标题 + 分隔线 + 若干行卡牌），返回下一个区块的顶部 y。</summary>
    private float BuildSection(int round, IReadOnlyList<RememberedCard> items, float y, float width, bool isCurrent, bool isFocused)
    {
        bool empty = items.Count == 0;
        float headerHeight = empty ? EmptyRoundHeight : HeaderHeightInSection;

        Label label = new Label
        {
            Name = $"Round{round}Label",
            Text = RoundHeaderText(round),
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        label.Position = new Vector2(4f, y);
        label.Size = new Vector2(width, headerHeight - 10f);
        // 空回合不抢眼：只用暗淡的标题交代「这个回合存在，只是没留下回忆」。
        ApplyHeaderStyle(label, isCurrent, isFocused, empty);
        _content.AddChildSafely(label);

        if (empty)
        {
            _sections.Add(new Section(round, y, 0, label, null, true));
            return y + headerHeight + SectionGap;
        }

        // 标题下的分隔线（颜色跟标题一致：追忆中的回合是蓝色）
        ColorRect line = new ColorRect
        {
            Name = $"Round{round}Line",
            Color = HeaderLineColor(isCurrent, isFocused),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.Position = new Vector2(4f, y + HeaderHeightInSection - 10f);
        line.Size = new Vector2(Mathf.Max(0f, width - 8f), 2f);
        _content.AddChildSafely(line);
        _sections.Add(new Section(round, y, items.Count, label, line));

        float cardW = CardSize.X;
        float cardH = CardSize.Y;
        int columns = Mathf.Max(1, Mathf.FloorToInt((width + CardPadding) / (cardW + CardPadding)));
        float rowTop = y + HeaderHeightInSection;

        for (int i = 0; i < items.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;
            int inRow = Mathf.Min(columns, items.Count - row * columns);
            // 每一行各自水平居中（内容区是全屏宽的，短行靠左会显得界面没铺满）。
            float rowWidth = inRow * cardW + (inRow - 1) * CardPadding;
            float x0 = (width - rowWidth) * 0.5f;
            Vector2 topLeft = new Vector2(
                x0 + col * (cardW + CardPadding),
                rowTop + row * (cardH + CardPadding));

            CreateHolder(items[i].Card, topLeft + CardSize * 0.5f);
        }

        int rows = (items.Count + columns - 1) / columns;
        float sectionHeight = HeaderHeightInSection + rows * cardH + Mathf.Max(0, rows - 1) * CardPadding;
        return y + sectionHeight + SectionGap;
    }

    private void BuildEmptyHint(float width, float y)
    {
        Label label = new Label
        {
            Name = "EmptyHint",
            Text = LocOrDefault("REED-RECALL.pile.empty", "这场战斗还没有留下任何回忆。"),
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Position = new Vector2(0f, y);
        label.Size = new Vector2(width, EmptyHintHeight);
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(KreonRegularPath));
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", new Color(StsColors.cream, 0.6f));
        _content.AddChildSafely(label);
    }

    /// <summary>
    /// 摆一张卡：走 deck / 牌堆界面同款的 <see cref="NGridCardHolder"/>，于是**悬停放大 + 悬停 tip
    /// + 点击**全部由原版 holder 自带（尺寸 <c>smallScale</c> = 0.8，与 deck 完全一致）。
    /// </summary>
    private void CreateHolder(CardModel model, Vector2 center)
    {
        NCard? card = NCard.Create(model, ModelVisibility.Visible);
        if (card == null)
        {
            return; // TestMode 下原版工厂直接返回 null
        }
        NGridCardHolder? holder = NGridCardHolder.Create(card);
        if (holder == null)
        {
            return;
        }
        holder.Name = $"MemoryCard{_cards.Count}";
        // holder 与卡一样：Position 是**卡牌中心**。
        holder.Position = center;
        holder.MouseFilter = MouseFilterEnum.Pass; // 与 NCardGrid.InitGrid 一致
        _content.AddChildSafely(holder);
        holder.Connect(NCardHolder.SignalName.Pressed, Callable.From<NCardHolder>(OnHolderPressed));
        holder.Connect(NCardHolder.SignalName.AltPressed, Callable.From<NCardHolder>(OnHolderPressed));
        // 必须在入树之后：NCard.UpdateVisuals 内部有 IsNodeReady() 前置判断。
        // 用自定义的「回忆牌堆」类型：卡面按非战斗牌堆正常渲染，同时语义上属于回忆而不是「无牌堆」。
        card.UpdateVisuals(ReedPileType.Memory, CardPreviewMode.Normal);
        _cards.Add(model);
    }

    private void ClearContent()
    {
        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChildSafely(child);
            // holder / card 都是 IPoolable → 这个扩展会把它们交还 NodePool（先出树再归还，
            // 于是池子不会误删它们的信号连接）；Label / ColorRect 则直接 QueueFree。
            child.QueueFreeSafely();
        }
        _cards.Clear();
        _sections.Clear();
    }

    /// <summary>回合标题文案；本地化缺失时退回英文，绝不因为一个文案把整个界面搞崩。</summary>
    private static string RoundHeaderText(int round)
    {
        LocString loc = new LocString("static_hover_tips", "REED-RECALL.round.header");
        loc.Add("Round", round);
        return FormatLoc(loc, $"Round {round}");
    }

    private static string LocOrDefault(string key, string fallback)
    {
        return FormatLoc(new LocString("static_hover_tips", key), fallback);
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
            GD.PushWarning($"[MemoryPileScreen] 本地化文案缺失（{e.Message}），已用兜底文本。");
            return fallback;
        }
    }

    /// <summary>标题下分隔线的颜色：与标题同步 —— 追忆中的回合蓝、当前回合金、其余淡金。</summary>
    private static Color HeaderLineColor(bool isCurrent, bool isFocused)
    {
        return new Color(isFocused ? StsColors.blue : StsColors.gold, isCurrent || isFocused ? 0.55f : 0.25f);
    }

    /// <summary>
    /// 回合标题样式。<paramref name="isFocused"/>（正在被追忆的回合）用蓝色、<paramref name="isCurrent"/>
    /// （当前回合）用金色 —— 与右侧刻度列同一套规则，**蓝色优先**（追忆的正好是当前回合时蓝色覆盖金色）。
    /// </summary>
    private static void ApplyHeaderStyle(Label label, bool isCurrent, bool isFocused, bool empty = false)
    {
        bool emphasized = isCurrent || isFocused;
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(KreonBoldPath));
        label.AddThemeFontSizeOverride("font_size", emphasized ? 36 : empty ? 28 : 32);
        label.AddThemeColorOverride(
            "font_color",
            isFocused ? StsColors.blue
            : isCurrent ? StsColors.gold
            : empty ? new Color(StsColors.cream, 0.4f)
            : StsColors.cream);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }

    // ============ 滚动 ============

    private float ScrollLimit => Mathf.Min(0f, _viewport.Size.Y - _contentHeight);

    private void ClampScroll()
    {
        _targetScroll = Mathf.Clamp(_targetScroll, ScrollLimit, 0f);
        _scroll = Mathf.Clamp(_scroll, ScrollLimit, 0f);
    }

    private void OnScrollbarValueChanged(double value)
    {
        if (!_scrollbarPressed)
        {
            return; // 我们自己 SetValueWithoutAnimation 造成的回环，忽略
        }
        _targetScroll = ScrollLimit * (float)(value / 100.0);
    }

    /// <summary>把滚动条拇指挪到与当前滚动位置一致的地方（不触发回调）。</summary>
    private void SyncScrollbar()
    {
        float limit = ScrollLimit;
        if (!_scrollbarPressed && limit < 0f)
        {
            _scrollbar.SetValueWithoutAnimation(Mathf.Clamp(_scroll / limit, 0f, 1f) * 100f);
        }
    }

    /// <summary>内容坐标里的某个回合区块顶部；没有该回合时返回 null。</summary>
    private float? SectionTop(int round)
    {
        foreach (Section section in _sections)
        {
            if (section.Round == round)
            {
                return section.Y;
            }
        }
        return null;
    }

    /// <summary>平滑滚到某个回合区块的顶部。</summary>
    private void JumpToRound(int round)
    {
        if (SectionTop(round) is { } top)
        {
            _targetScroll = Mathf.Clamp(-top, ScrollLimit, 0f);
        }
    }

    /// <summary>打开界面后，把滚动位置对齐到当前回合（布局尺寸没落定前一直挂起）。</summary>
    private void ApplyPendingJump()
    {
        if (!_pendingJumpToCurrent || _needsRebuild || _viewport.Size.Y <= 1f)
        {
            return;
        }
        _pendingJumpToCurrent = false;
        if (SectionTop(_currentRound) is { } top)
        {
            _scroll = _targetScroll = Mathf.Clamp(-top, ScrollLimit, 0f);
            _content.Position = new Vector2(0f, _scroll);
            SyncScrollbar();
        }
    }

    public override void _Process(double delta)
    {
        if (_viewport == null)
        {
            return;
        }

        if (_needsRebuild)
        {
            Rebuild(); // 布局落定前挂起的重建（私有方法不能用 CallDeferred(nameof(...))，改在每帧重试）
            if (_needsRebuild)
            {
                return;
            }
        }

        ApplyPendingJump();

        float limit = ScrollLimit;

        if (_scrollbarPressed)
        {
            _targetScroll = Mathf.Clamp(_targetScroll, limit, 0f);
        }

        if (Mathf.Abs(_scroll - _targetScroll) > 0.1f)
        {
            _scroll = Mathf.Lerp(_scroll, _targetScroll, Mathf.Clamp((float)delta * ScrollLerpSpeed, 0f, 1f));
            if (Mathf.Abs(_scroll - _targetScroll) < 0.5f)
            {
                _scroll = _targetScroll;
            }
            ClampScroll();
            _content.Position = new Vector2(0f, _scroll);
            SyncScrollbar();
        }

        // 滚动条：内容装得下就藏起来（与原版网格一致）
        bool scrollable = _viewport.Size.Y < _contentHeight;
        if (_scrollbar.Visible != scrollable)
        {
            _scrollbar.Visible = scrollable;
            _scrollbar.MouseFilter = scrollable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        }

        _ticks?.Refresh(_viewport.Size.Y);
    }

    // ============ 输入：拖动 / 滚轮 ============

    /// <summary>
    /// 滚轮 / 触控板滚动。挂在**整屏**上（不是视口）：视口只占屏幕中间一块，
    /// 而玩家会理所当然地在刻度列、说明条、右侧空白处滚 —— 那些地方的滚轮事件顺着父链传到这里。
    ///
    /// <para>注意滚轮本身也是 <see cref="InputEventMouseButton"/>（<c>WheelUp</c>/<c>WheelDown</c>），
    /// 所以**不能**交给 <see cref="HandleMouseButton"/>（它只认左键、顺带把滚轮一起吞掉）——
    /// 这正是之前滚轮完全没反应的原因。</para>
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (_viewport == null || !GodotObject.IsInstanceValid(_viewport))
        {
            return;
        }
        float wheel = ScrollHelper.GetDragForScrollEvent(@event);
        if (Mathf.IsZeroApprox(wheel))
        {
            return;
        }
        _targetScroll = Mathf.Clamp(_targetScroll + wheel, ScrollLimit, 0f);
        AcceptEvent();
    }

    private void OnViewportInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            HandleMouseButton(button);
            return;
        }
        if (@event is InputEventMouseMotion motion && _dragging)
        {
            _dragDistance += Mathf.Abs(motion.Relative.Y);
            _targetScroll = Mathf.Clamp(_dragStartScroll + (motion.Position.Y - _dragStartMouseY), ScrollLimit, 0f);
        }
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        if (button.Pressed)
        {
            _dragging = true;
            _dragDistance = 0f;
            _dragStartMouseY = button.Position.Y;
            _dragStartScroll = _scroll;
            return;
        }
        _dragging = false;
    }

    /// <summary>点卡牌 → 原版放大查看（可左右翻页），与 deck 的 <c>ShowCardDetail</c> 同一套 API。</summary>
    private void OnHolderPressed(NCardHolder holder)
    {
        CardModel? model = holder.CardModel;
        if (model == null || NGame.Instance is not { } game)
        {
            return;
        }
        int index = _cards.IndexOf(model);
        if (index < 0)
        {
            return;
        }
        _backButton.Disable();
        NInspectCardScreen inspect = game.GetInspectCardScreen();
        List<CardModel> list = _cards.ToList();
        inspect.Open(list, index, false);
        Callable callable = Callable.From(OnInspectHidden);
        if (inspect.IsConnected(CanvasItem.SignalName.VisibilityChanged, callable))
        {
            inspect.Disconnect(CanvasItem.SignalName.VisibilityChanged, callable);
        }
        inspect.Connect(CanvasItem.SignalName.VisibilityChanged, callable, (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void OnInspectHidden()
    {
        if (_backButton != null && GodotObject.IsInstanceValid(_backButton))
        {
            _backButton.Enable();
        }
    }

    // ============ 回合刻度列 ============

    /// <summary>
    /// 滚动条左侧的「回合时间轴」：**固定不动**（不随内容滚动），自上而下 = 新回合 → 老回合。
    ///
    /// <list type="bullet">
    ///   <item>刻度 y 取自**真实内容坐标**：把各回合区块顶部 [第一个区块, 最后一个区块] 线性映射到
    ///     刻度列高度上 —— 所以间距反映真实占比，区块长的回合间隔就大，不是均匀切分；</item>
    ///   <item>每根刻度旁边画一条淡淡的**区间带**（本回合区块顶 → 下个回合区块顶），
    ///     一眼能看出每个回合在整场战斗里占了多大篇幅；当前回合的区间带是金色；</item>
    ///   <item>回合太多时自动抽稀：间隔 1 → 2 → 4 → 8 …，保证屏幕上大约 10 根刻度，方便快速跳转；</item>
    ///   <item>当前回合**永远显示**（即使它落在抽稀的间隔之外），用金色 + 更粗的刻度线 + 右侧圆点标出，
    ///     标签写成完整标题，箭头指向它自己那根刻度；</item>
    ///   <item>**正在被追忆的回合**（<see cref="RecallFocus"/>，即追忆选择屏开着的那一会儿）用同样的方式
    ///     标出，但换成**蓝色** —— 于是「我这次追的是哪一回合的牌」在回忆界面上一眼可见；它恰好就是当前回合时
    ///     蓝色**覆盖**金色（蓝色优先）;</item>
    ///   <item>鼠标悬停：刻度线变长变粗、标签放大变亮（与按钮一致的「悬停放大」手感）；</item>
    ///   <item>点任意刻度 → 平滑滚到该回合区块。</item>
    /// </list>
    /// </summary>
    private sealed partial class MemoryRoundTicks : Control
    {
        private const float TickHalfLength = 14f;    // 刻度线长度（贴着右边缘）
        private const float HoverTickExtra = 9f;     // 悬停时刻度线额外伸长的部分
        private const float HoverTickWidth = 5f;
        private const float HoverLabelScale = 1.15f;
        private const float BandWidth = 5f;          // 区间带的宽度
        private const float ClickTolerance = 24f;
        private const float LabelHeight = 30f;
        private const float ArrowGap = 18f;          // 标签与刻度线之间留给箭头的空隙
        private const float VerticalMargin = 22f;    // 首尾刻度离上/下边缘的距离
        private const int MaxVisibleTicks = 10;
        private const double HoverScaleDur = 0.08;

        private readonly List<int> _allRounds = new();   // 全部回合（与界面同序）
        private readonly List<float> _sectionYs = new(); // 各区块顶部（内容坐标系）
        private readonly List<int> _shownRounds = new(); // 抽稀后真正画刻度的回合
        private readonly List<float> _ys = new();        // 各显示刻度的 y
        private readonly List<float> _bandEnds = new();  // 各显示刻度所属区间的下沿 y
        private readonly List<Label> _labels = new();
        private readonly List<Tween?> _labelTweens = new();
        private int _currentRound;

        /// <summary>正在被追忆的回合（蓝色标出）；没在追忆时为 null。</summary>
        private int? _focusRound;

        private float _contentHeight = 1f;
        private float _lastHeight = -1f;
        private bool _dirty = true;
        private int _hovered = -1;

        /// <summary>刻度被点按（参数 = 回合数）。</summary>
        public Action<int>? OnTickPressed { get; set; }

        public override void _Ready()
        {
            MouseExited += () => SetHovered(-1);
        }

        /// <summary>
        /// 重建刻度（回合集合 / 当前回合 / 内容总高 / 正在追忆的回合变化时调用）。重复调用必须幂等。
        /// <paramref name="focusRound"/> 为 null 表示当前没有人在追忆。
        /// </summary>
        public void SetSections(IReadOnlyList<Section> sections, int currentRound, float contentHeight, int? focusRound)
        {
            _allRounds.Clear();
            _sectionYs.Clear();
            foreach (Section section in sections)
            {
                _allRounds.Add(section.Round);
                _sectionYs.Add(section.Y);
            }
            _currentRound = currentRound;
            _focusRound = focusRound;
            _contentHeight = Mathf.Max(contentHeight, 1f);
            _hovered = -1;
            SelectShownRounds();
            EnsureLabels();
            ApplyLabelStyle();
            _lastHeight = -1f; // 强制重排（尺寸已知时立刻摆好，避免刻度在第一帧停在左上角）
            Refresh(Size.Y);
        }

        /// <summary>
        /// 抽稀：间隔 1/2/4/8…，让屏上刻度数不超过 <see cref="MaxVisibleTicks"/>，
        /// 并保证**当前回合**与**正在追忆的回合**这两根一定在内（它们各有一段完整标题要显示）。
        /// </summary>
        private void SelectShownRounds()
        {
            _shownRounds.Clear();
            int n = _allRounds.Count;
            if (n == 0)
            {
                return;
            }
            int step = 1;
            while ((n + step - 1) / step > MaxVisibleTicks)
            {
                step *= 2;
            }
            for (int i = 0; i < n; i += step)
            {
                _shownRounds.Add(_allRounds[i]);
            }
            EnsureShown(_currentRound);
            EnsureShown(_focusRound);
        }

        /// <summary>把某个回合插进显示列表（抽稀恰好滤掉它时用），插入后仍按原来的回合顺序排列。</summary>
        private void EnsureShown(int? round)
        {
            if (round is not { } value || value <= 0
                || !_allRounds.Contains(value) || _shownRounds.Contains(value))
            {
                return;
            }
            _shownRounds.Add(value);
            _shownRounds.Sort((a, b) => _allRounds.IndexOf(a).CompareTo(_allRounds.IndexOf(b)));
        }

        private void EnsureLabels()
        {
            while (_labels.Count < _shownRounds.Count)
            {
                Label label = new Label
                {
                    Name = $"Tick{_labels.Count}",
                    MouseFilter = MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                // 字体/描边是静态样式，只在这里设一次；之后只改文字、字号与颜色。
                label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>(KreonBoldPath));
                label.AddThemeConstantOverride("outline_size", 6);
                label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
                this.AddChildSafely(label);
                _labels.Add(label);
            }
        }

        /// <summary>把「文字 / 字号 / 颜色 / 悬停放大」一次性写进标签（悬停变化只走这里）。</summary>
        private void ApplyLabelStyle()
        {
            if (!IsInsideTree())
            {
                _dirty = true; // 还没入树（首帧）：入树后由 Refresh 补一次
                return;
            }
            for (int i = 0; i < _labels.Count; i++)
            {
                bool used = i < _shownRounds.Count;
                _labels[i].Visible = used;
                if (!used)
                {
                    continue;
                }
                int round = _shownRounds[i];
                bool emphasized = IsEmphasized(round);
                bool hovered = i == _hovered;
                // 当前回合 / 正在追忆的回合写成完整标题（「第 N 回合」），滚动时一眼就能找到自己现在在哪。
                _labels[i].Text = emphasized ? RoundHeaderText(round) : round.ToString();
                _labels[i].AddThemeFontSizeOverride("font_size", emphasized ? 26 : 22);
                _labels[i].AddThemeColorOverride(
                    "font_color",
                    emphasized ? EmphasisColor(round) : hovered ? StsColors.cream : new Color(StsColors.cream, 0.7f));
                _labels[i].AddThemeConstantOverride("outline_size", hovered ? 8 : 6);
                TweenLabelScale(i, hovered ? HoverLabelScale : 1f);
            }
            _dirty = true;
        }

        /// <summary>标签缩放：轴心放在右端中点，于是放大是「朝左长出去」，不会压到刻度线。</summary>
        private void TweenLabelScale(int index, float target)
        {
            while (_labelTweens.Count <= index)
            {
                _labelTweens.Add(null);
            }
            Label label = _labels[index];
            label.PivotOffset = new Vector2(label.Size.X, LabelHeight * 0.5f);
            _labelTweens[index]?.Kill();
            _labelTweens[index] = CreateTween();
            _labelTweens[index]!.TweenProperty(label, "scale", Vector2.One * target, HoverScaleDur)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
        }

        private void SetHovered(int index)
        {
            if (_hovered == index)
            {
                return;
            }
            _hovered = index;
            ApplyLabelStyle();
            QueueRedraw();
        }

        /// <summary>离鼠标最近的那根刻度（超出容差返回 -1）。</summary>
        private int NearestTick(float y)
        {
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < _shownRounds.Count && i < _ys.Count; i++)
            {
                float distance = Mathf.Abs(_ys[i] - y);
                if (distance < best)
                {
                    best = distance;
                    bestIndex = i;
                }
            }
            return best <= ClickTolerance ? bestIndex : -1;
        }

        /// <summary>
        /// 按**真实内容坐标**摆刻度：把 [第一个区块顶, 最后一个区块顶] 映射到刻度列高度上。
        /// 与滚动位置无关（刻度是「钉住」的），但间距忠实反映每个回合区块的实际长短。
        /// </summary>
        public void Refresh(float viewHeight)
        {
            if (viewHeight <= 1f || !IsInsideTree())
            {
                return;
            }

            bool heightChanged = !Mathf.IsEqualApprox(viewHeight, _lastHeight);
            if (!heightChanged && !_dirty)
            {
                return;
            }
            _lastHeight = viewHeight;
            _dirty = false;

            _ys.Clear();
            _bandEnds.Clear();
            int n = _shownRounds.Count;
            if (n == 0 || _sectionYs.Count == 0)
            {
                QueueRedraw();
                return;
            }

            float span = Mathf.Max(viewHeight - VerticalMargin * 2f, 1f);
            float firstTop = _sectionYs[0];
            float lastTop = _sectionYs[^1];
            float range = lastTop - firstTop;

            for (int i = 0; i < n; i++)
            {
                int index = _allRounds.IndexOf(_shownRounds[i]);
                if (index < 0)
                {
                    _ys.Add(VerticalMargin + span * 0.5f);
                    _bandEnds.Add(VerticalMargin + span * 0.5f);
                    continue;
                }

                float top = MapToColumn(_sectionYs[index], firstTop, range, span);
                // 区间下沿 = 下一个区块顶；最后一个区块没有下一个，就一路画到内容末尾。
                float endContent = index + 1 < _sectionYs.Count ? _sectionYs[index + 1] : _contentHeight;
                float end = MapToColumn(Mathf.Min(endContent, lastTop), firstTop, range, span);
                if (index + 1 >= _sectionYs.Count)
                {
                    end = Mathf.Max(end, viewHeight - VerticalMargin); // 最后一个回合：区间一直延伸到列底
                }
                _ys.Add(top);
                _bandEnds.Add(Mathf.Max(end, top + 3f));
            }

            for (int i = 0; i < n && i < _labels.Count; i++)
            {
                _labels[i].Position = new Vector2(0f, _ys[i] - LabelHeight * 0.5f);
                _labels[i].Size = new Vector2(Size.X - TickHalfLength - ArrowGap, LabelHeight);
                // 缩放的轴心放在标签右端中点（贴着刻度线那一侧），悬停放大时才不会遮住刻度。
                _labels[i].PivotOffset = new Vector2(_labels[i].Size.X, LabelHeight * 0.5f);
            }

            QueueRedraw();
        }

        /// <summary>内容坐标 → 刻度列坐标。只有一个回合（range 为 0）时居中摆放。</summary>
        private static float MapToColumn(float contentY, float firstTop, float range, float span)
        {
            if (range <= 1f)
            {
                return VerticalMargin + span * 0.5f;
            }
            return VerticalMargin + Mathf.Clamp((contentY - firstTop) / range, 0f, 1f) * span;
        }

        public override void _Draw()
        {
            float x1 = Size.X;
            int n = Mathf.Min(_shownRounds.Count, Mathf.Min(_ys.Count, _bandEnds.Count));

            // 时间轴主线
            DrawLine(new Vector2(x1, 0f), new Vector2(x1, Size.Y), new Color(StsColors.cream, 0.12f), 1f);

            // 区间带：本回合区块占用的篇幅（真实长短比例）
            for (int i = 0; i < n; i++)
            {
                bool emphasized = IsEmphasized(_shownRounds[i]);
                Color band = emphasized
                    ? new Color(EmphasisColor(_shownRounds[i]), 0.35f)
                    : new Color(StsColors.cream, 0.14f);
                DrawRect(new Rect2(x1 - BandWidth, _ys[i], BandWidth, Mathf.Max(3f, _bandEnds[i] - _ys[i])), band);
            }

            for (int i = 0; i < n; i++)
            {
                float y = _ys[i];
                bool emphasized = IsEmphasized(_shownRounds[i]);
                Color emphasis = EmphasisColor(_shownRounds[i]);
                bool hovered = i == _hovered;
                Color color = emphasized
                    ? emphasis
                    : hovered ? StsColors.cream : new Color(StsColors.cream, 0.45f);

                // 悬停放大：刻度线朝左伸长并加粗（标签的放大在 ApplyLabelStyle 里做）。
                float length = TickHalfLength + (hovered ? HoverTickExtra : 0f);
                float width = emphasized ? 4f : hovered ? HoverTickWidth : 2f;
                DrawLine(new Vector2(x1 - length, y), new Vector2(x1, y), color, width);

                if (hovered && !emphasized)
                {
                    DrawCircle(new Vector2(x1 - length, y), 4f, color);
                }

                if (emphasized)
                {
                    DrawCircle(new Vector2(x1, y), 5f, emphasis);
                    // 「当前回合 / 正在追忆的回合在这」的箭头：紧贴标签右侧，指向这根刻度
                    // （长度固定，悬停时不乱跳；颜色跟刻度一致 —— 追忆中就是蓝色）。
                    float ax = x1 - TickHalfLength - ArrowGap + 4f;
                    DrawColoredPolygon(
                        new[]
                        {
                            new Vector2(ax, y - 7f),
                            new Vector2(ax, y + 7f),
                            new Vector2(ax + 11f, y),
                        },
                        emphasis);
                }
            }
        }

        /// <summary>这根刻度要不要「重点标出」：当前回合（金）或正在被追忆的回合（蓝）。</summary>
        private bool IsEmphasized(int round) => round == _currentRound || round == _focusRound;

        /// <summary>
        /// 重点刻度的颜色：正在追忆 → 蓝色（<see cref="StsColors.blue"/>）；
        /// 否则是当前回合 → 金色。**蓝色优先**，于是「追忆的正好是当前回合」时蓝色覆盖金色。
        /// </summary>
        private Color EmphasisColor(int round) => round == _focusRound ? StsColors.blue : StsColors.gold;

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion)
            {
                SetHovered(NearestTick(motion.Position.Y)); // 悬停放大
                return;
            }
            if (@event is not InputEventMouseButton button
                || button.ButtonIndex != MouseButton.Left
                || !button.Pressed)
            {
                return;
            }
            int index = NearestTick(button.Position.Y);
            if (index >= 0)
            {
                AcceptEvent();
                OnTickPressed?.Invoke(_shownRounds[index]);
            }
        }
    }
}
