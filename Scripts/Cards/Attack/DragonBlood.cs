using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Helper;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class DragonBlood : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(12,ValueProp.Move),
        new RepeatVar(1)
    ];


    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite)
    ];

    public DragonBlood() : base(2, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<Creature> hittableEnemies = base.CombatState.HittableEnemies;
        foreach (Creature item in hittableEnemies)
        {
            ReedHelper.CreateFireFx(item);
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .TargetingAllOpponents(CombatState)
        .Execute(choiceContext);

        foreach(Creature target in CombatState.HittableEnemies)
        {
            CardModel? victim = await ReedAttachCmd.GetOneToBurst(choiceContext, target, Owner, 0);
            if (victim != null)
            {
                if (victim is IFireFlower)
                {
                    await ReedAttachCmd.Burnt(choiceContext, victim, target, times:1+DynamicVars.Repeat.IntValue);
                }
                ResistanceSystem.RemoveSpark(target, Owner, victim);
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
        DynamicVars.Repeat.UpgradeValueBy(1);
    }
}