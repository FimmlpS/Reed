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

    public static CardModel CreateFireFlower<T>(this CardModel cardModel) where T : CardModel
    {
        return cardModel.CombatState.CreateCard<T>(cardModel.Owner);
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

        List<CardModel> shivs = new List<CardModel>();
        for (int i = 0; i < count; i++)
        {
            shivs.Add(card.CombatState?.CreateCard<T>(card.Owner));
        }

        await CardPileCmd.AddGeneratedCardsToCombat(shivs, PileType.Hand, creator ?? card.Owner);
        return shivs;
    }
}