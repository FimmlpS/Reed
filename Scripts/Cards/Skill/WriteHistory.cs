using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Memory;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.CardSelection;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class WriteHistory : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(3,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public WriteHistory() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach(CardModel card in await CardSelectCmd.FromHandForDiscard(choiceContext, Owner, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt,0,99), null, this))
        {
            await CardCmd.Discard(choiceContext, card);
            await RecordCmd.Record(choiceContext,Owner)
            .FromPlay(cardPlay)
            .Offset(0)
            .Card(card)
            .Execute(choiceContext);
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, true);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(1);
    }
}