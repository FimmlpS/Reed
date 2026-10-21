using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Patches;

/// <summary>
/// 战斗开始的生命周期补丁：把「清掉上一场残留的火花/抗性状态」从“第一次建血条时惰性订阅 CombatSetUp”改成
/// 绑在 <see cref="CombatManager.SetUpCombat"/> 上。
///
/// <para><b>为什么</b>：惰性订阅依赖 UI（NHealthBar.SetCreature 建条）与 CombatSetUp 谁先谁后，而多人里
/// 各客户端的 UI 时序并不一致 —— 一旦某个客户端错过 CombatSetUp，它这场的 <c>_attachedSparkCount</c>
/// 就不会归零、跨战斗残留的火花也不会被清掉，两个客户端的火花表从此各算各的（表现为结算时突然
/// “State divergence detected”，或者“火花一爆就炸”）。Harmony 补丁在宿主与客户端上以完全相同的位置执行，
/// 与 UI 无关；再用 state 实例去重，保证一场战斗只跑一次（事件那条路也还可能到）。</para>
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class CombatSetupPatch
{
    [HarmonyPostfix]
    private static void Postfix(CombatState state)
    {
        ResistanceSystem.HandleCombatSetUp(state);
    }
}
