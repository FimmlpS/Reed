using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 「枯焰生花」：获得格挡，引爆一张火花牌，然后从**上一回合**的回忆中追忆一张技能牌
/// （自动打出，并从回忆里移除）。
///
/// <para>引爆部分照抄 <see cref="Spread"/>：<see cref="ReedAttachCmd.PickOneToBurst"/> 让玩家从目标
/// 身上的火花里选一张，<see cref="ReedAttachCmd.Burnt"/> 触发它的爆发效果，再
/// <see cref="ResistanceSystem.RemoveSpark"/> 移除（卡行消散动画由移除事件驱动）。目标身上没有火花时
/// 该段整体跳过，后面的追忆照常执行。</para>
///
/// <para>基准回合取 <c>cardPlay</c>（<see cref="RecallCmd.FromPlay"/>），被追忆打出时依旧以「被记住的
/// 那个回合」为基准 → 支持连锁追忆。</para>
///
/// <para>实现 <see cref="IMemorySubscriber"/>：悬浮时在回忆图标处扇形预览「上一回合可追忆的技能牌」。</para>
/// </summary>
[Pool(typeof(ReedCardPool))]
public class BornFlower : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(5,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public BornFlower() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .OfType(CardType.Skill)
        .Execute(choiceContext);
    }

    /// <summary>悬浮预览：上一回合（本牌基准回合 -1）里最新的至多 5 张技能牌。</summary>
    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c => c.Type == CardType.Skill);

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}
