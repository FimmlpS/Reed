using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 抗性条 UI。直接复用生物自身 HP 条的外观与布局机制（对照 health_bar.tscn / NHealthBar.cs）：
/// HP 条的彩色填充并不是平铺矩形，而是“白色胶囊纹理(health_bar_fill.png) + self_modulate 染色”，
/// 全部被塞在带 clip_children 的 HpForegroundContainer(四边内缩 L5/T3/R5/B3)里裁成一条矩形道，
/// 端部自带圆尖，因此永远不可能伸出外层胶囊边框(health_bar_bg.png)的轮廓。
/// 本实现照搬该机制：深拷贝血条静态装饰(胶囊底)作框，框内再放一个同几何的裁剪容器(ReedLane)，
/// 抗性值/红色变化/空槽都用胶囊纹理层染色，宽由“当前抗性/上限”连续比例驱动。
/// 抗性条与血条同宽同高，整体置于血条正上方（节点顺序在血条前，先渲染被血条盖住一侧）。
/// 数值文字字号 = 血条文本字号的 0.8 倍；相对条体向上偏移一点后垂直居中。
/// 说明：本类是纯 C# 帮助类（不继承 Godot 节点），全部视觉用引擎 Control 拼装。
/// </summary>
internal sealed class ResistanceBarVisual
{
    public const string NodeName = "ReedResistanceBar";

    /// <summary>与血条顶部的间距（像素）。抗性条在血条上方，因此指血条上沿往上的留白。</summary>
    private const float GapAboveHp = 6f;

    /// <summary>单次变化动画时长（秒）。</summary>
    private const float ChangeDuration = 0.45f;

    /// <summary>数值文字相对整条几何中心再上移的像素数（视觉居中补偿，y 越小越靠上）。</summary>
    private const float NumUpShift = 12f;

    // —— 配色（可直接改）——
    private static readonly Color FillBlue = Color.FromHtml("#3375F0");         // 普通=蓝
    private static readonly Color FillPurple = Color.FromHtml("#A04AE0");       // 燃烧=紫
    private static readonly Color DrainRed = Color.FromHtml("#F23C46");         // 变化区=红
    private static readonly Color LaneDark = Color.FromHtml("#101C2A");         // 框内空槽底色
    private static readonly Color FontColor = Color.FromHtml("#F7E9D1");        // 数字=奶油色（同 HP 文字观感）
    private static readonly Color FontOutline = Color.FromHtml("#6E1A12");      // 数字描边
    private static readonly Color RimColor = Color.FromHtml("#2A606B");         // 顶层描边色（= HpBackground 的 teal，找不到时兜底）

    /// <summary>HP 条里随血量动态刷新的节点名（深拷贝时剔除，剩下的即是静态边框装饰=胶囊底）。</summary>
    private static readonly HashSet<string> DynamicHpNodeNames = new()
    {
        "HpForegroundContainer", "HpMiddleground", "HpForeground", "PoisonForeground",
        "DoomForeground", "HpLabel", "BlockContainer", "BlockLabel", "BlockOutline", "InfinityTex",
    };

    public readonly Control Root;

    private readonly NHealthBar _healthBar;
    private Control? _frame;           // 血条静态装饰的深拷贝（外层胶囊背板，垫底）
    private Control? _rim;             // 顶层空心描边（复用原版 BlockOutline 的 stroke 纹理，画在内容之上 = 最靠前的边框）
    private Control? _laneClip;        // 裁剪容器（复刻 HpForegroundContainer 的 clip 语义）
    private Control? _track;           // 空槽（胶囊纹理层/兜底矩形），全宽垫在下层
    private Control? _ghost;           // 红 = 正在变化的一段
    private Control? _fill;            // 蓝/紫 = 当前抗性
    private readonly Label _num;

    private Tween? _changeTween;

    // 框内填充道（由活血条的 HpForegroundContainer 实测得到，Root 内相对坐标）
    private float _laneX;
    private float _laneY;
    private float _laneW = 1f;
    private float _laneH = 1f;

