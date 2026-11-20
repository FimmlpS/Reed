using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 向大地诉说：把一张手牌记进**玩家级账本**（<see cref="RoundZeroLedger"/>，以后每场战斗的第 0 回合都会有它，
/// 与「无名诗集」共用同一个桶），然后从**本场战斗**已有的第 0 回合里追忆一张技能牌（升级后含攻击牌）。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class SpeakToTerra : AbstractReedCard, IMemorySubscriber
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust,
        CardKeyword.Innate
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Recall),
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public SpeakToTerra() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 上半句：记一张手牌。写进账本（跨战斗）+ 本场第 0 回合（立刻能追忆）。
        IEnumerable<CardModel> picked = await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(Prompt, 1),
            c=>c is not SpeakToTerra,
            this);
        foreach (CardModel card in picked)
        {
            await RoundZeroLedger.AddAsync(choiceContext, Owner, CombatState, card);
        }

        // 下半句：从**本场战斗已有的**第 0 回合追忆（刚记进去的那张就在里面，追忆会把它取走）。
        await RecallCmd.Recall(choiceContext, Owner)
            .FromRound(MemorySystem.CarriedRound)
            .Where(CanRecall)
            .Execute(choiceContext);
    }

    /// <summary>追忆口径：技能牌；升级后也允许攻击牌（与 TO_DUST 同款）。</summary>
    private bool CanRecall(CardModel card)
        => card.Type == CardType.Skill || (IsUpgraded && card.Type == CardType.Attack);

    public IReadOnlyList<CardModel> GetRecallPreview(MemoryPreviewContext context)
        => context.LatestAt(MemorySystem.CarriedRound, CanRecall);

    /// <summary>
    /// 选牌标题：key 见 <c>Reed/localization/zhs/static_hover_tips.json</c>。
    /// 缺文案时退回「记录一张牌」那条（已在的 key），不让缺 key 把整次出牌搞崩。
    /// </summary>
    private static LocString Prompt
    {
        get
        {
            try
            {
                if (LocString.GetIfExists("static_hover_tips", "REED-SPEAK_TO_TERRA.select.title") is { } prompt)
                {
                    return prompt;
                }
            }
            catch (Exception e)
            {
                GD.PushWarning($"[SpeakToTerra] 读取选牌标题失败：{e.Message}");
            }
            return new LocString("static_hover_tips", "REED-RECORD.select.title");
        }
    }
}
