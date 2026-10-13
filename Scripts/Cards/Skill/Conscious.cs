using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Commands;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Conscious : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromPower<StrengthPower>()
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<StrengthPower>(3)
    ];

    public Conscious() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        List<MonsterModel> targets = new List<MonsterModel>();
        if (!cardPlay.IsAutoPlay)
        {
            if(cardPlay.Target?.IsMonster??false)
                targets.Add(cardPlay.Target.Monster);
        }
        else
        {
            foreach(Creature creature in Owner.Creature.CombatState.GetOpponentsOf(Owner.Creature))
            {
                if (creature.IsMonster)
                {
                    targets.Add(creature.Monster);
                }
            }
        }
        foreach(MonsterModel monster in targets)
        {
            if (monster.IntendsToAttack)
            {
                await PowerCmd.Apply<StrengthPower>(choiceContext, monster.Creature, -DynamicVars.Strength.BaseValue, Owner.Creature, this);
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Strength.UpgradeValueBy(1);
    }
}
