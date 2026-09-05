using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Patches;

/// <summary>
/// 在血条初始化（NHealthBar.SetCreature，即生物状态条创建）时同步建立抗性条/数据。
/// </summary>
[HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.SetCreature))]
internal static class ResistanceBarNHealthBarPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHealthBar __instance, Creature creature)
    {
        ResistanceSystem.OnNHealthBarCreated(__instance, creature);
    }
}

/// <summary>
/// 生物状态条每次 SetCreatureBounds 时把抗性条对齐到其血条下方。
/// </summary>
[HarmonyPatch(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.SetCreatureBounds))]
internal static class ResistanceBarLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCreatureStateDisplay __instance)
    {
        ResistanceSystem.OnStateDisplayBounds(__instance);
    }
}

/// <summary>
/// 血条真正“落定条宽”的瞬间（含其内部延迟一帧的那次 SetHpBarContainerSizeWithOffsetsImmediately
/// 调用）后置重排抗性条。SL 恢复条宽、战斗中生物变大/改宽等任何路径都汇聚到这里，
/// 保证抗性条与血条永远同时刻对齐，不依赖 SetCreatureBounds 是否恰好被触发。
/// </summary>
[HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately")]
internal static class ResistanceBarGeometryPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHealthBar __instance)
    {
        ResistanceSystem.OnHealthBarGeometryChanged(__instance);
    }
}
