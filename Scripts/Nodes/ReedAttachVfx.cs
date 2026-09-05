using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.TestSupport;

namespace Reed.Scripts.Nodes;

/// <summary>
/// 通用“关联（Attach）”特效：把一张卡牌作为特效内容，注入到某生物身上——
/// 卡牌从 scale 0 → <see cref="AppearMaxScale"/> 在目标位置非常快速地放大出现，
/// 放大完成后立刻在该位置播放原版卡牌“耗尽（exhaust）”特效（连同这张卡一起消散）。
///
/// 用途：提示玩家“有一张卡牌与目标生物产生了关联”（例如火花牌塞入）。与生物有关的 VFX
/// 一律通过 <see cref="Creature.GetVfxContainer()"/> 挂到生物的 VFX 容器（而非 instance.Ui）。
///
/// 对齐原理（核对 shipped card.tscn / vfx_card_exhaust.tscn，并经玩家实测确认）：
///  * 卡面美术填充在 %CardContainer（内容原点 = 卡 transform 原点 = card.GlobalPosition）。
///  * 耗尽特效把这同一张卡搬进自身 SubViewport，并把根节点锁定在卡当时的 GlobalPosition；
///    之后按卡自身 pivot（=卡片半尺寸 150,211）把画面放到“根 + 半尺寸”处，
///    因此卡内容原点相对根还会残留 (1 - scale) * half 的偏移。
///  * 要让耗尽卡的画面与弹出卡逐像素重合，耗尽根必须落在
///    “弹出卡内容原点 - 缩放残留” = root 原点 + card.Position。
///    这个量在弹出卡满幅后同步可得，必须在耗尽首次绘制前（同帧）覆盖其根位置，
///    否则第一帧会在偏右下 (1-scale)*half 处闪现出一张“额外卡”。
///
/// 纯 C# 组合（不依赖任何 Reed 场景资源），可被任意规则/卡牌/事件调用：
///   ReedAttachVfx.Play(targetCreature, cardModel);
/// </summary>
public static class ReedAttachVfx
{
    /// <summary>卡牌放大出现的目标缩放（真实卡 300x422 → 半幅约 150x211，观感轻巧、不抢戏）。</summary>
    public const float AppearMaxScale = 0.5f;

    /// <summary>放大到满幅所需时长（“非常快速”）。</summary>
    private const float PopInSeconds = 0.3f;

    /// <summary>关联卡落点相对目标生物点的最大随机偏移（x、y 各 ±80 的方形范围）。</summary>
    private const float JitterRange = 80f;

    /// <summary>
    /// 在目标生物身上播放关联特效。anchor 省略时默认取生物 Hitbox 的中上部
    /// （顶缘向下约 1/3 处），让卡牌大致落在生物胸口/头顶区域。
    /// 落点会在目标点周围 ±80 的方形范围内随机散布，避免每次关联都出现在同一处。
    /// </summary>
    public static void Play(Creature target, CardModel model, Vector2? anchor = null, float maxScale = AppearMaxScale)
    {
        if (TestMode.IsOn || target == null || model == null)
        {
            return;
        }
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(target);
        Control? container = target.GetVfxContainer();
        if (creatureNode == null || container == null)
        {
            return;
        }

        Vector2 top = creatureNode.GetTopOfHitbox();
        Vector2 bottom = creatureNode.GetBottomOfHitbox();
        Vector2 origin = anchor ?? top.Lerp(bottom, 0.30f);

        // 目标点周围 ±80 方形内随机落点（每次不同）。
        float rx = (float)GD.RandRange(-JitterRange, JitterRange);
        float ry = (float)GD.RandRange(-JitterRange, JitterRange);
        origin += new Vector2(rx, ry);

        Start(container, origin, model, Mathf.Clamp(maxScale, 0.01f, 1.5f));
    }

    /// <summary>在固定屏幕坐标播放（无需活的生物节点也可用，例如结算/图鉴），不做随机散布。</summary>
    public static void PlayAt(Control container, Vector2 anchor, CardModel model, float maxScale = AppearMaxScale)
    {
        if (TestMode.IsOn || container == null || model == null)
        {
            return;
        }
        Start(container, anchor, model, Mathf.Clamp(maxScale, 0.01f, 1.5f));
    }

    private static void Start(Control container, Vector2 anchor, CardModel model, float maxScale)
    {
        Vector2 half = NCard.defaultSize * 0.5f; // 半宽/半高（150,211），缩放 pivot 用

        Control root = new Control
        {
            Name = "ReedAttachVfx",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        container.AddChildSafely(root);
        root.GlobalPosition = anchor; // 容器与生物同画布坐标，root 原点 = 落点
        NCard card = NCard.Create(model);
        card.Scale = Vector2.Zero;
        card.PivotOffset = half;
        // 卡内容原点 = root 原点 + Position + (1-缩放)*half。
        // 令 Position = -(1-maxScale)*half，满幅(scale=maxScale)时卡内容原点正好落在 root 原点(=anchor)。
        card.Position = -(1f - maxScale) * half;
        root.AddChild(card);
        card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal); // 已入树，刷新描述

        Tween tween = root.CreateTween();
        tween.TweenProperty(card, "scale", Vector2.One * maxScale, PopInSeconds)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Back); // 带回弹，先冲到目标附近
        tween.TweenCallback(Callable.From(() => Finish(root, card, container)));
    }

    /// <summary>放大到位（scale=maxScale）后：把卡片交给原版耗尽特效并在原地播放。</summary>
    private static void Finish(Control root, NCard card, Control container)
    {
        // 耗尽根的目标落点：弹出卡内容原点 - 缩放残留 = root 原点 + card.Position。
        // 此刻 card 仍是 root 的子节点，该值可同步取出，不必等帧、也不依赖任何测量。
        Vector2 targetRoot = root.GlobalPosition + card.Position;

        NCardExhaustVfx? exhaust = NCardExhaustVfx.Create(card);
        if (exhaust != null)
        {
            container.AddChildSafely(exhaust);
            TaskHelper.RunSafely(PlayExhaustAlignedAsync(exhaust, targetRoot));
        }
        else
        {
            card.QueueFree();
        }
        root.QueueFree(); // 卡片已被 Create 移走，这里只释放空壳
    }

    /// <summary>
    /// 播放原版耗尽动画：其同步前缀会把耗尽根锁定在卡当时的 GlobalPosition（= 卡内容原点 +
    /// (1-scale)*half，天然偏右下 (1-scale)*half），随后在<em>同一帧、首次绘制前</em>覆盖为正确落点，
    /// 从第一帧起就与弹出卡重合，不会闪现出位置错误的“额外卡”。动画推进与自释放由游戏内部负责，
    /// 本任务仅保持到播完，避免被提前回收。
    /// </summary>
    private static async Task PlayExhaustAlignedAsync(NCardExhaustVfx exhaust, Vector2 targetRoot)
    {
        try
        {
            Task animation = exhaust.PlayAnimation();
            exhaust.GlobalPosition = targetRoot; // 同步覆盖，未跨帧 → 无残影
            await animation;
        }
        catch (OperationCanceledException)
        {
            // 特效被释放/退出场景属正常结束，忽略。
        }
    }
}
