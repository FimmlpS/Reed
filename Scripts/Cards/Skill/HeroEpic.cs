using BaseLib.Utils;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class HeroEpic : AbstractReedCard, IMemorySubscriber
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public HeroEpic() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {

    }

    public static bool CanBeRecalled(CardModel card) => card is not IMemorySubscriber;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 手动打出：牌效不发动（消耗与「被记进回忆」由常规流程负责，这里什么都不用做）。
        if (!cardPlay.IsFromRecall())
        {
            return;
        }

        // 界面与真正的追忆必须用**同一个**谓词，否则界面会承诺一些打不出来的牌。
        int? chosen = await MemoryRoundSelectScreen.Select(Owner, CanBeRecalled);
        if (chosen is not int round)
        {
            return; // 取消：不追忆任何牌（消耗照旧）
        }

        await RecallCmd.RecallAll(choiceContext, Owner, round, CanBeRecalled);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    IReadOnlyList<CardModel> IMemorySubscriber.GetRecallPreview(MemoryPreviewContext context)
    {
        return [];
    }

}
