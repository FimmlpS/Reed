using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Spread : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite)
    ];

    public Spread() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? victim = await ReedAttachCmd.PickOneToBurst(choiceContext, cardPlay.Target, Owner);
        if (victim == null)
        {
            return;
        }
        if (victim is IFireFlower flower)
        {
            foreach(Creature target in CombatState.GetOpponentsOf(Owner.Creature).Where(c => c.IsAlive))
            {
                await ReedAttachCmd.Burnt(choiceContext, victim, target, blv:IsUpgraded?1.5m:1m);
            }

        }
        ResistanceSystem.RemoveSpark(cardPlay.Target, Owner, victim);
    }

    

    protected override void OnUpgrade()
    {

    }
}