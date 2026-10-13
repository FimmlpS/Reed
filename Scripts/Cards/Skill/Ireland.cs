using Godot;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class Ireland : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower),
        HoverTipFactory.FromKeyword(ReedKeywords.Step)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public Ireland() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        CardModel? spark = await PickSteppableSpark(choiceContext, target, Owner);
        if (spark == null)
        {
            return;
        }

        await ReedAttachCmd.UpgradeSpark(choiceContext, target, spark);
    }

    private static async Task<CardModel?> PickSteppableSpark(PlayerChoiceContext? choiceContext, Creature target, Player owner)
    {
        List<CardModel> candidates = ResistanceSystem.SparksOf(target, owner)
            .Where(s => s is IFireFlower flower && flower.GetUpperCard() != null)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        if (choiceContext == null)
        {
            return candidates[candidates.Count - 1];
        }

        CardSelectorPrefs prefs = new CardSelectorPrefs(StepPrompt, 1);
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(choiceContext, candidates, owner, prefs);
        return chosen.FirstOrDefault();
    }

    private static LocString StepPrompt
    {
        get
        {
            LocString? prompt = null;
            try
            {
                prompt = LocString.GetIfExists("static_hover_tips", "REED-SPARK.step-select.title");
            }
            catch (Exception e)
            {
                GD.PushWarning($"[Ireland] 读取进阶选择标题失败：{e.Message}");
            }
            return prompt ?? new LocString("static_hover_tips", "REED-SPARK.burst-select.title");
        }
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
