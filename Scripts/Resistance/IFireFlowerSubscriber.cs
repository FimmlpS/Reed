using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Resistance;

public interface IFireFlowerSubscriber
{
    public virtual async Task OnBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt)
    {
        
    }

    public virtual FireBurnt ModifyBurnt(CardModel fireFlower, FireBurnt fireBurnt, bool triggerByBurning)
    {
        return fireBurnt;
    }

    public virtual async Task AfterModifyBurnt(PlayerChoiceContext playerChoiceContext, CardModel fireFlower, FireBurnt fireBurnt, bool triggerByBurning)
    {
        
    }

    public virtual int ModifySparkMax(Creature c, Player owner, int amount)
    {
        return amount;
    }

    /// <summary>
    /// 修改一次抗性变化的<b>最终数值</b>：可以是灼烧扣减，也可以是恢复。
    /// 例：返回 <c>amount + 1</c> 时，「灼烧 1」实际扣 2 点抗性。
    ///
    /// <para>调用点在 <see cref="ReedBurnCmd"/> 结算一次灼烧/恢复之前（见 <c>ResistanceSystem.ModifyChangeAmount</c>），
    /// 且<b>先于</b> <see cref="BeforeBurn"/> —— 因此修正后的值既是最终抗性变化量，
    /// 也是 BeforeBurn / AfterBurn 收到的数值（薪燃按它加火花伤害）。</para>
    /// </summary>
    /// <param name="target">抗性发生变化的那只生物（不一定是本订阅者的主人）。</param>
    /// <param name="amount">原始数值（&gt; 0）。</param>
    /// <param name="isLoss">true = 扣减（灼烧）；false = 恢复。</param>
    public virtual int ModifyResistanceChange(Creature target, int amount, bool isLoss)
    {
        return amount;
    }

    public virtual async Task AfterAllBurnt(PlayerChoiceContext playerChoiceContext, Creature c, bool triggerByBurning)
    {

    }

    /// <summary>
    /// 一张火花牌刚被 <paramref name="owner"/> 成功附着到 <paramref name="target"/> 身上。
    /// 广播点在 <see cref="ReedAttachCmd"/> 的附着收尾（<see cref="ReedAttachCmd.Attach"/> 与
    /// <see cref="ReedAttachCmd.UpgradeSpark"/> 都走这里）：此时火花已进目标名单，火花自身的
    /// <see cref="IFireFlower.OnAttached"/> 也已结算完。
    /// </summary>
    public virtual async Task AfterAttach(PlayerChoiceContext playerChoiceContext, Player owner, CardModel card, Creature target)
    {

    }

    public virtual async Task BeforeBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {
        
    }

    public virtual async Task AfterBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {

    }

    /// <summary>
    /// 某生物【刚进入】燃烧状态（<see cref="ResistanceSystem.IsBurning"/> 由 false 变 true）时调用。
    /// 广播点在 <see cref="ResistanceSystem.EnterBurning"/>：条已转紫、<b>进入燃烧时的火花爆发
    /// （triggerByBurning）也已结算完</b>之后才轮到本函数，因此这里看到的是燃烧状态下的稳定局面。
    /// 只有真正发生切换才会调用（已在燃烧中的重复调用被状态机挡掉）。
    /// </summary>
    /// <param name="target">进入燃烧状态的那只生物（不一定是本订阅者的主人）。</param>
    public virtual async Task AfterEnterBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {

    }

    /// <summary>
    /// 某生物【刚离开】燃烧状态（<see cref="ResistanceSystem.IsBurning"/> 由 true 变 false）时调用。
    /// 广播点在 <see cref="ResistanceSystem.ExitBurning"/>：条已转回蓝、常驻火焰已停之后。
    /// 触发条件固定为「<b>抗性条回满</b>」（Current == Max）—— 这是燃烧状态的唯一出口；
    /// 换战斗时重置状态不走这里（那是 <c>ResetForNewCombat</c> 直接写字段）。
    /// 同样只在真正发生切换时调用。
    /// </summary>
    /// <param name="target">离开燃烧状态的那只生物（不一定是本订阅者的主人）。</param>
    public virtual async Task AfterExitBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {

    }
}