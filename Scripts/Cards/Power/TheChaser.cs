using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Powers;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Helpers;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class TheChaser : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<LightPower>("LightPower",2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        ..HoverTipFactory.FromEnchantment<ChaseLight>()
    ];

    public TheChaser() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ChaseLight model = ModelDb.Enchantment<ChaseLight>();
        CardModel cardModel = (await CardSelectCmd.FromHand(choiceContext,Owner, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), c=>model.CanEnchant(c),this)).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Enchant(model.ToMutable(), cardModel, 1m);
            NCardEnchantVfx nCardEnchantVfx = NCardEnchantVfx.Create(cardModel);
            if (nCardEnchantVfx != null)
            {
                NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(nCardEnchantVfx);
            }
        }
        await PowerCmd.Apply<LightPower>(choiceContext, Owner.Creature, DynamicVars["LightPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["LightPower"].UpgradeValueBy(1);
    }
}