    private int _cur;
    private int _max;
    private bool _burning;
    private float _w = -1f;            // 当前已绘制的填充宽度
    private bool _relayoutQueued;      // 首帧后补一次布局（血条容器宽度常延后一帧才定）

    private ResistanceBarVisual(Control host, NHealthBar healthBar)
    {
        _healthBar = healthBar;
        Root = new Control
        {
            Name = NodeName,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _num = new Label
        {
            Name = "ResistanceNum",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _num.AddThemeColorOverride("font_color", FontColor);
        _num.AddThemeColorOverride("font_outline_color", FontOutline);
        _num.AddThemeConstantOverride("outline_size", 2);

        Root.AddChild(_num);

        host.AddChild(Root);
        // 节点顺序排在血条之前：先渲染、被血条盖住，抗性条自身位于血条上方。
        MoveRootBeforeHealthBar(host, healthBar);
    }

    /// <summary>把抗性条 Root 插入到血条节点之前（血条在 display 下的直接子节点或其祖先）。</summary>
    private static void MoveRootBeforeHealthBar(Control host, NHealthBar healthBar)
    {
        // 找到血条在 display 下的直接子节点，把 Root 放到它前面（低 index = 先渲染 = 被压在下层）。
        Node? target = healthBar;
        while (target != null && target.GetParent() != host)
        {
            target = target.GetParent();
        }
        if (target == null)
        {
            Node root = host.GetChild(host.GetChildCount() - 1);
            host.MoveChild(root, 0); // 退路：挪到最底
            return;
        }
        int idx = target.GetIndex();
        Node ourRoot = host.GetChild(host.GetChildCount() - 1);
        host.MoveChild(ourRoot, idx);
    }

    /// <summary>建条并登记到系统。</summary>
    public static ResistanceBarVisual Create(Control host, NHealthBar healthBar, Creature creature)
    {
        var bar = new ResistanceBarVisual(host, healthBar);
        ResistanceSystem.RegisterBar(bar, creature);
        return bar;
    }

    /// <summary>把抗性条对齐到血条正上方、与血条同宽同高。</summary>
    public void LayoutUnderHpBar()
    {
        Control hc = _healthBar.HpBarContainer;
        if (hc == null || !GodotObject.IsInstanceValid(hc))
        {
            return;
        }

        float w = Mathf.Max(1f, hc.Size.X);
        float h = Mathf.Max(1f, hc.Size.Y);

        Root.Size = new Vector2(w, h);
        Root.GlobalPosition = hc.GlobalPosition + new Vector2(0f, -(h + GapAboveHp));

        // —— 1) 框：深拷贝血条容器的静态装饰（胶囊底），垫在最下 ——
        BuildFrame(hc, w, h);

        // —— 2) 实测血条内部填充道（HpForegroundContainer 的 clip 矩形），抗性填充与之对齐 ——
        MeasureLane(hc, w, h);

        // —— 3) 裁剪容器 + 胶囊填充层（复刻血条：彩色都在 clip 道内，物理上被边框约束）——
        EnsureColoredLayers(hc);
        if (_laneClip != null)
        {
            SetRect(_laneClip, _laneX, _laneY, _laneW, _laneH);
        }
        SetRect(_track, 0f, 0f, _laneW, _laneH);
        SetRect(_ghost, 0f, 0f, _laneW, _laneH);
        SetRect(_fill, 0f, 0f, _laneW, _laneH);
        ApplyColors();

        // —— 4) 文字：整条高内居中，再整体上移 NumUpShift ——
        float font = HpFontSize() * 0.9f;                 // 0.8 × 血条文本字号
        SetRect(_num, Mathf.Max(0f, _laneX - 2f), -NumUpShift, Mathf.Max(2f, _laneW + 4f), h);
        _num.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(Mathf.Clamp(font, 7f, 40f)));
        Font? hpFont = HpFont();
        if (hpFont != null)
        {
            _num.AddThemeFontOverride("font",hpFont);
        }
        _num.AddThemeConstantOverride("shadow_offset_x",6);
        _num.AddThemeConstantOverride("shadow_offset_y",4);
        _num.AddThemeConstantOverride("outline_size",16);
        _num.AddThemeConstantOverride("shadow_outline_size",0);
        
        // —— 5) 顶层空心描边（在内容之上、数字之下 = 视角上最靠前的边框）——
        BuildRim(hc);

        // 尺寸/布局变化后，把已绘制的宽度按比例重放（不回放动画）。
        _w = -1f;
        UpdateValues(animate: false);
    }

    /// <summary>建立血条静态装饰拷贝（首次）或跟随血条尺寸伸缩。找不到可用静态装饰时忽略，仅显示空槽+填充。</summary>
    private void BuildFrame(Control hc, float w, float h)
    {
        if (_frame == null)
        {
            Control? copy = hc.Duplicate() as Control;
            if (copy != null && StripDynamic(copy))
            {
                _frame = copy;
                Root.AddChild(_frame);
                Root.MoveChild(_frame, 0); // 垫底：先渲染，填充/文字在其上
            }
            else
            {
                copy?.QueueFree(); // 没有可用静态装饰，丢弃拷贝
            }
        }

        if (_frame != null)
        {
            _frame.Position = Vector2.Zero;
            _frame.Size = new Vector2(w, h);
        }
    }

    /// <summary>递归剔除动态血量节点；若整棵只剩空壳则视为没有可用静态装饰。</summary>
    private static bool StripDynamic(Control container)
    {
        var toRemove = new List<Node>();
        void Walk(Node n)
        {
            if (n != container && DynamicHpNodeNames.Contains(n.Name))
            {
                toRemove.Add(n);
                return;
            }
            int count = n.GetChildCount();
            for (int i = 0; i < count; i++)
            {
                Walk(n.GetChild(i));
            }
        }
        Walk(container);
        foreach (Node n in toRemove)
        {
            container.RemoveChild(n);
            n.QueueFree();
        }
        return container.GetChildCount() > 0;
    }

    /// <summary>
    /// 建立裁剪容器与三层胶囊填充（仅一次）。
    /// 纹理/九宫格参数取自活血条的 HpForeground（health_bar_fill.png 白色胶囊），染成抗性条配色；
    /// 取不到时退化为同构的纯色矩形，仍被裁剪容器约束在边框内。
    /// 子节点全部以 (0,0)+全道尺寸放置，宽度单独驱动（size:x），与血条用 OffsetRight 缩右端同理。
    /// </summary>
    private void EnsureColoredLayers(Control hc)
    {
        if (_laneClip != null)
        {
            return;
        }

        Texture2D? capsule = null;
        float patchLeft = 6f;
        float patchRight = 6f;
        if (FindDescendant(hc, "HpForeground") is NinePatchRect fg)
        {
            capsule = fg.Texture;
            patchLeft = fg.PatchMarginLeft;
            patchRight = fg.PatchMarginRight;
        }

        _laneClip = new Control
        {
            Name = "ReedLane",
            ClipContents = true,          // 同 HpForegroundContainer.clip_children：彩色不越道
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _track = NewCapsule(capsule, patchLeft, patchRight, LaneDark, "Track");
        _ghost = NewCapsule(capsule, patchLeft, patchRight, DrainRed, "Ghost");
        _fill = NewCapsule(capsule, patchLeft, patchRight, FillBlue, "Fill");

        _laneClip.AddChild(_track); // 顺序：空槽(下) → 红(变化) → 值(上)
        _laneClip.AddChild(_ghost);
        _laneClip.AddChild(_fill);

        Root.AddChild(_laneClip);
        Root.MoveChild(_laneClip, _num.GetIndex()); // 置于数值之下、边框(帧)之上
    }

    /// <summary>
    /// 顶层空心描边（首次创建，之后每次布局跟随活血条 BlockOutline 几何）。
    /// 纹理直接取原版 health_bar_stroke.png（空心三角-长条-三角轮廓，与外层胶囊同轮廓）；
    /// 放到彩色内容之上、数字之下 → 边框在 z 轴最前，内容仍被裁剪道几何约束、绝不越界。
    /// 找不到 BlockOutline 时忽略（内容已由内缩+裁剪约束在框内，无描边也能看）。
    /// </summary>
    private void BuildRim(Control hc)
    {
        if (_rim == null)
        {
            if (FindDescendant(hc, "BlockOutline") is not NinePatchRect src || src.Texture == null)
            {
                return;
            }

            Color tint = RimColor;
            if (FindDescendant(hc, "HpBackground") is NinePatchRect bg)
            {
                tint = bg.Modulate; // 用外层胶囊自己的 teal，观感与血条边框同族
            }
            tint.A = 1f; // 描边压不透明，作为"最靠前"的清晰边框

            _rim = new NinePatchRect
            {
                Name = "ReedRim",
                Texture = src.Texture,
                PatchMarginLeft = src.PatchMarginLeft,
                PatchMarginRight = src.PatchMarginRight,
                SelfModulate = tint,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            Root.AddChild(_rim);
            Root.MoveChild(_rim, _num.GetIndex()); // 垫在彩色之上、数值文字之下
        }

        if (FindDescendant(hc, "BlockOutline") is Control outline)
        {
            // 同原点同尺寸（Root 与 hc 同宽高、左上重合），直接搬相对几何即可。
            Vector2 rel = outline.GlobalPosition - hc.GlobalPosition;
            _rim.Position = rel;
            _rim.Size = outline.Size;
        }
    }

    /// <summary>胶囊纹理层；无纹理时退回纯色矩形（同样在裁剪道内）。</summary>
    private static Control NewCapsule(Texture2D? tex, float patchLeft, float patchRight, Color color, string name)
    {
        if (tex != null)
        {
            return new NinePatchRect
            {
                Name = name,
                Texture = tex,
                PatchMarginLeft = (int)patchLeft,
                PatchMarginRight = (int)patchRight,
                SelfModulate = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }

        return new ColorRect
        {
            Name = name,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>统一给填充层上色（NinePatchRect 用 SelfModulate，ColorRect 用 Color）。</summary>
    private static void SetLayerColor(Control c, Color color)
    {
        if (c is NinePatchRect n)
        {
            n.SelfModulate = color;
        }
        else if (c is ColorRect r)
        {
            r.Color = color;
        }
    }

    private void ApplyColors()
    {
        SetLayerColor(_track, LaneDark);
        SetLayerColor(_ghost, DrainRed);
        SetLayerColor(_fill, _burning ? FillPurple : FillBlue);
    }

    /// <summary>从活血条读取填充道（HpForegroundContainer）的容器内相对几何；读不到时退化为中央一条。</summary>
    private void MeasureLane(Control hc, float w, float h)
    {
        Control? lane = FindDescendant(hc, "HpForegroundContainer");
        if (lane != null && GodotObject.IsInstanceValid(lane))
        {
            Vector2 rel = lane.GlobalPosition - hc.GlobalPosition;
            _laneX = rel.X;
            _laneY = rel.Y;
            _laneW = Mathf.Max(1f, lane.Size.X);
            _laneH = Mathf.Max(1f, lane.Size.Y);
            return;
        }

        // 退路：假定左右各 5px 三角区、高度取血条中段。
        _laneX = 5f;
        _laneW = Mathf.Max(1f, w - 10f);
        _laneH = Mathf.Max(1f, h * 0.5f);
        _laneY = (h - _laneH) * 0.5f;
    }

    /// <summary>血条文本当前生效字号（MegaLabel 自适应后一般已写进 theme override）。</summary>
    private float HpFontSize()
    {
        if (FindDescendant(_healthBar, "HpLabel") is Label lbl)
        {
            return lbl.GetThemeFontSize("font_size");
        }
        return 20f; // 找不到就用接近默认的字号
    }

    private Font? HpFont()
    {
        if (FindDescendant(_healthBar, "HpLabel") is Label lbl)
        {
            return lbl.GetThemeFont("font");
        }
        return null;
    }

    private static Control? FindDescendant(Node root, string name)
    {
        int count = root.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            Node c = root.GetChild(i);
            if (c is Control co && co.Name == name)
            {
                return co;
            }
            Control? deeper = FindDescendant(c, name);
            if (deeper != null)
            {
                return deeper;
            }
        }
        return null;
    }

    /// <summary>数据或显示条件变化后刷新条（颜色、长度、文字、显隐）。</summary>
    public void UpdateAll(ResistanceData data, bool show)
    {
        _cur = data.Current;
        _max = data.Max;
        _burning = data.Burning;

        bool wantShow = show;
        bool becameVisible = wantShow && !Root.Visible;
        Root.Visible = wantShow;

        ApplyColors();
        _num.Text = $"{_cur}/{_max}";
        _num.Visible = true;

        if (!wantShow)
        {
            _changeTween?.Kill();
            _changeTween = null;
            return;
        }

        if (becameVisible)
        {
            // 首次展示：立即按血条几何排一次版（避免一瞬间 0 宽条 / 无框）。
            LayoutUnderHpBar();
            QueueDeferredRelayout();
        }
        else
        {
            UpdateValues(animate: true);
        }
    }

    /// <summary>血条容器宽度由原版延后一帧（CallDeferred）设置，首次点亮后再补一次对齐。</summary>
    private void QueueDeferredRelayout()
    {
        if (_relayoutQueued)
        {
            return;
        }
        _relayoutQueued = true;
        Callable.From(() =>
        {
            _relayoutQueued = false;
            if (GodotObject.IsInstanceValid(Root))
            {
                LayoutUnderHpBar();
            }
        }).CallDeferred();
    }

    public void SetShown(bool show)
    {
        if (!show)
        {
            Root.Visible = false;
            _changeTween?.Kill();
            _changeTween = null;
            return;
        }

        bool wasVisible = Root.Visible;
        Root.Visible = true;
        if (!wasVisible)
        {
            LayoutUnderHpBar();
            QueueDeferredRelayout();
        }
    }

    private void UpdateValues(bool animate)
    {
        float ratio = _max <= 0 ? 0f : Mathf.Clamp((float)_cur / _max, 0f, 1f);
        float target = _laneW * ratio;

        if (!animate || _w < 0f)
        {
            SetWidth(_ghost, target);
            SetWidth(_fill, target);
            _w = target;
            return;
        }

        AnimateTo(target);
    }

    /// <summary>
    /// 长度从旧值走向 target，正在变化的一段由红色承担。
    /// 扣减：蓝/紫瞬间到新值，红色从旧长度缓慢缩到新长度（drain）。
    /// 恢复：红色先铺满“目标-旧值”区间，蓝/紫从旧长度长出盖住它。
    /// </summary>
    private void AnimateTo(float target)
    {
        float from = Mathf.Max(_fill.Size.X, _ghost.Size.X);
        _changeTween?.Kill();

        if (target < from)
        {
            SetWidth(_fill, target);
            _changeTween = Root.CreateTween();
            _changeTween.TweenProperty(_ghost, "size:x", target, ChangeDuration)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Expo);
        }
        else if (target > from)
        {
            SetWidth(_ghost, target);
            _changeTween = Root.CreateTween();
            _changeTween.TweenProperty(_fill, "size:x", target, ChangeDuration)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Expo);
        }
        else
        {
            SetWidth(_ghost, target);
            SetWidth(_fill, target);
        }

        _w = target;
    }

    private static void SetRect(Control c, float x, float y, float w, float h)
    {
        c.Position = new Vector2(x, y);
        c.Size = new Vector2(w, h);
    }

    private static void SetWidth(Control c, float w)
    {
        Vector2 s = c.Size;
        c.Size = new Vector2(w, s.Y);
    }
}
