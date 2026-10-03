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
            CardModel card = ResistanceSystem.SparksOf(target, Owner.Player!).Where(c=>c.DynamicVars.ContainsKey("CalculatedDamage")||c.DynamicVars.ContainsKey("Damage")||c.DynamicVars.ContainsKey("OstyDamage")).TakeRandom(1,target.CombatState.RunState.Rng.CombatTargets).FirstOrDefault();
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
                ResistanceSystem.GetData(target)?.Bar?.Spark?.FindCardByModel(card)?.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            }
        }
    }
}