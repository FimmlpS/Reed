using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Memory;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Ruins : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new EnergyVar(1),
        new RepeatVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public Ruins() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue,Owner);
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
        DynamicVars.Repeat.UpgradeValueBy(1);
    }
}