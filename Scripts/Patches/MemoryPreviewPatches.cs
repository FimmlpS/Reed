using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Patches;

/// <summary>
/// 追忆预览的悬浮驱动：一张实现了 <see cref="IMemorySubscriber"/> 的牌被悬浮时，在回忆牌堆图标处
/// 展开「它可追忆的牌」的扇形预览。
///
/// <para>挂点选 <see cref="NCardHolder.DoCardHoverEffects"/> ——因为它是**真正产生悬浮效果**的那一步
/// （放大 + tip），而不是 <c>OnFocus</c>：<c>NCardHolder.OnFocus</c> 只置位并转交
/// <c>RefreshFocusState → DoCardHoverEffects</c>，且各子类的重写路径不一致
/// （<c>NGridCardHolder</c> 调 base.OnFocus，<c>NPreviewCardHolder.OnFocus</c> 不调 base）。</para>
///
/// <para>覆盖情况：<c>NCardHolder</c> 基类那条覆盖了网格牌（回忆界面 / 牌堆界面 —— 它们没有重写
/// <c>DoCardHoverEffects</c>）；手牌由 <c>NHandCardHolder</c> 自己重写且**不调 base**，所以单独再挂一条。
/// 两条路径互不重叠（重写体不执行基类实现），同一次悬浮只会触发一次。</para>
/// </summary>
[HarmonyPatch(typeof(NCardHolder), "DoCardHoverEffects")]
internal static class CardHoverMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCardHolder __instance, bool isHovered)
    {
        MemoryPreviewOverlay.OnHoverChanged(__instance, isHovered);
    }
}

/// <summary>手牌：<c>NHandCardHolder</c> 重写了 <c>DoCardHoverEffects</c> 且不调 base，得单独挂。</summary>
[HarmonyPatch(typeof(NHandCardHolder), "DoCardHoverEffects")]
internal static class HandCardHoverMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHandCardHolder __instance, bool isHovered)
    {
        MemoryPreviewOverlay.OnHoverChanged(__instance, isHovered);
    }
}

/// <summary>
/// 「抓住」驱动之一：出牌时展开追忆预览。<c>NPlayerHand.StartCardPlay</c> 是**玩家主动出牌的唯一入口** ——
/// 鼠标点住手牌拖出来（<c>OnHolderPressed</c>，<c>startedViaShortcut: false</c>）和键盘 1-10 出牌
/// （<c>_UnhandledInput</c>，<c>startedViaShortcut: true</c>）都调它。键盘那条尤其重要：鼠标从没碰过牌，
/// 悬浮永远不会触发，不挂这里就完全没有预览。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), "StartCardPlay")]
internal static class HandCardGrabMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHandCardHolder holder)
    {
        MemoryPreviewOverlay.OnCardHeld(holder);
    }
}

/// <summary>
/// 「松手」驱动：出牌流程收尾时收起抓住期间的预览。<c>NCardPlay.Cleanup</c> 是成功打出
/// （<c>Cleanup(isFinished: true)</c>）与取消出牌（<c>CancelPlayCard → Cleanup(isFinished: false)</c>）
/// 的共同出口，成功、取消两条路都在这里收干净。
/// </summary>
[HarmonyPatch(typeof(NCardPlay), "Cleanup")]
internal static class CardPlayCleanupMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        MemoryPreviewOverlay.OnCardReleased();
    }
}

/// <summary>
/// 「松手」驱动的兜底（取消路径）：<c>NPlayerHand.ReturnHolderToHand</c> —— 点空白处 / 按 Esc 取消出牌、
/// 回合结束的 <c>CancelAllCardPlay</c> 都会走到它。和上面 <c>Cleanup</c> 那条是同一件事的两道保险：
/// <c>Cleanup</c> 更早更准，但它本体很短，理论上可能被 JIT 内联进调用方而绕过 Harmony 挂的钩子；
/// 这条方法体更大、不会被内联，用来兜住「取消」这一路。
/// <para>打出成功那一路不需要兜底：牌离开手牌时 holder 会退出场景树，<c>TreeExiting</c> 早在展开时就接好了。</para>
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), "ReturnHolderToHand")]
internal static class HandCardReturnMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        MemoryPreviewOverlay.OnCardReleased();
    }
}

/// <summary>
/// 「抓住」驱动之二：选择模式（<c>Mode.SimpleSelect</c>，“选一张牌弃掉”这类）里选中一张手牌。
/// 选中的牌会被挪进 <c>NSelectedHandCardContainer</c>（原 holder 立即被移除、悬浮随之消失），
/// 所以用**新造出来的那个 holder** 当主人；它被取消选中时自己离开场景树，预览就跟着收起。
/// </summary>
[HarmonyPatch(typeof(NSelectedHandCardContainer), "Add")]
internal static class HandCardSelectMemoryPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(NSelectedHandCardHolder __result)
    {
        MemoryPreviewOverlay.OnCardHeld(__result);
    }
}
