using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Reed.Scripts.Memory;

/// <summary>
/// 「一张牌**进出回忆**」的系统事件订阅者：power / 卡牌实现它，就能在回忆被写进 / 被追忆掉的那一刻做事
/// （典型用法是「博物」<c>NaturalHistoryPower</c> —— 每进出 10 张牌抽 1 张）。
///
/// <para>与 <see cref="IMemorySubscriber"/> 的分工：那个是**只读的表现层**（悬浮卡牌时预览它会追忆到哪些牌，
/// 必须同步无副作用）；这个是**有副作用的结算层** —— 两个方法都会被 <c>await</c>，里面可以放心跑
/// <c>CardCmd.*</c> / <c>CreatureCmd.*</c> / 弹选择屏。</para>
///
/// <para>实现方式与 <c>IFireFlowerSubscriber</c> 完全一致：**不订阅、不注册**，由回忆层在事件发生时
/// 遍历所有订阅者模型挨个 <c>await</c>（<c>MemorySystem.CollectSubscribers</c>）。被遍历到的是两拨：</para>
/// <list type="number">
///   <item><c>ICombatState.IterateHookListeners()</c> —— 战斗中的 power / 遗物 / 牌堆里的牌，
///     以及每张牌自己的**附魔**与**痛苦**；</item>
///   <item>**玩家回忆里保存的每一张牌**，同样带上它们的附魔与痛苦 —— 回忆里的是不进牌堆的快照副本，
///     上面那条看不见它们，所以由回忆层显式展开。想让「躺」在回忆里的牌参与结算就靠这一拨。</item>
/// </list>
/// <para>所以：</para>
/// <list type="bullet">
///   <item>不用管生命周期（power 一被移出战斗，下次遍历自然就找不到它了）；</item>
///   <item>两个方法都有**默认空实现**，只实现关心的那个即可；</item>
///   <item>别在这里读「刚打完的牌」以外的战斗状态做缓存 —— 每次事件都会重新遍历一遍；</item>
///   <item>「如果**此牌**在回忆中才生效」的效果，用 <c>MemorySystem.IsInMemory(player, this, out int round)</c>
///     自证：牌堆里的那张牌与回忆里的快照是两个对象，**会各收到一次广播**（牌堆那份通常要挡掉）。</item>
/// </list>
/// </summary>
public interface IMemoryEventSubscriber
{
    /// <summary>
    /// 一张牌被**写进回忆**：手动打出的自动记录（<see cref="MemorySystem.CaptureAsync"/>）与效果记录
    /// （<see cref="MemorySystem.RecordAsync"/>，如裂解 / 残垣 / 书写历史 / 从历史中来）都会走到这里。
    /// </summary>
    Task AfterRemembered(PlayerChoiceContext choiceContext, Player player, CardModel card) => Task.CompletedTask;

    /// <summary>
    /// 一张牌被**追忆打出**（<see cref="RecallCmd"/> 打出的每一张复制品，含连锁追忆与 <c>Card()</c> 指定的递归支点）。
    /// 在这个事件里回忆条目**已经被消耗掉**了（<c>RecallCmd</c> 先移除再打出）。
    /// </summary>
    Task AfterRecalled(PlayerChoiceContext choiceContext, Player player, CardModel card) => Task.CompletedTask;

    /// <summary>
    /// **玩家回合开始**（<c>Hook.AfterPlayerTurnStart</c>：牌已经抽完、出牌阶段之前）时，
    /// 为回忆里的**每一条记录**各广播一次 —— <paramref name="entry"/> 就是那条记录
    /// （<see cref="RememberedCard.RoundNumber"/> + 快照卡）。
    ///
    /// <para>为什么按「每条记录」而不是「每个回合」广播：记录里躺着的正是卡牌快照本身，而快照不进任何牌堆，
    /// 收不到原版 hook；「记录所在回合 == 当前回合时才做事」（<c>LatentDetection</c> 的到期追忆）
    /// 只能靠这一拨广播自证 —— <c>ReferenceEquals(entry.Card, this)</c> 认领自己那一份，
    /// 再看 <c>entry.RoundNumber ==</c> 当前回合。别人的、还没到期的记录直接忽略。</para>
    ///
    /// <para><b>拿当前回合不要用 <c>this.CombatState</c></b>：回忆快照不进任何牌堆，
    /// <c>CardModel.CombatState</c> 只对「在战斗牌堆里」的牌返回非 null，对快照恒为 null ——
    /// 要用 <c>player.Creature.CombatState</c>（广播参数里的 <paramref name="player"/> 就是回忆的主人）。</para>
    ///
    /// <para>和其他事件一样会被 <c>await</c>，里面可以放心追忆（<see cref="RecallCmd"/>）、自动出牌。
    /// 写进**未来回合**的记录也会收到广播（那几份就是「还没到期」的）。</para>
    /// </summary>
    Task AfterRoundStarted(PlayerChoiceContext choiceContext, Player player, RememberedCard entry) => Task.CompletedTask;
}
