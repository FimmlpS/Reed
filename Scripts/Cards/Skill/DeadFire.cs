using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class DeadFire : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Ignite),
        HoverTipFactory.FromKeyword(ReedKeywords.Step),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public DeadFire() : base(3, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 1) 引爆所有敌人身上的火花牌：不是直接销毁，而是逐一摘下来收进 detonated
        List<CardModel> detonated = new();
        foreach (Creature enemy in CombatState.HittableEnemies)
        {
            foreach (CardModel spark in ResistanceSystem.SparksOf(enemy, Owner).ToList())
            {
                if (spark is IFireFlower)
                {
                    await ReedAttachCmd.Burnt(choiceContext, spark, enemy, blv: 2m); // 以200％伤害量引爆
                }
                ResistanceSystem.RemoveSpark(enemy, Owner, spark);
                detonated.Add(spark);
            }
        }

        // 2) 合并：凑够张数的同名火花进阶成上级火花（3 个敌人各一张普通 → 一张终末）
        List<CardModel> queue = ReedAttachCmd.MergeSparks(Owner, detonated);

        // 3) 随机附着：队列里每张火花随机挑一个还活着的敌人挂回去
        foreach (CardModel spark in queue)
        {
            Creature? target = CombatState.HittableEnemies
                .TakeRandom(1, CombatState.RunState.Rng.CombatTargets)
                .FirstOrDefault();
            if (target == null)
            {
                break; // 没有存活敌人：剩下的火花无处可去，作废
            }
            await ReedAttachCmd.Attach(choiceContext, spark, target);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
