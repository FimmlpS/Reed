using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class FacePast : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromPower<CurseHealPower>(1),
        HoverTipFactory.FromKeyword(ReedKeywords.Recall)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public FacePast() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,1m,Owner.Creature,this);
        int value = cardPlay.Resources.EnergyValue;

        for(int i = 1; i <= value; i++)
        {
            await RecallCmd.Recall(choiceContext, Owner)
            .FromPlay(cardPlay)
            .Offset(-i)
            .Where(c=>c.Type==CardType.Skill || c.Type==CardType.Attack)
            .Execute(choiceContext);
        }
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,-1m,Owner.Creature,this);
    }

    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c=>c.Type==CardType.Skill ||c.Type==CardType.Attack);

    protected override void OnUpgrade()
    {
        
    }

    protected override bool HasEnergyCostX => true;
}
