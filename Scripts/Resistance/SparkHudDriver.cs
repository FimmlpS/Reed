using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Reed.Scripts.Nodes;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 火花牌的 UI 驱动器：挂在每个生物的“生物状态条（NCreatureStateDisplay）”上，全部用纯 C# 拼 Control。
///
/// 两块显示：
///   1) 计数圆（类似格挡指示）：抗性条左端一个圆形徽章，内部显示当前火花牌数量（默认显示，不悬停也在）。
///      悬浮在圆上会弹出原版风格的 tip 框（标题=火花牌，描述=“当前你有 N 张火花牌”）。
///   2) 火花牌行：悬浮数字圆 或 生物本体/血条 时，把本地玩家的火花牌以真实卡牌（NCard）从左到右
///      排在该生物上方；卡牌上沿、左右都不会超出屏幕。
///
/// 出现/消失特效：出现=快速渐显（0.16s）；消失=参考卡牌“虚无”消散的观感做淡出+上浮（火焰色调染色
/// 会盖住卡牌本体，暂不做，留注释位置便于日后换用原版虚无/气绝的淡出材质）。
/// </summary>
internal sealed class SparkHudDriver
{
    public const string NodeName = "ReedSparkHud";

    // —— 可直接调的外观参数 ——
    private const float CardScale = 0.44f;       // 真实卡 300x422 → 约 132x186（第3点：放大到 2 倍左右）
    private const float CardGap = 8f;            // 卡与卡间距（够宽，放大后也不会互相遮盖）
    private const float RowAboveBar = 66f;       // 卡行下沿离抗性条上沿的距离：加大 = 行整体调高（y 更小）
    private const float RowMinMargin = 6f;       // 卡行与屏幕边缘的最小留白
    private const float CircleDiameter = 30f;    // 计数圆直径
    private const float RevealFade = 0.16f;      // 卡行整行渐显时长
    private const float VanishFade = 0.30f;      // 单张火花牌消失时长
    private const float VanishRise = 16f;        // 消失时上浮像素

    // —— 计数圆“添加闪烁”（第2点）：金色光环在圆背后快速外扩淡出 ——
    private const float FlashFade = 0.32f;       // 光环外扩+淡出时长
    private const float FlashScale = 2.6f;       // 光环最终放大倍数
    private const float FlashPeakAlpha = 0.85f;  // 光环起始不透明度
    private static readonly Color FlashBg = Color.FromHtml("#FFC93C");      // 金色光晕底色
    private static readonly Color FlashBorder = Color.FromHtml("#FFF0BF");  // 金色亮边

    private const string CardScenePath = "res://scenes/cards/card.tscn";

    /// <summary>找不到生物根（非战斗上下文）时，允许的重试次数；之后退回挂到状态条末尾。</summary>
    private const int OverlayFallbackAttempts = 8;

    private static readonly Color CircleBg = Color.FromHtml("#1E2837");
    private static readonly Color CircleBorder = Color.FromHtml("#E8D9A0");   // 奶油金描边，呼应抗性条数字
    private static readonly Color CircleNum = Color.FromHtml("#F7E9D1");
    private static readonly Color CircleNumOutline = Color.FromHtml("#6E1A12");

    private readonly NCreatureStateDisplay _display;
    private readonly ResistanceBarVisual _bar;
    private readonly Creature _creature;

    private readonly Control _overlay;     // 塞在 display 最上层（最后一个子节点）
    private readonly Panel _circle;
    private readonly Label _number;
    private readonly Control _cardLayer;   // 所有火花牌 NCard 的父节点，整层显隐/渐显
    private readonly List<NCard> _cards = new();

    private readonly StyleBoxFlat _circleStyle;
    private bool _detached;

    // —— 悬停计数：状态条血条框、生物身体、计数圆 三者任一悬停即视为“悬停该生物” ——
    private int _barHover;
    private int _bodyHover;
    private int _circleHover;
    private bool _hoverHooked;
    private Action? _hpEnter;
    private Action? _hpExit;
    private Action? _bodyEnter;
    private Action? _bodyExit;
    private Action? _circleEnter;
    private Action? _circleExit;
    private bool _bodyHooked;

