using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Patches;

/// <summary>
/// 兜底：生物/能力已经离开战斗（死亡移除、撤退、房间回收）但 UI 节点还留着时，原版
/// <see cref="Creature.HoverTips"/> 会解引用 <c>CombatState.Allies</c>、
/// <see cref="PowerModel.HoverTips"/> 会解引用 <c>Owner.CombatState</c>，悬停一次刷一条 NRE。
/// 这两种状态本来就出不了正常提示，这里给空/简化提示，不碰任何结算。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.HoverTips), MethodType.Getter)]
internal static class CreatureHoverTipsGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Creature __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance.CombatState != null)
        {
            return true;
        }
        __result = [];
        return false;
    }
}

/// <summary>见 <see cref="CreatureHoverTipsGuardPatch"/>：能力已脱离生物（Owner 为空）或生物已离场时不走原版智能描述。</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
internal static class PowerHoverTipsGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PowerModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!__instance.IsMutable)
        {
            return true; // 规范卡：原版走 Description 分支，不碰 Owner
        }
        Creature owner;
        try
        {
            owner = __instance.Owner;
        }
        catch (Exception)
        {
            return true;
        }
        if (owner != null && owner.CombatState != null)
        {
            return true;
        }
        try
        {
            __result = __instance.IsVisible ? [__instance.DumbHoverTip] : [];
        }
        catch (Exception)
        {
            __result = [];
        }
        return false;
    }
}
