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
using MegaCrit.Sts2.Core.Entities.Players;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class WhereSpiritsGo : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
       new BurnVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<BurnMarkFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;

    public WhereSpiritsGo() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        List<Player> players = CombatState.Players.ToList();
        foreach(Player player in players)
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<BurnMarkFlower>(player), cardPlay.Target);
        }
        foreach(Player player in players)
        {
            await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
            .FromCard(this, cardPlay)
            .FromCreature(player.Creature)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Burn().UpgradeValueBy(1);
    }
}