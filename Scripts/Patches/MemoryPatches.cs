using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using Reed.Scripts.Memory;

namespace Reed.Scripts.Patches;

/// <summary>
/// 给每一次出牌盖上「视为在第几回合打出」的戳（见 <see cref="MemoryPlayRound"/>）。
///
/// <para>用**普通 postfix**而不是状态机注入：<see cref="Hook.BeforeCardPlayed"/> 是 <c>async Task</c>，
/// 编译器生成的外层 stub 不会被 JIT 内联，postfix 在它返回（即状态机跑到第一个 await）时执行——
/// 此时正好在 <c>OnPlay</c> 之前，卡牌自身的追忆逻辑读 <c>PlayedRound()</c> 拿到的就是正确的基准回合。
/// 这一步只是往副作用表里写一个 int，**不需要 await**，所以没必要动状态机。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
public static class MemoryStampPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardPlay cardPlay)
    {
        cardPlay.StampPendingRound();
    }
}

/// <summary>
/// 记录玩家手动打出的牌（见 <see cref="MemorySystem.CaptureAsync"/>）。
///
/// <para><b>手法：把返回的 Task 包一层。</b><see cref="Hook.AfterCardPlayed"/> 是 <c>async Task</c>，
/// 它的 postfix 只在「方法体第一次挂起、stub 返回 Task」时执行，那还不是 hook 跑完的时刻。
/// 所以这里用 <c>ref __result</c> 把返回的 Task 换成「先 <c>await</c> 原 Task，再 <c>await</c> 记录流程」
/// 的新 Task；调用方（<c>CardModel.OnPlayWrapper</c>）<c>await</c> 到的是这个新 Task，于是
/// <see cref="MemorySystem.RememberedAsync"/> 上的异步副作用（VFX / <c>CardCmd.*</c> / 弹选择）
/// 会被**真正等完**，不会再出现「牌已经移出 Play 堆、战斗已经往前走，异步逻辑才收尾」的错位。</para>
///
/// <para>时机依然安全：<c>OnPlayWrapper</c> 是 <c>await</c> 完整个 hook 之后才把牌移出
/// <see cref="PileType.Play"/>，所以此刻 <c>CreateClone()</c> 合法（它要求牌在战斗牌堆里）。</para>
///
/// <para><b>为什么不用 BaseLib 的状态机注入（<c>AsyncMethodCall</c>）</b>：该方法在
/// <see cref="Hook.AfterCardPlayed"/> 上已经压着两个 <c>beforeState: original</c> 注入
///（BaseLib 的 <c>AfterCardPlayedPatch</c> 与 <c>BurnHealPatch</c>），而 <c>afterState: original</c>
/// 插到「最后一个 <c>AfterCardPlayedLate</c> 监听者之后」会让 Harmony 生成非法 IL
///（实测 <c>InvalidProgramException</c>，会导致整个 mod 的 <c>PatchAll</c> 中断）。
/// 包 Task 完全不碰 IL，与任何 transpiler 都互不干扰，语义还更准：真的是「所有监听者跑完之后」。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
public static class MemoryCapturePatch
{
    [HarmonyPostfix]
    private static void Postfix(
        ref Task __result, ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Task hook = __result;
        __result = CaptureWhenHookCompletes(hook, combatState, choiceContext, cardPlay);
    }

    /// <summary>
    /// 替换掉 <see cref="Hook.AfterCardPlayed"/> 的返回任务：先让原 hook 完整跑完
    ///（含全部 <c>AfterCardPlayed</c> / <c>AfterCardPlayedLate</c> 监听者），再执行记录。
    /// 原 hook 抛异常时异常原样向上传播，记录流程不执行。
    /// </summary>
    private static async Task CaptureWhenHookCompletes(
        Task hook, ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await hook;
        await MemorySystem.CaptureAsync(combatState, choiceContext, cardPlay);
    }
}
