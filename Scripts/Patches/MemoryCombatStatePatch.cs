using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Patches;

/// <summary>
/// 回忆里的记录牌不在任何牌堆里，原版 <see cref="CardModel.CombatState"/> 对它恒为 null
/// （只有「在战斗牌堆里」或「UpgradePreviewType == Combat」才返回）。
/// 这里让它在战斗期间退回 owner 的 CombatState —— 其他 mod 的牌直接用 <c>this.CombatState</c> 时不会拿到 null。
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.CombatState), MethodType.Getter)]
internal static class MemoryCombatStatePatch
{
    [HarmonyPostfix]
    private static void Postfix(CardModel __instance, ref ICombatState? __result)
    {
        if (__result != null || !__instance.IsMutable || !MemorySystem.IsRememberedSnapshot(__instance))
        {
            return;
        }
        __result = __instance.Owner?.Creature?.CombatState;
    }
}
