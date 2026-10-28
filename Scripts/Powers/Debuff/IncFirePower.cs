using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

/// <summary>
/// 薪燃：挂在敌人身上的临时强化。本回合该敌人每受到 1 点灼烧，它的一张火花伤害 +<c>Amount</c>；
/// 玩家回合结束时移除。与 <see cref="ChaseFirePower"/> 同源，区别是**不要求**目标已在燃烧状态。
/// </summary>
public class IncFirePower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    async Task IFireFlowerSubscriber.BeforeBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {
        if (target != Owner || amount <= 0 || source?.Player is not Player player)
        {
            return;
        }
        // 与 ChaseFirePower 一致：加成给当前伤害最低的那张火花（确定性规则，不碰同步随机数）。
        CardModel? card = ResistanceSystem.SparksOf(target, player)
            .Where(c => c.DynamicVars.ContainsKey("CalculatedDamage") || c.DynamicVars.ContainsKey("Damage") || c.DynamicVars.ContainsKey("OstyDamage"))
            .OrderBy(ResistanceSystem.SparkDamageOf)
            .FirstOrDefault();
        if (card == null)
        {
            return;
        }
        decimal improveVal = amount * Amount;
        if (card.DynamicVars.ContainsKey("CalculatedDamage"))
        {
            card.DynamicVars.CalculatedDamage.BaseValue += improveVal;
        }
        else if (card.DynamicVars.ContainsKey("Damage"))
        {
            card.DynamicVars.Damage.BaseValue += improveVal;
        }
        else
        {
            card.DynamicVars.OstyDamage.BaseValue += improveVal;
        }
        NCard? hud = ResistanceSystem.GetData(target)?.Bar?.Spark?.FindCardByModel(card);
        if (hud != null)
        {
            hud.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            // UpdateVisuals 只写基准值，这里补一次数值修正。
            SparkHudDriver.RefreshCardDescription(hud);
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Player)
        {
            await PowerCmd.Remove(this);
        }
    }
}
