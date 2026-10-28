using BaseLib.Utils;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Extensions;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class ForeverFlame : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6,ValueProp.Move),
        new BurnVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn),
        HoverTipFactory.FromKeyword(ReedKeywords.Burning)
    ];

    public ForeverFlame() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        Creature lastTarget = cardPlay.Target;

        for(int i = 0; i < 2; i++)
        {
            if (!lastTarget.IsBurningResistance() && CombatState.HittableEnemies.Count>0)
            {
                lastTarget = CombatState.HittableEnemies.TakeRandom(1,CombatState.RunState.Rng.CombatTargets).First();
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                .FromCard(this, cardPlay)
                .Targeting(lastTarget)
                .Execute(choiceContext);

                await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
                .FromCard(this, cardPlay)
                .Targeting(lastTarget)
                .Execute(choiceContext);
            }
            else
            {
                break;
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}