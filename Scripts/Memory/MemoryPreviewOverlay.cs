using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using Reed.Scripts.Enums;
using Reed.Scripts.Patches;

namespace Reed.Scripts.Memory;

/// <summary>
/// 追忆预览的渲染层：把 <see cref="IMemorySubscriber.GetRecallPreview"/> 给出的候选牌，
/// 在**回忆牌堆图标**处按扇形环绕展开。
///
/// <list type="bullet">
///   <item>角度：0° = 图标正上方，角度增大 = **顺时针**（<c>dir = (sin θ, -cos θ)</c>），
///     所有牌的 <c>Rotation = θ</c>，于是每张牌的**底边都正对图标**（底边抵住图标外缘）；</item>
///   <item>半径：<c>图标半径 + 卡高 * FanScale / 2</c> —— 卡底刚好贴着图标；</item>
///   <item>比例很小（<see cref="FanScale"/> = 0.3），最多同时铺 <see cref="MaxCards"/> 张；</item>
///   <item>整层挂在 <c>SceneTree.Root</c> 下的高数值 <see cref="CanvasLayer"/> 上，
///     因此能盖住原版所有界面（原版 UI 都在默认画布层上靠子节点顺序叠放）；</item>
///   <item>**全部节点 MouseFilter = Ignore**：预览一旦抢到鼠标，被悬浮的那张牌会立刻收到
///     FocusExited，预览就会闪一下消失。</item>
///   <item>每张牌都是从 <see cref="NCard"/> 的**池子**里借的：摆位前一律
///     <see cref="NormalizeCard"/> 回规范状态（重点是 pivot 复位）—— 池子不保证干净，
///     上一次使用者留下的 pivot 会让「旋转后的卡」整体偏出画面。</item>
/// </list>
///
/// <para>显示/隐藏的时机由 <c>MemoryPreviewPatches</c>（patch <see cref="NCardHolder.DoCardHoverEffects"/>）
/// 驱动：悬浮进入 → 取候选并展开；悬浮离开、被悬浮的牌离开场景树（被打出 / 界面关闭）→ 收起。</para>
/// </summary>
internal sealed partial class MemoryPreviewOverlay : CanvasLayer
{
    /// <summary>覆盖层数值：压过全部原版界面（原版不存在数值这么大的 CanvasLayer）。</summary>
    private const int OverlayLayer = 90;

    /// <summary>预览卡的比例（比牌堆界面的 0.8 小得多，绕一圈也不占地方）。</summary>
    private const float FanScale = 0.3f;

    /// <summary>相邻两张预览牌的夹角；张数少时按 <see cref="MaxSweepDeg"/> 收窄，摆得更集中。</summary>
    private const float AngleStepDeg = 23f;

    /// <summary>扇形最大张角（顺时针，从正上方起）。图标贴着屏幕左缘，所以只往右半边铺。</summary>
    private const float MaxSweepDeg = 170f;

    /// <summary>图标半径（回忆按钮 80x80 → 40，再留 6px 空隙）。</summary>
    private const float IconRadius = 46f;

    private const int MaxCards = 8;

    /// <summary>
    /// 展开后连续「重设坐标」的帧数。新造出来的卡牌，卡面文字排版 / 中文的字体替换往往要到下一两帧
    /// 才落定，落定过程中卡牌自身布局会动 —— 玩家看到的就是「第一次悬浮位置错位、再悬浮一次才对」。
    /// 所以展开后这几帧里每帧都重刷一次卡面 + 重设一次坐标，第一次悬浮就落到最终位置。
    /// </summary>
    private const int SettleFrames = 3;

    private static MemoryPreviewOverlay? _instance;
    private static NCardHolder? _owner;
    private static Callable? _ownerExitCallable;

    private Control _fan = null!;
    private readonly List<NCard> _cards = new();

    /// <summary>本轮展开时每张预览牌的目标变换（按索引）。位置只由**索引**决定，与节点是从池子里
    /// 复用来的哪一个无关 —— 这样池子的 LIFO 顺序怎么变，同一张牌都落在同一个位置。</summary>
    private readonly List<CardSlot> _slots = new();

    /// <summary>还剩几帧「落定」重设（见 <see cref="SettleFrames"/>）。</summary>
    private int _settleFrames;

