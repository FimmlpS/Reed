using System;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.DynamicVars;

/// <summary>
/// 【额外格挡】变量，名字就叫 <c>ExtraBlock</c>。
///
/// <para>原版给「基础值 + 每份额外值」的伤害牌准备了成对的
/// <see cref="CalculationBaseVar"/>（<c>CalculationBase</c>）+ <see cref="ExtraDamageVar"/>（<c>ExtraDamage</c>），
/// 由 <see cref="CalculatedDamageVar"/> 读这对值；但**格挡侧只有一半**：<see cref="CalculatedBlockVar"/>
/// 默认去读 <c>CalculationExtra</c>，并没有 <c>ExtraBlock</c> 这个名字（<c>DynamicVarSet</c> 上也没有对应属性）。</para>
///
/// <para>所以这里照 <see cref="ExtraDamageVar"/> 的样子补一个同名变量，再配
/// <see cref="CalculatedBlockFromExtraVar"/> 把算式接过去 —— 于是卡面文案能和 Thorn 的
/// <c>{CalculatedDamage:diff()}</c> + <c>{ExtraDamage:diff()}</c> 写成一一对应的
/// <c>{CalculatedBlock:diff()}</c> + <c>{ExtraBlock:diff()}</c>。</para>
/// </summary>
public class ExtraBlockVar : DynamicVar
{
    public const string ExtraBlockName = "ExtraBlock";

    public ExtraBlockVar(decimal baseValue) : base(ExtraBlockName, baseValue)
    {
    }
}

/// <summary>
/// <see cref="CalculatedBlockVar"/>，唯一区别是算式里的「额外值」取自 <see cref="ExtraBlockVar"/>
/// （<c>ExtraBlock</c>），而不是原版默认的 <c>CalculationExtra</c>。用法与父类完全一致：
/// <c>new CalculatedBlockFromExtraVar(ValueProp.Move).WithMultiplier((card, target) =&gt; ...)</c>，
/// 卡面文案依旧写 <c>{CalculatedBlock:diff()}</c>（变量名来自父类，没变）。
/// </summary>
public class CalculatedBlockFromExtraVar : CalculatedBlockVar
{
    public CalculatedBlockFromExtraVar(ValueProp props) : base(props)
    {
    }

    protected override DynamicVar GetExtraVar()
    {
        if (_owner is CardModel card)
        {
            return card.DynamicVars.ExtraBlock();
        }
        throw new InvalidOperationException(
            $"CalculatedBlockFromExtraVar 只能挂在 CardModel 上（当前 owner：{_owner?.Id}）。");
    }
}

public static class ExtraBlockExtensions
{
    /// <summary>取 <c>ExtraBlock</c> 动态变量（升级就 <c>DynamicVars.ExtraBlock().UpgradeValueBy(1)</c>）。</summary>
    public static DynamicVar ExtraBlock(this DynamicVarSet dynamicVars)
    {
        return dynamicVars[ExtraBlockVar.ExtraBlockName];
    }
}
