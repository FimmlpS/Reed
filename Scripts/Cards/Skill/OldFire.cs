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
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class OldFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new DynamicVar("Increase",2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<NormalFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.Step)
    ];

    public OldFire() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<NormalFlower>(), cardPlay.Target);
        foreach(CardModel card in ResistanceSystem.SparksOf(cardPlay.Target, Owner))
        {
            decimal improveVal = DynamicVars["Increase"].BaseValue;
            if(card is IFireFlower f && f.IsAdvanced)
            {
                improveVal*=2;
            }
            //damage
            if (card.DynamicVars.ContainsKey("CalculatedDamage"))
            {
                card.DynamicVars.CalculatedDamage.BaseValue += improveVal;
            }
            else if(card.DynamicVars.ContainsKey("Damage"))
            {
                card.DynamicVars.Damage.BaseValue += improveVal;
            }
            else if(card.DynamicVars.ContainsKey("OstyDamage"))
            {
                card.DynamicVars.OstyDamage.BaseValue += improveVal;
            }
            NCard? hud = ResistanceSystem.GetData(cardPlay.Target)?.Bar?.Spark?.FindCardByModel(card);
            if (hud != null)
            {
                hud.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                SparkHudDriver.RefreshCardDescription(hud);
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["Increase"].UpgradeValueBy(2);
    }
}