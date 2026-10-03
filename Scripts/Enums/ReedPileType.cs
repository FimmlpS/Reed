using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Reed.Scripts.Enums;

/// <summary>
/// mod 自定义的 <see cref="PileType"/> 取值（声明方式同 <c>ReedValueProp</c> / <see cref="ReedKeywords"/>：
/// <c>[CustomEnum("名字")] public static 枚举 字段;</c>，真实取值由 BaseLib 在 <c>ModelDb.Init</c> 时按
/// 「命名空间哈希 + 字段名哈希」生成并回填，不会和原版的 0~6 撞号）。
///
/// <para><see cref="Memory"/> 用在回忆牌堆界面里：<c>NCard.UpdateVisuals(pileType, ...)</c> 会按这个值决定
/// 卡面文案与费用颜色。直接用原版的 <see cref="PileType.None"/> 等于告诉卡面「你不属于任何牌堆」，
/// 语义上说不通；给回忆一张自己的牌堆类型后，卡面按「非战斗牌堆」正常渲染，同时又能被识别为回忆。</para>
/// </summary>
public class ReedPileType
{
    /// <summary>回忆牌堆：玩家手动打出过的牌留存的副本所在之处。</summary>
    [CustomEnum("MEMORY")]
    public static PileType Memory;
}
