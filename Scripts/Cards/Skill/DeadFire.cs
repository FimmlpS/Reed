using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class DeadFire : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite),
        HoverTipFactory.FromKeyword(ReedKeywords.Step),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public DeadFire() : base(2, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        List<CardModel> detonated = new();
        foreach (Creature enemy in CombatState.HittableEnemies)
        {
            foreach (CardModel spark in ResistanceSystem.SparksOf(enemy, Owner).ToList())
            {
                if (spark is IFireFlower)
                {
                    await ReedAttachCmd.Burnt(choiceContext, spark, enemy, blv: this.IsUpgraded?1.5m:1m);
                }
                ResistanceSystem.RemoveSpark(enemy, Owner, spark);
                detonated.Add(spark);
            }
        }

        List<CardModel> queue = ReedAttachCmd.MergeSparks(Owner, detonated);

        foreach (CardModel spark in queue)
        {
            Creature? target = CombatState.HittableEnemies
                .TakeRandom(1, CombatState.RunState.Rng.CombatTargets)
                .FirstOrDefault();
            if (target == null)
            {
                break;
            }
            await ReedAttachCmd.Attach(choiceContext, spark, target);
        }
    }

    protected override void OnUpgrade()
    {
        //EnergyCost.UpgradeBy(-1);
    }
}
