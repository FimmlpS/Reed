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
    /// 被从目标身上<b>取下</b>时触发。所有「移除」都走 <see cref="ReedAttachCmd.Detach"/>
    /// （合体进阶、原地进阶、引爆后取下、上限腾位、以及各张直接取火花的效果），
    /// 因此「爆发」与「被移除」是两段独立结算：先 <see cref="OnBurnt"/>，取下时再这里一次。
    /// 注意：<see cref="ResistanceSystem.RemoveSpark"/> 本身只改数据、不会触发本回调。默认无行为。
    /// </summary>
    public virtual async Task OnRemoved(PlayerChoiceContext playerChoiceContext, Creature target)
    {
    }

    /// <summary>
    /// 按「生死之灵」的形态重算本卡<b>写在自己牌面上</b>的数值。形态取自 owner（玩家），参数直接给进来。
    /// 目前没有花用到它：映耀／灿烂的形态奖励改成了<b>爆发时现读</b>
    /// （<see cref="LivingForm.DiscountDrawn"/>），牌面数值不再随形态变。
    ///
    /// <para><b>实现必须幂等</b>：每次都从 canonical（<c>ModelDb.Card&lt;T&gt;()</c>）的基准值重算，
    /// 而不是在当前值上加减 —— 形态会在生灵↔死灵↔普通之间来回切，靠重算才能做到「切换回普通形态后重制」。
    /// 收到 <see cref="WhichLivingForm.None"/> 时也照样重算，那就是回到基准值。</para>
    ///
    /// <para>不急的形态效果<b>不要</b>写进这里：能读的时候现读（爆发时问一次 owner 的形态）就不必维护牌面数值，
    /// 也就不会有忘记重制的风险。默认无行为。</para>
    /// </summary>
    public virtual void ApplyLivingForm(WhichLivingForm form)
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

    /// <summary>
    /// 这张火花是否为「<b>进阶形态</b>」—— 即每族花的最上级（<c>EndFlower</c> / <c>SplendidFlower</c> / <c>BlazeFlower</c>）。
    /// 供按张数计伤的效果使用：MeltCalamity（融化灾变）把进阶火花记作 2 张。与“卡牌升级（upgrade）”无关。
    ///
    /// <para><b>必须显式声明</b>，不要用 <c>GetUpperCard() == null</c> 反推：那个判断的真实含义是“没有上级形态”，
    /// 一旦某张火花忘了声明上级，它就会被静默当成进阶牌、凭空多算一份伤害 —— 不抛异常，只是数值错，极难查。
    /// 默认 false：漏声明的后果是少算，方向上是安全的。</para>
    /// </summary>
    public virtual bool IsAdvanced => false;
}
