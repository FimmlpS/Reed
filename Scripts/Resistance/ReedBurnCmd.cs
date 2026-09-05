using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 灼烧命令：对目标的【抗性】造成伤害（走 ResistanceSystem.Reduce，数值条扣减）。
/// 这是 HP 之外的独立伤害，不经过 CreatureCmd.Damage。
/// 用法参考 DamageCmd / PowerCmd 的链式风格：
///   await ReedBurnCmd.Burn(1).FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(choiceContext);
/// </summary>
public static class ReedBurnCmd
{
    /// <summary>灼烧 N：构造一次对目标抗性造成 N 点伤害的命令。</summary>
    public static ReedBurnBuilder Burn(int amount) => new(amount);
}

/// <summary>“灼烧”命令的流式构建器（形态同 AttackCommand：目标可后置、Execute 前随时补）。</summary>
public sealed class ReedBurnBuilder
{
    private readonly int _amount;
    private CardPlay? _cardPlay;
    private Creature? _target;

    internal ReedBurnBuilder(int amount)
    {
        _amount = amount;
    }

    /// <summary>来源卡与本次出牌（保留以对齐其他 Cmd 形态；卡牌来源暂不参与结算）。</summary>
    public ReedBurnBuilder FromCard(CardModel card, CardPlay? cardPlay)
    {
        _cardPlay = cardPlay;
        return this;
    }

    /// <summary>指定承受灼烧的目标生物。</summary>
    public ReedBurnBuilder Targeting(Creature target)
    {
        _target = target;
        return this;
    }

    /// <summary>结算灼烧。优先使用显式目标，未指定时退回 cardPlay.Target；目标已死亡则跳过。</summary>
    public Task Execute(PlayerChoiceContext? choiceContext)
    {
        Creature? target = _target ?? _cardPlay?.Target;
        if (target == null || target.CurrentHp <= 0)
        {
            return Task.CompletedTask; // 无目标/目标已倒下：不打
        }

        ResistanceSystem.Reduce(target, _amount);
        return Task.CompletedTask;
    }
}
