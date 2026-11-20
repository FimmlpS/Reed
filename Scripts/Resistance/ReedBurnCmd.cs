using System.Threading.Tasks;
using Godot;
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
    public static ReedBurnBuilder Burn(int amount) => new(amount,true);

    /// <summary>
    /// 灼烧「其全部抗性」：每个目标以各自的<b>当前</b>抗性为灼烧量，逐个单独结算（互不影响，不共用一个数值）。
    /// 未燃烧且抗性 &gt; 0 的敌人必然被恰好烧进燃烧状态；已燃烧或抗性已归零的敌人灼烧量为 0，
    /// 被 <see cref="ResistanceSystem.Reduce"/> 的守卫跳过（等于不结算）。
    /// </summary>
    public static ReedBurnBuilder BurnAllResistance() => new(0, true, useTargetResistance: true);

    public static ReedBurnBuilder Heal(int amount) => new(amount,false);
}

/// <summary>“灼烧”命令的流式构建器（形态同 AttackCommand：目标可后置、Execute 前随时补）。</summary>
public sealed class ReedBurnBuilder
{
    private readonly int _amount;
    private readonly bool _useTargetResistance;
    private CardPlay? _cardPlay;
    private List<Creature> _target = [];
    private bool _isBurn;
    private Creature _source;

    internal ReedBurnBuilder(int amount, bool isBurn, bool useTargetResistance = false)
    {
        _amount = amount;
        _isBurn = isBurn;
        _useTargetResistance = useTargetResistance;
    }

    /// <summary>直接来源。</summary>
    public ReedBurnBuilder FromCreature(Creature source)
    {
        _source = source;
        return this;
    }

    /// <summary>来源卡与本次出牌（保留以对齐其他 Cmd 形态；卡牌来源暂不参与结算）。</summary>
    public ReedBurnBuilder FromCard(CardModel card, CardPlay? cardPlay)
    {
        _cardPlay = cardPlay;
        _source = card.Owner.Creature;
        return this;
    }

    /// <summary>指定承受灼烧的目标生物。</summary>
    public ReedBurnBuilder Targeting(Creature target)
    {
        _target.Add(target);
        return this;
    }

    public ReedBurnBuilder Targeting(List<Creature> target)
    {
        _target.AddRange(target);
        return this;
    }

    /// <summary>结算灼烧/治疗抗性。优先使用显式目标，未指定时退回 cardPlay.Target；目标已死亡则跳过。</summary>
    public async Task Execute(PlayerChoiceContext? choiceContext)
    {
        // 来源不得为空，目标不得为0
        if (_source==null || _target.Count() == 0)
        {
            return;
        }
        if(choiceContext == null)
        {
            choiceContext = new ThrowingPlayerChoiceContext();
        }
        foreach(Creature creature in _target)
        {
            // 灼烧「全部抗性」时，每个目标用自己的当前抗性，逐个单独结算。
            int amount = _useTargetResistance ? ResistanceSystem.GetCurrent(creature) : _amount;
            // 恨意一类的修正在这里落地：后续 BeforeBurn / AfterBurn 与抗性结算看到的是同一个最终值
            // （薪燃按修正后的灼烧量加火花伤害，抗性也按它扣）。
            amount = ResistanceSystem.ModifyChangeAmount(creature, amount, _isBurn);
            foreach(AbstractModel model in creature.CombatState?.IterateHookListeners() ?? [])
            {
                if(model is IFireFlowerSubscriber sub && _isBurn)
                {
                    try
                    {
                        await sub.BeforeBurn(choiceContext, creature, _source, amount);
                    }
                    catch (Exception e)
                    {
                        GD.PushWarning($"[ReedBurnCmd] BeforeBurn 失败（{model.Id}）：{e.Message}");
                    }
                }
            }
            if (creature.IsAlive)
            {
                if (_isBurn)
                {
                    // 带上出牌上下文：抗性归零会在 Reduce 内部进入燃烧状态，
                    // 并把「进入燃烧」事件派发给订阅者（见 ResistanceSystem.EnterBurning）。
                    await ResistanceSystem.Reduce(creature, amount, choiceContext);
                }
                else
                {
                    await ResistanceSystem.Restore(creature, amount, choiceContext);
                }
            }
            foreach(AbstractModel model in creature.CombatState?.IterateHookListeners() ?? [])
            {
                if(model is IFireFlowerSubscriber sub && _isBurn)
                {
                    try
                    {
                        await sub.AfterBurn(choiceContext, creature, _source, amount);
                    }
                    catch (Exception e)
                    {
                        GD.PushWarning($"[ReedBurnCmd] AfterBurn 失败（{model.Id}）：{e.Message}");
                    }
                }
            }
        }
    }
}
