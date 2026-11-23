using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 旧日残骸：选择目标身上的一张火花牌，使其伤害量提升 8（升级 12）。
/// 数值加法与 HUD 刷新照搬「旧日之火」；区别是只提升选中的那一张，且进阶牌不再额外翻倍。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class OldSkeletons : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Increase", 8)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower),
        HoverTipFactory.FromKeyword(ReedKeywords.Step)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public OldSkeletons() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        CardModel? spark = await PickSpark(choiceContext, target, Owner);
        if (spark == null)
        {
            return;
        }
        IncreaseDamage(spark, DynamicVars["Increase"].BaseValue);

        NCard? hud = ResistanceSystem.GetData(target)?.Bar?.Spark?.FindCardByModel(spark);
        if (hud != null)
        {
            hud.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            // UpdateVisuals 只写基准值，这里补一次数值修正。
            SparkHudDriver.RefreshCardDescription(hud);
        }
    }

    private static async Task<CardModel?> PickSpark(PlayerChoiceContext? choiceContext, Creature target, Player owner)
    {
        List<CardModel> candidates = ResistanceSystem.SparksOf(target, owner).ToList();
        if (candidates.Count == 0)
        {
            return null; // 没有火花：不弹选择屏
        }
        if (choiceContext == null)
        {
            return candidates[^1]; // 问不了玩家时确定性取一张（多人两边一致）
        }

        CardSelectorPrefs prefs = new CardSelectorPrefs(SelectPrompt, 1);
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(choiceContext, candidates, owner, prefs);
        return chosen.FirstOrDefault();
    }

    private static LocString SelectPrompt
    {
        get
        {
            LocString? prompt = null;
            try
            {
                prompt = LocString.GetIfExists("static_hover_tips", "REED-SPARK.increase-select.title");
            }
            catch (Exception e)
            {
                GD.PushWarning($"[OldSkeletons] 读取选择标题失败：{e.Message}");
            }
            return prompt ?? new LocString("static_hover_tips", "REED-SPARK.burst-select.title");
        }
    }

    /// <summary>把提升值写进火花牌实际承载伤害的那个动态变量（与旧日之火同一套优先级）。</summary>
    private static void IncreaseDamage(CardModel card, decimal improveVal)
    {
        if (card.DynamicVars.ContainsKey("CalculatedDamage"))
        {
            card.DynamicVars.CalculatedDamage.BaseValue += improveVal;
        }
        else if (card.DynamicVars.ContainsKey("Damage"))
        {
            card.DynamicVars.Damage.BaseValue += improveVal;
        }
        else if (card.DynamicVars.ContainsKey("OstyDamage"))
        {
            card.DynamicVars.OstyDamage.BaseValue += improveVal;
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["Increase"].UpgradeValueBy(4);
    }
}
