using BaseLib.Patches.Localization;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 生死之灵：玩家身上「火花形态」的<b>唯一实体</b>。有这个能力 = 玩家的火花处于某个形态；没有 = 普通形态。
///
/// <para><b>形态存在 Amount 上</b>（1 = 生灵、2 = 死灵，见 <see cref="Form"/>）。借 Amount 而不是自定义字段，
/// 是因为原版的能力状态只同步 id 与 amount（<c>NetFullCombatState.PowerState</c>）：自定义字段根本不进校验和，
/// 两端各自记一份形态、分叉了也不会被原版抓到，只会等到后面某张牌的结算数值对不上才炸出来。</para>
///
/// <para><b>层数数字不显示</b>：<see cref="PowerStackType.Single"/> 只影响渲染（NPower 仅在 Counter 时画数字），
/// 不影响施加/改量/移除判定（<c>PowerCmd</c> 只按 <c>InstanceType</c> 分流），所以 1/2 不会以数字形式露在能力图标上。</para>
///
/// <para><b>描述里的形态</b>：powers.json 的 smartDescription 用了 <c>{Alive:…|…}</c>，参数由本类经 BaseLib 的
/// <see cref="IAddDumbVariablesToPowerDescription"/> 注入 —— 它也盖住了能力浮窗的“智能”那条路径，因此不必改任何
/// 本地化文件、也不用覆写 <c>SmartDescription</c>。</para>
/// </summary>
public class WhichLivingPower : AbstractReedPower, IAddDumbVariablesToPowerDescription
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>层数恒为形态位（1/2），数字不显示。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>当前形态。Amount 只由「生死之灵」写成 1 或 2，其余一律按普通形态处理。</summary>
    public WhichLivingForm Form => Amount switch
    {
        1 => WhichLivingForm.Alive,
        2 => WhichLivingForm.Dead,
        _ => WhichLivingForm.None,
    };

    public void AddDumbVariablesToPowerDescription(LocString description)
    {
        LivingForm.AddArgsTo(description, Owner.IsPlayer ? Owner.Player : null);
    }

    /// <summary>
    /// 失去本能力（形态回落到普通形态）时，把该玩家的所有火花重算回基础数值 —— 即 extra 里说的「重制」。
    /// 重算是“每次都从 canonical 的基准值算”而不是加减，所以即使没走到这里，形态来回切换也不会累积。
    /// </summary>
    public override async Task AfterRemoved(Creature oldOwner)
    {
        LivingForm.ApplyToAllSparksOf(oldOwner.Player);
        await base.AfterRemoved(oldOwner);
    }
}
