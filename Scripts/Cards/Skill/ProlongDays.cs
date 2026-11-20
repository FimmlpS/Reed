using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 续昼：引爆目标身上你的所有火花牌，再按被引爆的每一张补一张火花 ——
/// 原本未进阶的补[巡标]，原本已进阶的补[旗帜]。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class ProlongDays : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<PatrolFlower>(),
        HoverTipFactory.FromCard<FlagFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public ProlongDays() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        // 快照：引爆/取下会改动火花表。先记下每张是否进阶，再统一清算，
        // 最后才补牌 —— 这样补牌时目标身上已经空出来，不会撞上限、也不会误引爆旧火花。
        List<bool> advanced = new();
        foreach (CardModel spark in ResistanceSystem.SparksOf(target, Owner).ToList())
        {
            advanced.Add(spark is IFireFlower { IsAdvanced: true });
            if (spark is IFireFlower)
            {
                await ReedAttachCmd.Burnt(choiceContext, spark, target);
            }
            await ReedAttachCmd.Detach(choiceContext, target, Owner, spark);
        }

        // 按揭开顺序补：未进阶 → 巡标，已进阶 → 旗帜。
        foreach (bool wasAdvanced in advanced)
        {
            CardModel replacement = wasAdvanced
                ? this.CreateFireFlower<FlagFlower>()
                : this.CreateFireFlower<PatrolFlower>();
            await ReedAttachCmd.Attach(choiceContext, replacement, target);
        }
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
