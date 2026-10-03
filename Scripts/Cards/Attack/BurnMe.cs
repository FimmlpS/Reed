using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Reed.Scripts.Cards.Attack;

/// <summary>
/// 「照我以火」：造成伤害，然后从**上一回合**的回忆中追忆一张攻击牌（自动打出，并从回忆里移除）。
///
/// <para>基准回合取 <c>cardPlay</c>（<see cref="RecallCmd.FromPlay"/>）：这张牌本身若是被追忆打出的，
/// 它的「视为打出回合」仍是它<b>被记住的那个回合</b>，于是「上一回合」会继续向前追溯 —— 连锁追忆成立。</para>
///
/// <para>实现 <see cref="IMemorySubscriber"/>：鼠标悬浮时在回忆图标处扇形预览「上一回合可追忆的攻击牌」
/// （<see cref="MemoryPreviewContext.Latest"/>，与本牌实际取候选的逻辑同一份）。</para>
/// </summary>
[Pool(typeof(ReedCardPool))]
public class BurnMe : AbstractReedCard, IMemorySubscriber
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6,ValueProp.Move)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public BurnMe() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        await RecallCmd.Recall(choiceContext, Owner)
        .FromPlay(cardPlay)
        .Offset(-1)
        .OfType(CardType.Attack)
        .Execute(choiceContext);
    }

    /// <summary>悬浮预览：上一回合（本牌基准回合 -1）里最新的至多 5 张攻击牌。</summary>
    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.Latest(-1, c => c.Type == CardType.Attack);

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}
