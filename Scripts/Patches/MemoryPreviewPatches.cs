using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
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
