using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class FlameMoon : AbstractReedCard
{
    private const int EvolveRequired = 2;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(14, ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<ShineFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.Step)
    ];

    public FlameMoon() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(CombatState)
            .Execute(choiceContext);

        foreach (Creature target in CombatState.HittableEnemies.ToList())
        {
            // 火花已满且这张映耀合不出进阶：跳过（Attach 在满上限时会开引爆选择屏，AoE 卡不该弹它）
            if (!ResistanceSystem.CanAddSpark(target, Owner) && !CanEvolveShine(target))
            {
                continue;
            }
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<ShineFlower>(), target);
        }
    }

    /// <summary>目标身上已有 2 张同名「映耀」，再附一张就会合体进阶。</summary>
    private bool CanEvolveShine(Creature target)
    {
        ShineFlower shine = ModelDb.Card<ShineFlower>();
        if (shine is not IFireFlower flower || flower.GetUpperCard() == null)
        {
            return false; // 没有进阶形态：只靠上限判断
        }
        return ResistanceSystem.SparksOf(target, Owner).Count(s => s.Id == shine.Id) >= EvolveRequired;
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}