    private bool _overlayParented; // _overlay 是否已挂到生物根（%Intents 之后）
    private int _overlayHookAttempts;
    private bool _alive = true;
    private bool _revealed;
    private bool _layerVisible;
    private Tween? _layerTween;
    private NHoverTipSet? _tipSet;

    // —— 计数圆“添加闪烁”：临时金色光环 + 其补间 ——
    private Panel? _flash;
    private Tween? _flashTween;

    private SparkHudDriver(NCreatureStateDisplay display, ResistanceBarVisual bar, Creature creature)
    {
        _display = display;
        _bar = bar;
        _creature = creature;

        _overlay = new Control
        {
            Name = NodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore, // 空白处不拦鼠标
        };

        // —— 计数圆 ——
        _circleStyle = new StyleBoxFlat
        {
            BgColor = CircleBg,
            BorderColor = CircleBorder,
            AntiAliasing = true,
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowSize = 3,
        };
        _circleStyle.SetCornerRadiusAll(Mathf.RoundToInt(CircleDiameter / 2f));
        _circleStyle.SetBorderWidthAll(2);

        _circle = new Panel
        {
            Name = "SparkCountCircle",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _circle.AddThemeStyleboxOverride("panel", _circleStyle);

        _number = new Label
        {
            Name = "SparkCountNum",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _number.AddThemeColorOverride("font_color", CircleNum);
        _number.AddThemeColorOverride("font_outline_color", CircleNumOutline);
        _number.AddThemeConstantOverride("outline_size", 6);
        _circle.AddChild(_number);
        _number.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _number.AddThemeFontSizeOverride("font_size", 16);

        // —— 卡行层 ——
        _cardLayer = new Control
        {
            Name = "SparkCardLayer",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _overlay.AddChild(_cardLayer);
        _overlay.AddChild(_circle);

        // 火花 UI 不直接挂到状态条子树：生物根里状态条排在 %Intents（意图）之前，火花若是状态条
        // 的子节点会被意图整个盖住。故 _overlay 延后挂到生物根、紧随 %Intents 之后（见 TryHookOverlay），
        // 让火花牌排在意图节点之后绘制（即意图之上）。挂好前不设任何全局坐标。
        _circle.Size = Vector2.One * CircleDiameter;
    }

    public static SparkHudDriver Attach(NCreatureStateDisplay display, ResistanceBarVisual bar, Creature creature)
    {
        var driver = new SparkHudDriver(display, bar, creature);
        driver.Init();
        return driver;
    }

    // ============ 外部通知 ============

    /// <summary>抗性条每次重排（LayoutUnderHpBar 末尾）时调用：跟着血条几何重新锚定圆与卡行。</summary>
    public void RefreshAnchor()
    {
        if (_detached)
        {
            return;
        }
        TryHookBodyOnce();
        TryHookOverlay();
        if (_overlay.GetParent() == null)
        {
            return; // 还没挂到生物根：此刻全局坐标无意义，TryHookOverlay 成功后会用 deferred 再排一次
        }
        if (!ValidBar())
        {
            return;
        }

        Vector2 barPos = _bar.Root.GlobalPosition;
        float barW = Mathf.Max(1f, _bar.Root.Size.X);
        float barH = Mathf.Max(1f, _bar.Root.Size.Y);
        Vector2 view = ViewSize();

        // 1) 计数圆：中心对准抗性条左端，垂直对准条中心。
        float d = CircleDiameter;
        float cx = barPos.X - d * 0.5f; // 左半探出条外、右半压住条尖 = “挂在左端的小圆钮”
        if (view.X > 0)
        {
            cx = Mathf.Clamp(cx, RowMinMargin, Mathf.Max(RowMinMargin, view.X - d - RowMinMargin));
        }
        float cy = barPos.Y + barH * 0.5f;
        _circle.GlobalPosition = new Vector2(cx, cy - d * 0.5f);

        // 2) 卡行：以抗性条几何为基准（其宽=生物宽 → 中心=生物中心），排在上方。
        LayoutCards(barPos, barW, view);
    }

    /// <summary>某生物火花数据变化时由系统回调（匹配则重建）。</summary>
    public void OnSparksChanged(Creature c)
    {
        if (_detached || c != _creature)
        {
            return;
        }
        RefreshSparks();
    }

    /// <summary>
    /// 给“本生物”塞入火花成功（且 owner 是本地玩家）后由系统回调：
    /// 计数圆闪一下（金色光环外扩），并在生物身上播放第4点通用 Attach 特效。
    /// 注意 GiveSpark 里 SparksChanged 先于本事件，因此此刻圆与卡行已刷新完毕。
    /// </summary>
    public void OnSparkAdded(Creature c, Player owner, CardModel spark)
    {
        if (_detached || c != _creature || !LocalContext.IsMe(owner))
        {
            return;
        }
        FlashCircle();
        ReedAttachVfx.Play(_creature, spark);
    }

    /// <summary>计数圆“添加闪烁”：在圆背后插入一个金色圆环光晕，快速外扩并淡出（约0.32s）。</summary>
    private void FlashCircle()
    {
        if (_detached || !ValidBar())
        {
            return;
        }
        _flashTween?.Kill();
        _flashTween = null;
        if (_flash != null && GodotObject.IsInstanceValid(_flash))
        {
            _flash.QueueFree();
        }
        _flash = null;

        float d = CircleDiameter;
        var glow = new Panel
        {
            Name = "SparkCountFlash",
            MouseFilter = Control.MouseFilterEnum.Ignore, // 不拦鼠标
            Size = Vector2.One * d,
        };
        var style = new StyleBoxFlat
        {
            BgColor = FlashBg,
            BorderColor = FlashBorder,
            AntiAliasing = true,
        };
        style.SetCornerRadiusAll(Mathf.RoundToInt(d / 2f));
        style.SetBorderWidthAll(2);
        glow.AddThemeStyleboxOverride("panel", style);

        // 以圆心为缩放原点：圆心贴着计数圆圆心 → 放大时从圆周围向外长出一圈光晕。
        glow.PivotOffset = Vector2.One * (d * 0.5f);
        glow.Position = _circle.Position + Vector2.One * (d * 0.5f);

        _overlay.AddChild(glow);
        _overlay.MoveChild(glow, _circle.GetIndex()); // 插到计数圆前一层：当“背景光”，不遮数字
        _flash = glow;

        glow.Modulate = new Color(1f, 1f, 1f, FlashPeakAlpha);
        glow.Scale = Vector2.One * 0.9f;

        _flashTween = glow.CreateTween().SetParallel();
        _flashTween.TweenProperty(glow, "modulate:a", 0f, FlashFade)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        _flashTween.TweenProperty(glow, "scale", Vector2.One * FlashScale, FlashFade)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        _flashTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(glow))
            {
                glow.QueueFree();
            }
            if (_flash == glow)
            {
                _flash = null;
            }
        }));
    }

    // ============ 组装 ============

    private void RefreshSparks()
    {
        if (_detached)
        {
            return;
        }
        SyncCards();
        RefreshAnchor();
        RefreshCardShown();
    }

    /// <summary>让 _cards 与本地玩家当前火花牌集合同步；未出现在新集合里的卡播放消散动画。</summary>
    private void SyncCards()
    {
        IReadOnlyList<CardModel> models = LocalSparks();
        bool shown = _cardLayer.Visible;

        // 移除：模型不再属于火花 → 淡出上浮后释放（若层隐藏则直接释放，省去看不见的动画）。
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            CardModel model = _cards[i].Model;
            if (model != null && ContainsRef(models, model))
            {
                continue;
            }
            NCard card = _cards[i];
            _cards.RemoveAt(i);
            if (shown)
            {
                VanishCard(card);
            }
            else
            {
                card.QueueFree();
            }
        }

        // 新增：按模型在火花列表中的顺序追加（尾部即最右）。
        foreach (CardModel model in models)
        {
            if (FindCardByModel(model) != null)
            {
                continue;
            }
            NCard card = CreateSparkCard(model);
            _cards.Add(card);
            if (shown)
            {
                PlayAppear(card);
            }
        }

        UpdateBadge();
    }

    private NCard CreateSparkCard(CardModel model)
    {
        PackedScene scene = PreloadManager.Cache.GetScene(CardScenePath);
        NCard card = scene.Instantiate<NCard>(PackedScene.GenEditState.Disabled);
        card.Scale = Vector2.One * CardScale;
        card.Model = model;   // 先设模型：入树时 _Ready→Reload 即以该模型渲染（官方 Create 同款顺序）
        _cardLayer.AddChild(card);
        card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);     // 已入树，此处刷新描述文本
        return card;
    }

    private void LayoutCards(Vector2 barPos, float barW, Vector2 view)
    {
        int n = _cards.Count;
        if (n == 0)
        {
            return;
        }
        float w = NCard.defaultSize.X * CardScale;
        float h = NCard.defaultSize.Y * CardScale;
        float totalW = n * w + (n - 1) * CardGap;

        float xCenter = barPos.X + barW * 0.5f;
        float x0 = xCenter - totalW * 0.5f;
        float yBottom = barPos.Y - RowAboveBar;
        float y0 = yBottom - h;

        if (view.X > 0)
        {
            // 左侧不压到计数圆：圆可见时，行从圆右缘再往右排起，避免放大后的卡与圆互相遮盖。
            float leftMin = RowMinMargin;
            if (_circle.Visible)
            {
                leftMin = Mathf.Max(leftMin, _circle.GlobalPosition.X + CircleDiameter + CardGap);
            }
            x0 = Mathf.Clamp(x0, leftMin, Mathf.Max(leftMin, view.X - totalW - RowMinMargin));
        }
        if (view.Y > 0)
        {
            y0 = Mathf.Clamp(y0, RowMinMargin, Mathf.Max(RowMinMargin, view.Y - h - RowMinMargin));
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].GlobalPosition = new Vector2(x0 + i * (w + CardGap), y0);
        }
    }

    // ============ 显隐 / 悬停 ============

    private void RefreshReveal()
    {
        bool revealed = _barHover > 0 || _bodyHover > 0 || _circleHover > 0;
        if (revealed == _revealed)
        {
            return;
        }
        _revealed = revealed;
        RefreshCardShown();
    }

    private void RefreshCardShown()
    {
        bool want = _alive && _revealed && _cards.Count > 0;
        if (want == _layerVisible)
        {
            return;
        }
        _layerVisible = want;

        _layerTween?.Kill();
        _layerTween = null;

        if (want)
        {
            _cardLayer.Visible = true;
            _cardLayer.Modulate = new Color(1f, 1f, 1f, 0f);
            _layerTween = _cardLayer.CreateTween();
            _layerTween.TweenProperty(_cardLayer, "modulate:a", 1f, RevealFade);
        }
        else
        {
            _layerTween = _cardLayer.CreateTween();
            _layerTween.TweenProperty(_cardLayer, "modulate:a", 0f, RevealFade * 0.6f);
            _layerTween.TweenCallback(Callable.From(() =>
            {
                _cardLayer.Visible = false;
                _cardLayer.Modulate = Colors.White;
            }));
        }
    }

    private void PlayAppear(NCard card)
    {
        card.Modulate = new Color(1f, 1f, 1f, 0f);
        Tween t = card.CreateTween();
        t.TweenProperty(card, "modulate:a", 1f, RevealFade);
    }

    private void VanishCard(NCard card)
    {
        Tween t = card.CreateTween().SetParallel();
        t.TweenProperty(card, "modulate:a", 0f, VanishFade).SetEase(Tween.EaseType.In);
        t.TweenProperty(card, "position:y", card.Position.Y - VanishRise, VanishFade)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        t.Chain().TweenCallback(Callable.From(card.QueueFree));
    }

    /// <summary>刷新计数圆的显隐与数字（默认 = 本地火花数量；留接口给将来显示伤害值等）。</summary>
    private void UpdateBadge()
    {
        if (_detached || !ValidBar())
        {
            return;
        }
        int count = LocalSparkCount();
        int display = ResistanceSystem.SparkBadgeValueProvider?.Invoke(_creature) ?? count;
        _number.Text = display.ToString();
        _circle.Visible = _alive && count > 0 && _bar.Root.Visible;
    }

    // ============ 悬停接线（复刻原版：状态条血条框 + 生物身体 Hitbox） ============

    private void HookHoverControls()
    {
        if (_hoverHooked)
        {
            return;
        }
        _hoverHooked = true;

        Control hp = _display.GetNode<Control>("%HpBarHitbox");
        _hpEnter = () => { _barHover++; RefreshReveal(); };
        _hpExit = () => { _barHover = Math.Max(0, _barHover - 1); RefreshReveal(); };
        hp.MouseEntered += _hpEnter;
        hp.MouseExited += _hpExit;

        _circleEnter = () =>
        {
            _circleHover++;
            ShowSparkTip();
            RefreshReveal();
        };
        _circleExit = () =>
        {
            _circleHover = Math.Max(0, _circleHover - 1);
            HideSparkTip();
            RefreshReveal();
        };
        _circle.MouseEntered += _circleEnter;
        _circle.MouseExited += _circleExit;

        TryHookBodyOnce();
    }

    /// <summary>生物身体 Hitbox 可能比血条晚一点进入 NCombatRoom 注册表，首次拿不到就延后重试。</summary>
    private void TryHookBodyOnce()
    {
        if (_bodyHooked || !_hoverHooked)
        {
            return;
        }
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(_creature);
        if (node?.Hitbox == null)
        {
            return;
        }
        Control hitbox = node.Hitbox;
        _bodyEnter = () => { _bodyHover++; RefreshReveal(); };
        _bodyExit = () => { _bodyHover = Math.Max(0, _bodyHover - 1); RefreshReveal(); };
        hitbox.MouseEntered += _bodyEnter;
        hitbox.MouseExited += _bodyExit;
        _bodyHooked = true;
    }

    /// <summary>
    /// 把火花 UI 挂到生物根 Control 上、紧随 %Intents（意图）之后 —— 生物根的绘制顺序是
    /// 状态条(下) → Hitbox → %Intents → RemoteCards → SelectionReticle(上)。火花若留在状态条子树里
    /// 会被 %Intents 盖住；这里挂在 %Intents 之后即“在意图节点之后绘制”，意图就挡不住火花牌了。
    /// 生物节点可能在建条时尚未注册，拿不到就留待下次 RefreshAnchor 再试（幂等）。
    /// </summary>
    private void TryHookOverlay()
    {
        if (_overlayParented || _detached || _overlay.GetParent() != null)
        {
            return;
        }
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(_creature);
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            // 非战斗上下文（图鉴/预览等）没有 NCombatRoom 的生物节点：重试若干次后退回旧的
            // “挂到状态条最后”的父级（这类生物没有意图层，不存在被盖住的问题）。
            if (++_overlayHookAttempts >= OverlayFallbackAttempts)
            {
                _display.AddChild(_overlay);
                _overlayParented = true;
                Callable.From(RefreshAnchor).CallDeferred();
            }
            return;
        }
        node.AddChild(_overlay);
        Control? intents = node.GetNodeOrNull<Control>("%Intents");
        if (intents != null && GodotObject.IsInstanceValid(intents))
        {
            node.MoveChild(_overlay, intents.GetIndex() + 1); // 紧跟意图层：火花画在意图上方
        }
        _overlayParented = true;
        // 刚入树，立刻按当前几何重排一次（此前不在树内，全局坐标无从谈起）。
        Callable.From(RefreshAnchor).CallDeferred();
    }

    private void ShowSparkTip()
    {
        if (_detached || !ValidBar())
        {
            return;
        }
        int count = LocalSparkCount();
        LocString title = new("static_hover_tips", "REED-SPARK.title");
        LocString desc = new("static_hover_tips", "REED-SPARK.description");
        desc.Add("SparkCount", (decimal)count);
        HoverTip tip = new(title, desc);
        _tipSet = NHoverTipSet.CreateAndShow(_circle, tip);
    }

    private void HideSparkTip()
    {
        if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
        {
            NHoverTipSet.Remove(_circle);
        }
        _tipSet = null;
    }

    // ============ 生物死亡/复活 ============

    private void OnDied(Creature creature)
    {
        _alive = false;
        _revealed = false;
        _barHover = _bodyHover = _circleHover = 0;
        HideSparkTip();
        _cardLayer.Visible = false;
        _cardLayer.Modulate = Colors.White;
        _circle.Visible = false;
    }

    private void OnRevived(Creature creature)
    {
        _alive = true;
        UpdateBadge();
        RefreshCardShown();
    }

    // ============ 生命周期 ============

    internal void Init()
    {
        ResistanceSystem.SparksChanged += OnSparksChanged;
        ResistanceSystem.SparkAdded += OnSparkAdded;
        _creature.Died += OnDied;
        _creature.Revived += OnRevived;

        HookHoverControls();
        TryHookOverlay(); // 尽量尽早把火花 UI 挂到生物根（拿不到就靠 RefreshAnchor 反复试）
        RefreshSparks();
    }

    internal void Detach()
    {
        if (_detached)
        {
            return;
        }
        _detached = true;

        ResistanceSystem.SparksChanged -= OnSparksChanged;
        ResistanceSystem.SparkAdded -= OnSparkAdded;
        _creature.Died -= OnDied;
        _creature.Revived -= OnRevived;

        _flashTween?.Kill();
        _flashTween = null;
        if (_flash != null && GodotObject.IsInstanceValid(_flash))
        {
            _flash.QueueFree();
        }
        _flash = null;

        HideSparkTip();
        if (_hoverHooked)
        {
            if (GodotObject.IsInstanceValid(_display))
            {
                Control? hp = _display.GetNodeOrNull<Control>("%HpBarHitbox");
                if (hp != null)
                {
                    if (_hpEnter != null) hp.MouseEntered -= _hpEnter;
                    if (_hpExit != null) hp.MouseExited -= _hpExit;
                }
            }
            if (_bodyHooked)
            {
                Control? body = NCombatRoom.Instance?.GetCreatureNode(_creature)?.Hitbox;
                if (body != null)
                {
                    if (_bodyEnter != null) body.MouseEntered -= _bodyEnter;
                    if (_bodyExit != null) body.MouseExited -= _bodyExit;
                }
            }
            _circle.MouseEntered -= _circleEnter;
            _circle.MouseExited -= _circleExit;
        }

        if (GodotObject.IsInstanceValid(_overlay))
        {
            if (_overlay.GetParent() != null)
            {
                _overlay.QueueFree();
            }
            else
            {
                _overlay.Free(); // 从未挂上生物根（如建条时生物节点始终缺位）：直接释放，避免泄漏
            }
        }
    }

    // ============ 内部工具 ============

    private bool ValidBar() => !_detached && _bar != null && GodotObject.IsInstanceValid(_bar.Root);

    private Vector2 ViewSize()
    {
        Viewport? vp = _overlay.GetViewport();
        return vp != null ? vp.GetVisibleRect().Size : Vector2.Zero;
    }

    private IReadOnlyList<CardModel> LocalSparks()
    {
        Player? me = LocalContext.GetMe(_creature.CombatState);
        return me == null ? Array.Empty<CardModel>() : ResistanceSystem.SparksOf(_creature, me);
    }

    private int LocalSparkCount()
    {
        Player? me = LocalContext.GetMe(_creature.CombatState);
        return me == null ? 0 : ResistanceSystem.CountSparks(_creature, me);
    }

    private NCard? FindCardByModel(CardModel model)
    {
        foreach (NCard card in _cards)
        {
            if (card.Model == model)
            {
                return card;
            }
        }
        return null;
    }

    private static bool ContainsRef(IReadOnlyList<CardModel> list, CardModel model)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], model))
            {
                return true;
            }
        }
        return false;
    }
}
