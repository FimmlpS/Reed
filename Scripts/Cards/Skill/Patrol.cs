using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Patrol : AbstractReedCard
{
    protected override bool HasEnergyCostX => true;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(8, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<NormalFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public Patrol() : base(0, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        int energyCost = ResolveEnergyXValue();
        for(int i = 0; i < energyCost; i++)
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<NormalFlower>(), cardPlay.Target);
        }
    }    

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
        AddKeyword(CardKeyword.Innate);
    }
}