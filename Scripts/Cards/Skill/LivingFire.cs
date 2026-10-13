using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Resistance;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Cards.Flower;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class LivingFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new BurnVar(4)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<BurnMarkFlower>(),
        HoverTipFactory.FromPower<CurseHealPower>(1),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public LivingFire() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,1m,Owner.Creature,this);
        foreach(Creature target in Owner.Creature.CombatState.GetOpponentsOf(Owner.Creature).ToList())
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<BurnMarkFlower>(), target);
        }
        foreach(Creature target in Owner.Creature.CombatState.GetOpponentsOf(Owner.Creature).ToList())
        {
            await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);
        }
        await PowerCmd.Apply<CurseHealPower>(choiceContext,Owner.Creature,-1m,Owner.Creature,this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Burn().UpgradeValueBy(2);
    }
}