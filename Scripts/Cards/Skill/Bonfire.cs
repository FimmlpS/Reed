using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Bonfire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(7,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<ShineFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public Bonfire() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<ShineFlower>(), cardPlay.Target);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}