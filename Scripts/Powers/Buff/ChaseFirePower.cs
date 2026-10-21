using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class ChaseFirePower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    int IFireFlowerSubscriber.ModifySparkMax(Creature c, Player owner, int amount)
    {
        return amount+1;
    }

    async Task IFireFlowerSubscriber.BeforeBurn(PlayerChoiceContext playerChoiceContext, Creature target, Creature source, int amount)
    {
        if (source != Owner || !Owner.IsPlayer)
        {
            return;
        }
        if (target.IsBurningResistance())
        {
            // 【多人安全】这里原本是 TakeRandom(1, RunState.Rng.CombatTargets)：TakeRandom = 整表洗牌后取前 N，
            // 它消耗多少个随机数取决于候选表的长度，而候选表是 mod 本地那份火花表 —— 只要两端火花张数/顺序
            // 有半点不同，整条战斗随机数流就会错位，从下一个动作起两边全盘不同步（表现为「火花一爆就炸」、
            // 或某些牌结算后 “State divergence detected”）。改成确定性规则：加成给当前伤害最低的那张火花
            // （并列时取列表靠前的一张），完全不碰同步随机数。
            CardModel? card = ResistanceSystem.SparksOf(target, Owner.Player!)
                .Where(c=>c.DynamicVars.ContainsKey("CalculatedDamage")||c.DynamicVars.ContainsKey("Damage")||c.DynamicVars.ContainsKey("OstyDamage"))
                .OrderBy(ResistanceSystem.SparkDamageOf)
                .FirstOrDefault();
            if(card != null)
            {
                decimal improveVal = amount * Amount;
                //damage
                if (card.DynamicVars.ContainsKey("CalculatedDamage"))
                {
                    card.DynamicVars.CalculatedDamage.BaseValue += improveVal;
                }
                else if(card.DynamicVars.ContainsKey("Damage"))
                {
                    card.DynamicVars.Damage.BaseValue += improveVal;
                }
                else if(card.DynamicVars.ContainsKey("OstyDamage"))
                {
                    card.DynamicVars.OstyDamage.BaseValue += improveVal;
                }
                NCard? hud = ResistanceSystem.GetData(target)?.Bar?.Spark?.FindCardByModel(card);
                if (hud != null)
                {
                    hud.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    // UpdateVisuals 只写基准值，这里补一次数值修正（否则牌面会从修正后的描述退回基准值）。
                    SparkHudDriver.RefreshCardDescription(hud);
                }
            }
        }
    }
}