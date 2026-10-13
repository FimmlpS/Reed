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
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class Clash : AbstractReedCard
{
    private const string _calculatedCardsKey = "CalculatedCards";

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8,ValueProp.Move),
        new BurnVar(1),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar("CalculatedCards").WithMultiplier((CardModel card, Creature? _) => {
            if(!card.IsInCombat){
                return 0;
            }
            HashSet<CardType> types = new HashSet<CardType>();
            foreach(CardModel c in PileType.Hand.GetPile(card.Owner).Cards.ToList()){
                if(c.Type!=CardType.Attack)
                    types.Add(c.Type);
            }
            return types.Count;
        })
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public Clash() : base(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int count = (int)((CalculatedVar)base.DynamicVars["CalculatedCards"]).Calculate(cardPlay.Target);
        if (count > 0)
        {
            await CreatureCmd.TriggerAnim(Owner.Creature,"Attack",0.1f);
        }
        for(int i = 0; i < count; i++)
        {
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithNoAttackerAnim()
            .Execute(choiceContext);

            await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}