using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Models;
using BaseLib.Patches.Features;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class TowardLife : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(2),
        new EnergyVar(1)
    ];

    public TowardLife() : base(1, CardType.Skill, CardRarity.Common, CustomTargetType.Anyone)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext,DynamicVars.Cards.BaseValue, Owner);
        CardModel? victim = await ReedAttachCmd.PickOneToBurst(choiceContext, cardPlay.Target, Owner);
        if (victim == null)
        {
            return;
        }
        if (victim is IFireFlower flower)
        {
            await ReedAttachCmd.Burnt(choiceContext, victim, cardPlay.Target);
        }
        await ReedAttachCmd.Detach(choiceContext, cardPlay.Target, Owner, victim);
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue,Owner);
    }

    

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}