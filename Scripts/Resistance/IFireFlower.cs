using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Resistance;

/// <summary>
/// “火花牌”卡牌应实现的接口：声明该卡在 <see cref="ReedAttachCmd.Attach"/> 流程里的几种关键行为。
/// 让卡牌自身拥有“火焰/花朵”性质：可被合体进阶、可被引爆、有挂上/取下的反应。
///
/// 由 <see cref="ReedAttachCmd.Attach"/> 驱动的时机（正常 attach 一律走那条命令，不要绕过）：
///   * 合体进阶时：目标上同 ModelId 的旧火花会被取下 → 触发其 <see cref="OnRemoved"/>；
///   * 火花已满需要腾位时：被玩家选中的火花会被引爆 → 触发 <see cref="OnBurnt"/>；
///   * 成功 attach 后：新火花会收到 <see cref="OnAttached"/>；
///   * 触发进阶的条件：<see cref="GetUpperCard"/> 返回一张非 null 的“上级卡”（canonical 即可，
///     <see cref="ReedAttachCmd"/> 会自动把它物化成一颗该玩家的全新副本去 attach）。
/// 若只是“同 ModelId 攒到 3 张却不会进阶”的普通火花卡，不实现本接口即可。
/// </summary>
public interface IFireFlower
{
    /// <summary>
    /// 被引爆（火花已满、玩家选择这张火花引爆腾位）时触发。此时火花仍挂在目标上，可在里面结算
    /// 灼烧/迸发等效果（例如对 <paramref name="target"/> 打 ReedBurnCmd / 播放火焰 VFX）。
    /// 默认无行为；随后 <see cref="ReedAttachCmd"/> 会将其从目标上移除（RemoveSpark → 卡行消散动画）。
    /// </summary>
    public virtual async Task OnBurnt(PlayerChoiceContext playerChoiceContext, FireBurnt fireBurnt)
    {
    }

    /// <summary>
    /// 成功 attach（GiveSpark 返回 true，计数圆已闪烁 + Attach 特效已播）之后触发。
    /// 适合做“种下后的小反应”。默认无行为。
    /// </summary>
    public virtual async Task OnAttached(PlayerChoiceContext playerChoiceContext, Creature target)
    {
    }

    /// <summary>
    /// 被从目标上取下时触发（不含被引爆的情况；引爆走 <see cref="OnBurnt"/>）。
    /// 目前指“合体进阶”：目标上已有 ≥2 张与本卡同 ModelId 的火花，且下一张即将 attach 的上级卡触发了合并，
    /// 于是这几张旧火花被取下以换成一张进阶卡。默认无行为。
    /// </summary>
    public virtual async Task OnRemoved(PlayerChoiceContext playerChoiceContext, Creature target)
    {
    }

    /// <summary>
    /// 返回这张火花的“上级形态”（canonical 即可），用于合体进阶：当目标上已有 2 张与本卡同 ModelId 的火花、
    /// 而这次又要 attach 一张本卡时，三张会合体成一张上级卡。返回 null（默认）表示本卡没有进阶形态。
    /// </summary>
    public virtual CardModel GetUpperCard()
    {
        return null;
    }
}
