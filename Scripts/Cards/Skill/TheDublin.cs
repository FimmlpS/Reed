using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Commands;
using Reed.Scripts.Powers;
using Reed.Scripts.Memory;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class TheDublin : AbstractReedCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromPower<RecallPower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Recall)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<RecallPower>("RecallPower",1)
    ];

    public TheDublin() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<RecallPower>(choiceContext,Owner.Creature,DynamicVars["RecallPower"].BaseValue,Owner.Creature,this);

        if (!cardPlay.IsFromRecall())
        {
            return;
        }

        CardModel source = cardPlay.Card.DupeOf ?? cardPlay.Card;
        CardModel copy = source.CreateCloneForPlayer(Owner);
        CardPileAddResult result = await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, Owner, CardPilePosition.Bottom);

        // 补飞卡动画。生成牌手里没有现成的 NCard 节点，CardPileCmd.Add 找不到可移动的节点就直接跳过动画
        // （GetTweenForCardsChangingPiles 只在 oldPile 非空时才飞），于是弃牌堆的 CardAddFinished 没人触发 ——
        // 图标上的计数正是靠它 +1（NCombatCardPile.AddCard）。原版所有「生成一张牌进弃牌堆」的牌
        // （愤怒 / 适应打击 …）都在 Add 之后补这一下。
        CardCmd.PreviewCardPileAdd(result);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["RecallPower"].UpgradeValueBy(1);
    }
}