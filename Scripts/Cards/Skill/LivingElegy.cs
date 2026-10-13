using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class LivingElegy : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new BurnVar(2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromPower<CurseHealPower>(1),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public LivingElegy() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyAlly)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,1m,Owner.Creature,this);
        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,-1m,Owner.Creature,this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Burn().UpgradeValueBy(2);
    }
}