using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class MeltCalamity : AbstractReedCard
{
    private const string _calculatedCardsKey = "CalculatedCards";

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(7,ValueProp.Move),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar("CalculatedCards").WithMultiplier((CardModel card, Creature? target) => {
            if(target == null || card.Owner == null){
                return 0;
            }
            int count = 0;
            foreach(CardModel f in ResistanceSystem.SparksOf(target,card.Owner)){
                if(f is IFireFlower flower){
                    count++;
                    // 进阶形态记作 2 张。走 IFireFlower.IsAdvanced 的显式声明，而不是 GetUpperCard()==null
                    // 反推 —— 后者只是“没有上级形态”，某张火花忘了声明上级就会被静默当成进阶牌多算一份伤害。
                    if(flower.IsAdvanced){
                        count++;
                    }
                }
            }
            return count;
        })
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower),
        HoverTipFactory.FromKeyword(ReedKeywords.Step)
    ];

    public MeltCalamity() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .WithHitCount((int)((CalculatedVar)base.DynamicVars["CalculatedCards"]).Calculate(cardPlay.Target))
        .OnlyPlayAnimOnce()
        .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}