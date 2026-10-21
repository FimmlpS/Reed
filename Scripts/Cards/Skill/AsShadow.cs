using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class AsShadow : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burning),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new RepeatVar(2),
        new BurnVar(1)
    ];

    public AsShadow() : base(0, CardType.Skill, CardRarity.Common, TargetType.AllEnemies)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach(Creature target in CombatState.HittableEnemies)
        {
            if (target.IsBurningResistance())
            {
                await ReedBurnCmd.Heal(DynamicVars.Repeat.IntValue)
                .FromCard(this,cardPlay)
                .Targeting(target)
                .Execute(choiceContext);
            }
            else
            {
                await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
                .FromCard(this,cardPlay)
                .Targeting(target)
                .Execute(choiceContext);
            }
        }
    }    

    protected override void OnUpgrade()
    {
        DynamicVars.Repeat.UpgradeValueBy(1);
    }
}