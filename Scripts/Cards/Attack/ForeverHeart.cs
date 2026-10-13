using BaseLib.Utils;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.CardSelection;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class ForeverHeart : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8,ValueProp.Move),
        // 选择屏标题里的 {Cards:diff()} 得有个同名变量才能格式化成数字
        // （否则 SmartFormat 找不到 Cards，会退回显示原始文本并记一条本地化错误）。
        new CardsVar(1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Step),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach)
    ];

    public ForeverHeart() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .Execute(choiceContext);

        List<CardModel> selection = new();
        foreach (IGrouping<ModelId, CardModel> group in ResistanceSystem.SparksOf(cardPlay.Target, Owner).GroupBy(c => c.Id))
        {
            if (group.Count() >= 2)
            {
                selection.Add(this.CreateFireFlower(ModelDb.GetById<CardModel>(group.Key)));
            }
        }
        if (selection.Count == 0)
        {
            return; // 没有成对的同名火花：不弹选择屏（也免得白开一次选牌）
        }

        CardModel? card = (await CardSelectCmd.FromSimpleGrid(choiceContext, selection, Owner, new CardSelectorPrefs(SelectionScreenPrompt,1))).FirstOrDefault();
        if (card != null)
        {
             await ReedAttachCmd.Attach(choiceContext, card, cardPlay.Target);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
    }
}