using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;

namespace Reed.Scripts.DynamicVars;

public class BurnVar : DynamicVar
{
    public const string BurnDynamicName = "Reed_Burn";

    public BurnVar(decimal baseValue) : base(BurnDynamicName,baseValue){}
}

public static class BurnExtensions
{
    public static DynamicVar Burn(this DynamicVarSet dynamicVars)
    {
        return dynamicVars[BurnVar.BurnDynamicName];
    }

    public static CardModel CreateFireFlower<T>(this CardModel cardModel, Player? player = null) where T : CardModel
    {
        return cardModel.CombatState.CreateCard<T>(player??cardModel.Owner);
    }

    public static CardModel CreateFireFlower(this CardModel cardModel, CardModel source, Player? player = null)
    {
        return cardModel.CombatState.CreateCard(source, player??cardModel.Owner);
    }

    public static async Task<CardModel?> CreateInHand<T>(this CardModel card, Player? creator = null) where T : CardModel
    {
        return (await CreateInHand<T>(card, 1, creator)).FirstOrDefault();
    }

    public static async Task<IEnumerable<CardModel>> CreateInHand<T>(this CardModel card, int count, Player? creator = null) where T : CardModel
    {
        if (count == 0)
        {
            return Array.Empty<CardModel>();
        }

        if (CombatManager.Instance.IsOverOrEnding)
        {
            return Array.Empty<CardModel>();
        }

        // 发起牌不一定在牌堆里（回忆里的快照副本就不在），战斗上下文与归属都退回玩家身上取
        Player? owner = creator ?? card.Owner;
        ICombatState? combat = card.CombatState ?? owner?.Creature.CombatState;
        if (owner == null || combat == null)
        {
            return Array.Empty<CardModel>();
        }

        List<CardModel> shivs = new List<CardModel>();
        for (int i = 0; i < count; i++)
        {
            shivs.Add(combat.CreateCard<T>(owner));
        }

        await CardPileCmd.AddGeneratedCardsToCombat(shivs, PileType.Hand, owner);
        return shivs;
    }
}