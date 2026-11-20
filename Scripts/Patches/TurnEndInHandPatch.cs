using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Patches;

/// <summary>
/// 回合结束时手牌里带 <c>HasTurnEndInHandEffect</c> 的牌（如焰光）由 CombatManager 直接调
/// <see cref="CardModel.OnTurnEndInHandWrapper"/> 结算，不产生 <see cref="MegaCrit.Sts2.Core.Entities.Cards.CardPlay"/>，
/// 因此收不到 <c>Hook.AfterCardPlayed</c> —— 心结的「自动打出获得格挡」在这里补一次。
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnTurnEndInHandWrapper))]
internal static class TurnEndInHandPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardModel __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        Task original = __result;
        __result = AwaitThenNotify(original, __instance, choiceContext);
    }

    private static async Task AwaitThenNotify(Task original, CardModel card, PlayerChoiceContext choiceContext)
    {
        await original;
        await HeartKnotPower.NotifyTurnEndInHand(choiceContext, card);
    }
}
