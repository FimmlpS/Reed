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

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class OverColdNight : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(14,ValueProp.Move),
        new RepeatVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public OverColdNight() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await RecordCmd.Record(choiceContext,Owner)
            .FromPlay(cardPlay)
            .Offset(-1)
            .Card(this)
            .Execute(choiceContext);
        for(int i = 1; i <= DynamicVars.Repeat.IntValue; i++)
        {
            await RecordCmd.Record(choiceContext,Owner)
            .FromPlay(cardPlay)
            .Offset(i)
            .Card(this)
            .Execute(choiceContext);
        }
    
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(4);
    }
}