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
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Skill;

/// <summary>
/// 邀舞：附着一张巡标火花；再把目标的一张火花「移除并重新附着」成巡标 ——
/// 未升级只转化「基础」火花，升级后转化为任意非巡标火花。新巡标继承被转化那张的额外伤害。
/// </summary>
[Pool(typeof(ReedCardPool))]
public class InvitationToDance : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<PatrolFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public InvitationToDance() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target == null)
        {
            return;
        }

        // 先附着（与描述顺序一致）；随后转化时新附的巡标不在候选里，不会被自己转掉。
        await ReedAttachCmd.Attach(choiceContext, this.CreateFireFlower<PatrolFlower>(), target);

        CardModel? victim = await PickSparkToTransform(choiceContext, target, Owner);
        if (victim == null)
        {
            return;
        }
        await ReedAttachCmd.TransformSpark(choiceContext, target, Owner, victim, this.CreateFireFlower<PatrolFlower>());
    }

    private async Task<CardModel?> PickSparkToTransform(PlayerChoiceContext? choiceContext, Creature target, Player owner)
    {
        ModelId patrolId = ModelDb.Card<PatrolFlower>().Id;
        ModelId basicId = ModelDb.Card<NormalFlower>().Id;
        List<CardModel> candidates = ResistanceSystem.SparksOf(target, owner)
            .Where(s => IsUpgraded ? s.Id != patrolId : s.Id == basicId)
            .ToList();
        if (candidates.Count == 0)
        {
            return null; // 没有可转化的火花：不弹选择屏
        }
        if (choiceContext == null)
        {
            return candidates[^1]; // 问不了玩家时确定性取一张（多人两边一致）
        }

        CardSelectorPrefs prefs = new CardSelectorPrefs(TransformPrompt, 1);
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(choiceContext, candidates, owner, prefs);
        return chosen.FirstOrDefault();
    }

    private static LocString TransformPrompt
    {
        get
        {
            LocString? prompt = null;
            try
            {
                prompt = LocString.GetIfExists("static_hover_tips", "REED-SPARK.transform-select.title");
            }
            catch (Exception e)
            {
                GD.PushWarning($"[InvitationToDance] 读取转化选择标题失败：{e.Message}");
            }
            return prompt ?? new LocString("static_hover_tips", "REED-SPARK.burst-select.title");
        }
    }

    protected override void OnUpgrade()
    {
        // 升级只放宽候选范围（描述里的 {IfUpgraded:show:...}），没有数值改动。
    }
}
