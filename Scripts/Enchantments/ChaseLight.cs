using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Status;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Enchantments;

public class ChaseLight : CustomEnchantmentModel, IMemoryEventSubscriber
{
    public override bool ShowAmount => false;

    public override bool HasExtraCardText => false;

    public override bool IsStackable => false;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameLight>()
    ];

    async Task IMemoryEventSubscriber.AfterRemembered(PlayerChoiceContext choiceContext, Player player, CardModel card)
    {
        if (ReferenceEquals(card, Card))
        {
            await Card.CreateInHand<FlameLight>(1, player);
        }
    }

    protected override string? CustomIconPath => "res://Reed/images/icon/chase_light.png";
}