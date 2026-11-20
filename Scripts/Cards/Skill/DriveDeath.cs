using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 驱策死亡：给予 1（升级 2）层易伤；若你本回合还没移除过火花牌，再附着一张巡标火花。
/// 后半句成立时卡面金色高亮（<see cref="ShouldGlowGoldInternal"/>，同原版 Evil Eye / Clash 的提示）。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class DriveDeath : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<VulnerablePower>("VulnerablePower", 1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<PatrolFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public DriveDeath() : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {

    }

    /// <summary>本回合还没移除过火花牌 → 现在打出会额外附着巡标：金色高亮。
    /// 规范卡没有 Owner，取不到就按不高亮处理。</summary>
    protected override bool ShouldGlowGoldInternal
        => !ResistanceSystem.HasRemovedSparkThisTurn(IsMutable ? Owner : null);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target == null)
        {
            return;
        }
        await PowerCmd.Apply<VulnerablePower>(choiceContext, cardPlay.Target,
            DynamicVars["VulnerablePower"].BaseValue, Owner.Creature, this);

        if (!ResistanceSystem.HasRemovedSparkThisTurn(Owner))
        {
            await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<PatrolFlower>(), cardPlay.Target);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["VulnerablePower"].UpgradeValueBy(1);
    }
}