    /// <summary>一张预览牌的目标变换。</summary>
    private readonly struct CardSlot
    {
        public CardSlot(NCard card, Vector2 offset, float rotation, Vector2 scale)
        {
            Card = card;
            Offset = offset;
            Rotation = rotation;
            Scale = scale;
        }

        public readonly NCard Card;

        /// <summary>相对**锚点**（回忆按钮中心）的位移：锚点每帧重算，按钮刚出现 / 刚挪位时扇形跟着落对地方。</summary>
        public readonly Vector2 Offset;

        public readonly float Rotation;
        public readonly Vector2 Scale;
    }

    // ============ 对外入口（patch 调用） ============

    /// <summary>悬浮状态变化：进入时取预览并展开，离开时收起。</summary>
    public static void OnHoverChanged(NCardHolder? holder, bool isHovered)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder))
        {
            return;
        }
        if (!isHovered)
        {
            if (ReferenceEquals(_owner, holder))
            {
                HidePreview();
            }
            return;
        }

        // 悬浮换到了另一张牌：旧的那张此刻已经不再被悬浮。原版是先给旧牌发离开、再给新牌发进入，
        // 这里再兜一层，避免新牌是普通牌 / 取不到预览时把上一张的预览留在屏幕上。
        if (_owner != null && !ReferenceEquals(_owner, holder))
        {
            HidePreview();
        }

        CardModel? model = holder.CardModel;
        if (model is not IMemorySubscriber subscriber)
        {
            return; // 普通牌：没有追忆预览
        }

        MemoryPreviewContext? context = MemoryPreviewContext.FromCard(model);
        if (context == null)
        {
            return;
        }

        IReadOnlyList<CardModel>? preview;
        try
        {
            preview = subscriber.GetRecallPreview(context);
        }
        catch (Exception e)
        {
            // 卡牌的预览逻辑出错不该把悬浮效果一起带走。
            GD.PushWarning($"[MemoryPreview] {model.Id} 的追忆预览失败：{e.Message}");
            return;
        }

        if (preview == null || preview.Count == 0)
        {
            // 没得追忆（比如上一回合没打过牌）：不显示，也别留着上一次的预览。
            if (ReferenceEquals(_owner, holder))
            {
                HidePreview();
            }
            return;
        }

        Show(holder, preview);
    }

    /// <summary>收起预览并断掉与被悬浮卡牌的联动。</summary>
    public static void HidePreview()
    {
        DetachOwnerExit();
        _owner = null;
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
        {
            _instance.ClearCards();
        }
    }

    // ============ 展开 ============

    private static void Show(NCardHolder holder, IReadOnlyList<CardModel> models)
    {
        MemoryPreviewOverlay? overlay = EnsureInstance(holder);
        if (overlay == null)
        {
            return;
        }

        // 换了一张牌：先把上一张的离开监听摘掉（旧预览会在 Build 里被清空）。
        DetachOwnerExit();
        _owner = holder;
        _ownerExitCallable = Callable.From(() =>
        {
            if (ReferenceEquals(_owner, holder))
            {
                HidePreview();
            }
        });
        holder.Connect(Node.SignalName.TreeExiting, _ownerExitCallable.Value);

        overlay.Build(holder, models);
    }

    /// <summary>被悬浮的牌被打出 / 界面关闭时，它自己会离开场景树 —— 预览跟着收起。</summary>
    private static void DetachOwnerExit()
    {
        if (_owner != null
            && GodotObject.IsInstanceValid(_owner)
            && _ownerExitCallable is { } callable
            && _owner.IsConnected(Node.SignalName.TreeExiting, callable))
        {
            _owner.Disconnect(Node.SignalName.TreeExiting, callable);
        }
        _ownerExitCallable = null;
    }

    private static MemoryPreviewOverlay? EnsureInstance(Node anyNodeInTree)
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
        {
            return _instance;
        }
        // 挂到 SceneTree.Root（Window）上：与游戏所有界面平级，靠 CanvasLayer 数值压在最上面。
        Window? root = anyNodeInTree.GetTree()?.Root;
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            return null;
        }
        MemoryPreviewOverlay overlay = new() { Name = "ReedMemoryPreview", Layer = OverlayLayer };
        root.AddChildSafely(overlay);
        _instance = overlay;
        return overlay;
    }

    public override void _Ready()
    {
        _fan = new Control
        {
            Name = "Fan",
            // 本类继承 CanvasLayer（不是 Control），所以 Control 的嵌套枚举得写全名。
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fan.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        this.AddChildSafely(_fan);

        // 回忆一变（追忆消耗掉一条、又打出新牌被记住）就重算预览 —— 预览里的牌必须与回忆同步。
        MemorySystem.MemoryChanged += OnMemoryChanged;
    }

    public override void _ExitTree()
    {
        MemorySystem.MemoryChanged -= OnMemoryChanged;
    }

    private static void OnMemoryChanged(Player player)
    {
        // 只刷新「当前正悬浮着的那张牌」的预览；没有预览在场时什么都不做。
        if (_owner == null || !GodotObject.IsInstanceValid(_owner) || !_owner.IsInsideTree())
        {
            return;
        }
        OnHoverChanged(_owner, true);
    }

    private void Build(NCardHolder holder, IReadOnlyList<CardModel> models)
    {
        ClearCards();
        if (_fan == null || !GodotObject.IsInstanceValid(_fan))
        {
            return;
        }

        int count = Mathf.Min(models.Count, MaxCards);
        float cardHeight = NCard.defaultSize.Y * FanScale;
        float radius = IconRadius + cardHeight * 0.5f;
        // 张数多时按步长铺开、最多 <MaxSweepDeg>；只有 1 张就正对上方。
        float step = count <= 1 ? 0f : Mathf.Min(AngleStepDeg, MaxSweepDeg / (count - 1));

        for (int i = 0; i < count; i++)
        {
            NCard? card = NCard.Create(models[i], ModelVisibility.Visible);
            if (card == null)
            {
                continue; // TestMode 下原版工厂返回 null
            }

            float degrees = i * step;
            float radians = Mathf.DegToRad(degrees);
            // 0° 在正上方，角度增大顺时针（屏幕 y 向下，所以上方是 (0,-1)）。
            Vector2 direction = new(Mathf.Sin(radians), -Mathf.Cos(radians));

            card.Name = $"PreviewCard{i}";
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            // 复用的实例可能还挂着上一次的出牌 Tween（它在 position / scale 上），先掐掉再摆位。
            card.PlayPileTween?.Kill();
            card.PlayPileTween = null;

            _fan.AddChildSafely(card);
            NormalizeCard(card); // 池里借来的卡可能带着上一次使用留下的 pivot，先消毒再摆位（见方法注释）
            // 入树之后才有效（NCard.UpdateVisuals 开头有 IsNodeReady 守卫）。
            card.UpdateVisuals(ReedPileType.Memory, CardPreviewMode.Normal);

            _cards.Add(card);
            _slots.Add(new CardSlot(card, direction * radius, radians, Vector2.One * FanScale));
        }

        // 变换**最后**设：入树 / 刷视觉这两步都可能让卡牌重算自身布局（Godot 会顺着 grow 方向
        // 回写 position），先设会被它们冲掉 —— 外观就是「回忆一变预览就错位」。
        ApplySlots();

        // 之后的几帧里继续刷新（见 SettleFrames）：每次悬浮进入都会重排一遍，
        // 于是「第一次悬浮错位、第二次才对」这种「要等一帧才落定」的问题不会再出现。
        _settleFrames = SettleFrames;
        SetProcess(true);
    }

    /// <summary>
    /// 落定：展开后的头几帧里，每帧重刷一次卡面、重算一次锚点、重设一次坐标。
    /// 卡面重刷是必须的 —— 新造出来的 <see cref="NCard"/> 在中文下要等字体替换 / 文本自适应落地后才排版正确，
    /// 这正是一次「再悬浮」能让预览变正常的原因；这里把那一帧的效果提前补齐。
    /// </summary>
    public override void _Process(double delta)
    {
        if (_settleFrames <= 0)
        {
            SetProcess(false);
            return;
        }
        _settleFrames--;

        foreach (CardSlot slot in _slots)
        {
            if (GodotObject.IsInstanceValid(slot.Card))
            {
                slot.Card.UpdateVisuals(ReedPileType.Memory, CardPreviewMode.Normal);
            }
        }
        ApplySlots();
    }

    /// <summary>
    /// 把一张从池里借来的预览牌恢复成 <c>card.tscn</c> 的**规范状态**：pivot = 0、绕原点居中、
    /// 不透明、不受父变换影响。
    ///
    /// <para><b>为什么每次都要做</b>：<see cref="NCard"/> 的还池只复位
    /// <c>Position / Rotation / Scale / Modulate</c>（<see cref="NCard.OnReturnedFromPool"/>），
    /// **不复位 <c>PivotOffset</c>**。而 Control 的旋转/缩放是绕 pivot 的：pivot 一旦被挪到
    /// 卡片半尺寸 (150,211)，美术中心就不再落在 <c>Position</c> 上，而是 <c>Position + P − R·S·P</c>
    /// （θ=45°、s=0.3 时偏出约 163×134 px）。池是 LIFO 复用，谁拿到脏卡取决于池序 ——
    /// 表现就是「同一张牌，有时在这个位置、有时飞到别处」。所以这里每次都强制回规范状态，
    /// 摆位结果只由**索引**决定。<see cref="Reed.Scripts.Patches.CardPoolHygienePatch"/>
    /// 已在还池时做根治，这里是同一层保险（也挡住别的来源写进来的脏状态）。</para>
    /// </summary>
    private static void NormalizeCard(NCard card)
    {
        card.PivotOffset = Vector2.Zero; // card.tscn 根节点就是 0：美术在 CardContainer 里绕原点居中
        card.Position = Vector2.Zero;
        card.Rotation = 0f;
        card.Scale = Vector2.One;
        card.Modulate = Colors.White;
        card.SelfModulate = Colors.White;
        card.Visible = true;
        card.TopLevel = false;
        card.ZIndex = 0;
        card.ZAsRelative = true;
    }

    /// <summary>把每张预览牌摆到它该在的位置（位置 = 锚点 + 索引决定的偏移，与节点身份无关）。</summary>
    private void ApplySlots()
    {
        if (!GodotObject.IsInstanceValid(this) || _owner == null || !GodotObject.IsInstanceValid(_owner))
        {
            return; // 悬浮的主人已经不在了：收起由 TreeExiting 负责，这里什么都不动
        }
        Vector2 anchor = ResolveAnchor(_owner);
        foreach (CardSlot slot in _slots)
        {
            if (!GodotObject.IsInstanceValid(slot.Card))
            {
                continue;
            }
            // 每帧都回一遍规范状态（见 NormalizeCard）：展开后头几帧卡面排版还在落定，
            // 期间被带进来的脏 pivot / 残留变换都会在这一遍里被抹平。
            NormalizeCard(slot.Card);
            slot.Card.Scale = slot.Scale;
            slot.Card.Rotation = slot.Rotation;   // 底边始终朝向图标
            slot.Card.GlobalPosition = anchor + slot.Offset; // 用全局坐标：不受 _fan 自身变换影响
        }
    }

    /// <summary>
    /// 扇形圆心 = **回忆牌堆图标中心**（屏幕坐标）。图标不在场时（理论上不会）退回被悬浮卡牌自身，
    /// 至少不让预览跑到屏幕左上角去。
    /// </summary>
    private static Vector2 ResolveAnchor(NCardHolder holder)
    {
        MemoryPileButton? button = MemoryPileButton.Instance;
        if (button != null && GodotObject.IsInstanceValid(button) && button.IsInsideTree())
        {
            // 按钮 80x80，原点在左上角 → 局部中心 (40,40) 换算到画布坐标。
            return button.GetGlobalTransformWithCanvas() * (button.Size * 0.5f);
        }
        NCard? card = holder.CardNode;
        if (card != null && GodotObject.IsInstanceValid(card))
        {
            return card.GetGlobalTransformWithCanvas().Origin; // 卡牌的局部原点就是卡牌中心
        }
        return holder.GetGlobalTransformWithCanvas().Origin;
    }

    private void ClearCards()
    {
        _slots.Clear();
        _settleFrames = 0; // 收起后不再需要逐帧重设
        // 以 **_fan 的实际子节点**为准来清，而不是以 _cards 为准：只要两者一旦失同步，
        // 上一轮的牌就会留在屏幕上（看起来正是「预览错位 / 两张牌来回换」）。
        // _fan 是这一层专用的容器，除预览牌外不放别的东西。
        if (_fan != null && GodotObject.IsInstanceValid(_fan))
        {
            foreach (Node child in _fan.GetChildren())
            {
                _fan.RemoveChildSafely(child);
                // NCard 是 IPoolable → 这个扩展会把它先出树再交还 NodePool。
                child.QueueFreeSafely();
            }
        }
        _cards.Clear();
    }
}
