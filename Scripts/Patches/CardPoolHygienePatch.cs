using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Reed.Scripts.Patches;

/// <summary>
/// <see cref="NCard"/> 还池时的「消毒」补丁：把 <c>PivotOffset</c> 复位。
///
/// <para><b>为什么需要</b>：<see cref="NCard.OnReturnedFromPool"/> 会重置
/// <c>Position / Rotation / Scale / Modulate</c>（还有 <c>Body</c> 的那几项），
/// **唯独漏了 <c>PivotOffset</c>**。而 Control 的旋转与缩放都是**绕 pivot** 的：
/// 规范的 pivot 是 (0,0)（<c>card.tscn</c> 根节点没写 pivot，卡面美术在 <c>CardContainer</c> 里
/// 围绕原点居中，例如 <c>Shadow</c> 的 offset 是 -138..162 / -199..223）。
/// 一旦某处把 pivot 挪到卡片半尺寸 (150,211)，这张卡回到池里之后再被**旋转**使用，
/// 美术中心就会落到 <c>Position + P − R·S·P</c> —— θ=45°、s=0.3 时偏出约 (163,134) px，
/// 看起来就是「这张牌飞到了别处」。</para>
///
/// <para><b>谁弄脏的</b>：目前是 <c>ReedAttachVfx</c>（放大出现 → 原版耗尽特效），它按注释把 pivot
/// 挪到半尺寸来做角度/缩放对齐，卡牌最终由 <c>NCardExhaustVfx.DelayedFree</c> 交还池子，没人还原。
/// 池是 LIFO 复用，于是只有「恰好拿到这张脏卡」的那一次渲染才会错位 —— 表现成
/// 「有时某张牌位置不对、再悬浮一次又正常」，且**关掉再开回忆/追忆界面**（改变池序）就会换一张牌出错。</para>
///
/// <para>在**还池的那一刻**统一还原：此时卡一定不在树上、也没人渲染它，既修掉现有漏洞，
/// 也让以后新增的 pivot 改动不再污染整个池（回忆界面的网格牌、图鉴、选牌界面共用同一个池）。</para>
/// </summary>
[HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
internal static class CardPoolHygienePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCard __instance)
    {
        // 只负责还池流程漏掉的那一项：Position / Rotation / Scale / Modulate / Body.*
        // 交给取用时的 OnReturnedFromPool 处理，这里重复设置只会和它互相牵扯。
        __instance.PivotOffset = Vector2.Zero;
    }
}
