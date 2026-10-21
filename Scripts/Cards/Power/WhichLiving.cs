using BaseLib.Utils;
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
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards.Power;

[Pool(typeof(ReedCardPool))]
public class WhichLiving : AbstractReedCard
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.FireFlower),
        HoverTipFactory.FromCard<LivingAlive>(),
        HoverTipFactory.FromCard<LivingDead>()
    ];

    public WhichLiving() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        WhichLivingForm form = await ChooseForm(choiceContext);
        if (form == WhichLivingForm.None)
        {
            return; // 没能问出形态（战斗已结束等）：不选形态也就没有后面的灼烧
        }

        // 形态是玩家身上的一个能力（唯一实体）：已经持有就只改它的形态位，没有才新施加。
        // 「已有则直接操作该实体」是设计约定 —— 反复打出本牌不应该叠出第二个能力。
        WhichLivingPower? power = Owner.Creature.GetPower<WhichLivingPower>();
        decimal amount = LivingForm.ToAmount(form);
        if (power == null)
        {
            await PowerCmd.Apply<WhichLivingPower>(choiceContext, Owner.Creature, amount, Owner.Creature, this);
        }
        else if (power.Amount != amount)
        {
            await PowerCmd.ModifyAmount(choiceContext, power, amount - power.Amount, Owner.Creature, this);
        }

        // 已经挂出去的火花立刻跟上新形态；之后才挂上去的由 ReedAttachCmd 在 attach 收尾时补上。
        LivingForm.ApplyToAllSparksOf(Owner);

        // 灼烧「其全部抗性」：每名敌人以自己的当前抗性为灼烧量，逐个单独结算。
        List<Creature> enemies = CombatState?.HittableEnemies.ToList() ?? [];
        await ReedBurnCmd.BurnAllResistance()
            .FromCard(this, cardPlay)
            .Targeting(enemies)
            .Execute(choiceContext);
    }

    /// <summary>让玩家在「生灵 / 死灵」两张 token 卡里选一张。只选 1 张、不可取消（点卡即选）。</summary>
    private async Task<WhichLivingForm> ChooseForm(PlayerChoiceContext choiceContext)
    {
        // 不能把 ModelDb 的规范模板塞进选择屏：NSimpleCardSelectScreen 会读 _cards.First().Owner，
        // 而 CardModel.Owner 的 getter 带 AssertMutable —— 规范卡会抛 CanonicalModelException，
        // 连带让 _Ready 半途中断（_confirmButton 没赋值，点卡时 NRE）。
        // 照 CreateFireFlower / LightShadowPower 的做法造两张归属玩家本人的可变副本：纯界面 token，不进任何牌堆。
        if (CombatState is not { } combat)
        {
            return WhichLivingForm.None;
        }
        List<CardModel> options = [
            combat.CreateCard<LivingAlive>(Owner),
            combat.CreateCard<LivingDead>(Owner)
        ];
        CardSelectorPrefs prefs = new CardSelectorPrefs(SelectPrompt, 1);
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(choiceContext, options, Owner, prefs);
        return chosen.FirstOrDefault() switch
        {
            LivingDead => WhichLivingForm.Dead,
            LivingAlive => WhichLivingForm.Alive,
            _ => WhichLivingForm.None,
        };
    }

    /// <summary>选择屏标题。每次都要新建一个 LocString：CardSelectorPrefs 会往上 Add 参数（Amount/MinCount/MaxCount）。</summary>
    private static LocString SelectPrompt
        => LocString.GetIfExists("static_hover_tips", "REED-WHICH_LIVING.select.title")
           ?? new LocString("static_hover_tips", "REED-SPARK.step-select.title");

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
