using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Cards.Status;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class WishAndCurse : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public WishAndCurse() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.None)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IEnumerable<CardModel> cards = (await CardSelectCmd.FromHandForDiscard(choiceContext,Owner,new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 0, 99), null, this)).AsEnumerable();
        bool discard = false;
        HashSet<CardType> typeSet = new HashSet<CardType>();
        foreach(CardModel card in cards){
            if (!typeSet.Contains(card.Type))
            {
                typeSet.Add(card.Type);
            }
            else
            {
                discard = true;
                break;
            }
        }
        if (discard)
        {
            await CardCmd.Discard(choiceContext, cards);
        }
        else
        {
            foreach(CardModel card in cards){
                await CardCmd.Exhaust(choiceContext, card);
            }
        }
        await CardPileCmd.Draw(choiceContext, cards.Count(), Owner);
    }    

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}