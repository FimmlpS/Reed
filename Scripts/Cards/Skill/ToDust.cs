using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Extensions;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class ToDust : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory),
        HoverTipFactory.FromKeyword(ReedKeywords.Future)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public ToDust() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int round = 1;
        PlayerMemory memory = MemorySystem.Of(Owner);
        while (memory.HasRound(round))
        {
            CardModel reRecord = memory.PeekRound(round).TakeRandom(1, CombatState.RunState.Rng.CombatCardSelection).FirstOrDefault()?.Card;
            if(reRecord != null)
            {
                await RecordCmd.Record(choiceContext, Owner)
                .FromPlay(cardPlay)
                .Offset(0)
                .Card(reRecord)
                .Execute(choiceContext);
            }
            round++;
        }

        await RecallCmd.Recall(choiceContext, Owner)
            .FromPlay(cardPlay)
            .Offset(0)
            .Where(c=>c.Type==CardType.Skill || (IsUpgraded && c.Type==CardType.Attack))
            .Execute(choiceContext);
    }

    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(0, c=>c.Type==CardType.Skill || (IsUpgraded && c.Type==CardType.Attack));

    protected override void OnUpgrade()
    {
        //EnergyCost.UpgradeBy(-1);
    }
}
