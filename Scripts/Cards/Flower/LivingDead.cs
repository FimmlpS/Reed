using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.CardPools;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Flower;

/// <summary>「生死之灵」的形态选择卡：只在选择屏里出现，永远不进任何牌堆，打出无效果（效果在 WhichLiving 里结算）。</summary>
[Pool(typeof(TokenCardPool))]
public class LivingDead : AbstractReedCard
{
    public LivingDead() : base(-1, CardType.Status, CardRarity.Token, TargetType.Self)
    {

    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;
}
