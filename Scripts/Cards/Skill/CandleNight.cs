using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Cards.Status;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Memory;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class CandleNight : AbstractReedCard, IMemoryEventSubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<FlameLight>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(6, ValueProp.Move)
    ];

    public CandleNight() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }

    async Task IMemoryEventSubscriber.AfterRemembered(PlayerChoiceContext choiceContext, Player player, CardModel card)
    {
        if (ReferenceEquals(card, this))
        {
            await this.CreateInHand<FlameLight>(DynamicVars.Cards.IntValue);
        }
    }
}