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
using Reed.Scripts.Cards.Flower;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class DragonOath : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(10,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<BurnMarkFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public DragonOath() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        if (IsUpgraded)
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<BurnMarkFlower>(), cardPlay.Target);
            CardCmd.Downgrade(this);
            NCard.FindOnTable(this)?.UpdateVisuals(Pile?.Type??PileType.None,CardPreviewMode.None);
        }
        else
        {
            CardModel? victim = await ReedAttachCmd.PickOneToBurst(choiceContext, cardPlay.Target, Owner);
            if (victim != null)
            {
                if (victim is IFireFlower flower)
                {
                    await ReedAttachCmd.Burnt(choiceContext, victim, cardPlay.Target);
                }
                await ReedAttachCmd.Detach(choiceContext, cardPlay.Target, Owner, victim);
            }
            CardCmd.Upgrade(this);
            NCard.FindOnTable(this)?.UpdateVisuals(Pile?.Type??PileType.None,CardPreviewMode.None);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(-2);
    }
}