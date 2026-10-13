using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Powers;

public class LightShadowPower : AbstractReedPower, IFireFlowerSubscriber
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>进入燃烧 → 焰光</summary>
    async Task IFireFlowerSubscriber.AfterEnterBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {
        await Give(playerChoiceContext, light: true);
    }

    /// <summary>离开燃烧 → 焰影</summary>
    async Task IFireFlowerSubscriber.AfterExitBurning(PlayerChoiceContext playerChoiceContext, Creature target)
    {
        await Give(playerChoiceContext, light: false);
    }

    private async Task Give(PlayerChoiceContext choiceContext, bool light)
    {
        if (Owner.Player is not Player player)
        {
            return;
        }
        ICombatState? combat = CombatState;
        if (combat == null || CombatManager.Instance.IsOverOrEnding || Amount <= 0)
        {
            return;
        }

        List<CardModel> cards = new();
        for (int i = 0; i < Amount; i++)
        {
            cards.Add(light ? combat.CreateCard<FlameLight>(player) : combat.CreateCard<FlameShadow>(player));
        }
        if (cards.Count == 0)
        {
            return;
        }
        Flash();
        await CardPileCmd.AddGeneratedCardsToCombat(cards, PileType.Hand, player);
    }
}
