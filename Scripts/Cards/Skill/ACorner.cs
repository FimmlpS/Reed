using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class ACorner : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public ACorner() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (CombatState is not { } combat)
        {
            return;
        }
        int count = DynamicVars.Cards.IntValue;

        List<CardModel> candidates = PileType.Draw.GetPile(Owner).Cards
            .TakeRandom(count, combat.RunState.Rng.CombatCardSelection)
            .Concat(PileType.Discard.GetPile(Owner).Cards
                .TakeRandom(count, combat.RunState.Rng.CombatCardSelection))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        CardSelectorPrefs prefs = new(SelectionScreenPrompt, 0, candidates.Count);
        List<CardModel> toTop = (await CardSelectCmd.FromSimpleGrid(choiceContext, candidates, Owner, prefs)).ToList();

        if (toTop.Count > 0)
        {
            await CardPileCmd.Add(toTop, PileType.Draw, CardPilePosition.Top);
        }

        foreach (CardModel card in candidates.Where(c => !toTop.Contains(c)))
        {
            await RecordCmd.Record(choiceContext, Owner)
                .FromPlay(cardPlay)
                .Offset(0)
                .Card(card)
                .Execute(choiceContext);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}
