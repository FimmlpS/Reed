using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Nodes;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 火花牌“正常 attach”的唯一入口。任何一张火花牌要挂到某生物身上，都应调用 <see cref="Attach"/>，
/// 而不是直接 <see cref="ResistanceSystem.GiveSpark"/>。它会按固定顺序处理完整流程：
/// <para>
///   1) 进阶判断：当目标生物（该玩家的视角下）已有 ≥2 张与“即将 attach 的卡”同 <see cref="CardModel.Id"/>
///      （ModelId）的火花牌，且即将 attach 的卡是 <see cref="IFireFlower"/> 且其
///      <see cref="IFireFlower.GetUpperCard"/> 返回了上级卡时 —— 先把这些同 id 的已有火花从目标上取下
///      （触发各自 <see cref="IFireFlower.OnRemoved"/> 并 <see cref="ResistanceSystem.RemoveSpark"/>，
///      卡行消散动画随事件播放），再【改为 attach 一张进阶后的卡】。因为每合并一次目标上的火花净减少一张，
///      循环必然终止，因此可支持 A→B→C 链式进阶，直到不再触发。
///      <br/>进阶时结算“额外伤害继承”：三张参与合体的牌里，凡伤害被养到高于模板（ModelDb 同 Id 规范卡）
///      的部分都会被累加，写进进阶后的那张牌（见 <see cref="ExcessDamageOf"/>），加成不白费。
/// </para>
/// <para>
///   2) 上限判断：若该火花组已满，循环 await <see cref="CardSelectCmd.FromSimpleGrid"/> 让玩家从
///      已 attach 的火花里选一张“引爆”——触发 <see cref="IFireFlower.OnBurnt"/> 并移除它（腾出位置），
///      直到有空位。顺序上把进阶放在上限之前：若本就要合体，合体自己就会腾位置，不会白白烧掉一张。
/// </para>
/// <para>
///   3) 实际 attach：走 <see cref="ResistanceSystem.GiveSpark"/>，让 SparkAdded 事件（计数圆闪烁 +
///      生物身上的 Attach 特效）照常播放；成功后通知火花自身 <see cref="IFireFlower.OnAttached"/>。
/// </para>
///
/// 调用约定：<paramref name="card"/> 传“要 attach 成火花的卡”，可以是规范卡（canonical）或任何可变副本。
/// 传 canonical 时本命令会自动把它物化为“该玩家”的一颗全新可变副本（否则同一张卡无法在同一生物上叠加
/// 同名火花，见 <see cref="ResistanceSystem.GiveSpark"/> 的去重逻辑）。正常玩法示例（某张牌打出后种火花）：
/// <code>
///     // canonical 火花卡，例如 ModelDb.Card&lt;EmberSeed&gt;()
///     await ReedAttachCmd.Attach(choiceContext, sparkCanonical, cardPlay.Target);
/// </code>
/// 火花归属玩家 = 火花卡的 Owner（多人时是谁的就是谁的组）；取不到时退回出牌上下文 / 本地玩家。
/// </summary>
public static class ReedAttachCmd
{
    /// <summary>触发进阶所需的目标上已有的同名（同 ModelId）火花张数。</summary>
    private const int FuseSameIdRequired = 2;

    /// <summary>进阶循环的兜底上限：正常最多连跳几级就停，防逻辑缺陷死循环。</summary>
    private const int FuseLoopGuard = 64;

    /// <summary>引爆选择屏的标题文案（key 见 Reed/localization/zhs/static_hover_tips.json）。</summary>
    private static LocString BurstPrompt => new LocString("static_hover_tips", "REED-SPARK.burst-select.title");

    /// <summary>执行一次完整的“给目标生物 attach 一张火花牌”流程（进阶判断 → 上限判断 → 实际 attach）。</summary>
    public static async Task Attach(PlayerChoiceContext? choiceContext, CardModel card, Creature target)
    {
        if (card == null || target == null || target.CurrentHp <= 0)
        {
            return; // 无卡 / 无目标 / 目标已倒下：不 attach
        }

        Player? owner = ResolveSparkOwner(choiceContext, card, target);
        if (owner == null)
        {
            GD.PushWarning("[ReedAttachCmd] 无法判定火花牌的归属玩家，attach 被跳过。");
            return;
        }

        // “即将 attach 的卡” → 一颗可用火花实例（canonical 自动物化为该玩家的可变副本）。
        CardModel? toAttach = MaterializeSpark(card, owner, target);
        if (toAttach == null)
        {
            return; // 无法实例化（原因已在 MaterializeSpark 内提示）
        }

        // ============ 1) 进阶判断 ============
        for (int guard = 0; guard < FuseLoopGuard; guard++)
        {
            if (toAttach is not IFireFlower fire)
            {
                break; // 仅对 IFireFlower 卡生效
            }
            CardModel? upperTemplate = fire.GetUpperCard();
            if (upperTemplate == null)
            {
                break; // 这张火花没有进阶形态
            }

            // 目标上已有的、与即将 attach 卡同 ModelId 的火花（快照，避免边删边枚举）。
            List<CardModel> sameId = ResistanceSystem.SparksOf(target, owner)
                .Where(s => s.Id == toAttach.Id).ToList();
            if (sameId.Count < FuseSameIdRequired)
            {
                break;
            }

            // 结算“额外伤害继承”：参与本次合体的三张牌（目标上已有的 N 张 + 即将 attach 的这张）里，
            // 凡是伤害 BaseValue 高于其模板（ModelDb 里同 Id 的规范卡）的，把高出的部分全部累加起来，
            // 一并加到进阶后的那张牌上（谁被 ChaseFirePower 之类养出来的伤害都不会白费）。
            decimal carried = ExcessDamageOf(toAttach);
            foreach (CardModel existing in sameId)
            {
                carried += ExcessDamageOf(existing);
            }

            // 把这 N 张已有火花取下（触发各自的 OnRemoved + RemoveSpark → 卡行消散动画）。
            // “即将 attach”的那一张还从未挂上去，无需 RemoveSpark，直接随本次不 attach 而作废。
            foreach (CardModel existing in sameId)
            {
                if (existing is IFireFlower removed)
                {
                    await removed.OnRemoved(choiceContext!, target);
                }
                ResistanceSystem.RemoveSpark(target, owner, existing);
            }

            // 【改为】attach 一张进阶后的卡：把上级卡物化为该玩家的一颗全新火花副本，进入下一轮判断
            //（若目标上已有 2 张上级卡，可继续链式进阶）。
            CardModel? upper = MaterializeSpark(upperTemplate, owner, target);
            if (upper == null)
            {
                break; // 非战斗等极端情况无法实例化：终止进阶
            }
            AddDamage(upper, carried); // 继承来的额外伤害写进进阶卡（同样计入下一轮链式进阶的基准）
            toAttach = upper;
        }

        // ============ 2) 上限判断 ============
        // 火花已满：让玩家选一张已 attach 的火花引爆（OnBurnt）并移除，直到腾出空位。
        while (!ResistanceSystem.CanAddSpark(target, owner))
        {
            CardModel? victim = await PickOneToBurst(choiceContext, target, owner);
            if (victim == null)
            {
                GD.PushWarning("[ReedAttachCmd] 火花已满且无可引爆对象，本次 attach 取消。");
                return;
            }
            if (victim is IFireFlower flower)
            {
                await Burnt(choiceContext, victim, target);
            }
            ResistanceSystem.RemoveSpark(target, owner, victim); // 移除事件驱动卡行消散动画
        }

        // ============ 3) 实际 attach ============
        // GiveSpark 广播 SparkAdded（计数圆闪烁 + Attach 特效）；成功后再通知火花自身。
        if (ResistanceSystem.GiveSpark(target, owner, toAttach))
        {
            if (toAttach is IFireFlower attached)
            {
                await attached.OnAttached(choiceContext!, target);
            }
        }
    }

    public static async Task Burnt(PlayerChoiceContext? choiceContext, Creature target, decimal blv = 1m, int times = 1, bool triggerByBurning = false)
    {
        if(choiceContext == null)
        {
            choiceContext = new ThrowingPlayerChoiceContext();
        }
        foreach(Player owner in target.CombatState?.Players??[])
        {
            await Burnt(choiceContext, owner, target, blv, times, triggerByBurning);
        }
        foreach(AbstractModel model in target.CombatState?.IterateHookListeners() ?? [])
        {
            if(model is IFireFlowerSubscriber sub)
            {
                await sub.AfterAllBurnt(choiceContext, target, triggerByBurning);
            }
        }
    }

    public static async Task Burnt(PlayerChoiceContext? choiceContext, Player owner, Creature target, decimal blv = 1m, int times = 1, bool triggerByBurning = false)
    {
        if(choiceContext == null)
        {
            choiceContext = new ThrowingPlayerChoiceContext();
        }
        foreach(CardModel card in ResistanceSystem.SparksOf(target, owner))
        {
            await Burnt(choiceContext, card, target, blv, times, triggerByBurning);
        }
    }

    public static async Task Burnt(PlayerChoiceContext? choiceContext, CardModel card, Creature target, decimal blv = 1m, int times = 1, bool triggerByBurning = false)
    {
        if(choiceContext == null)
        {
            choiceContext = new ThrowingPlayerChoiceContext();
        }
        if(card is IFireFlower flower)
        {
            ReedAttachVfx.Play(target, card);
            FireBurnt burnt = new FireBurnt(target, blv, times);
            List<IFireFlowerSubscriber> modifiers = new List<IFireFlowerSubscriber>();
            foreach(AbstractModel model in target.CombatState?.IterateHookListeners() ?? [])
            {
                if(model is IFireFlowerSubscriber sub)
                {
                    FireBurnt tmpBurnt = sub.ModifyBurnt(card, burnt, triggerByBurning);
                    if(tmpBurnt != burnt)
                    {
                        modifiers.Add(sub);
                    }
                    burnt = tmpBurnt;
                }
            }
            foreach(IFireFlowerSubscriber sub in modifiers)
            {
                await sub.AfterModifyBurnt(choiceContext, card, burnt, triggerByBurning);
            }
            await flower.OnBurnt(choiceContext, burnt); // 引爆：火花自带的“燃烧/迸发”效果
            foreach(AbstractModel model in target.CombatState?.IterateHookListeners() ?? [])
            {
                if(model is IFireFlowerSubscriber sub)
                {
                    await sub.OnBurnt(choiceContext, card, burnt);
                }
            }
        }
        else
        {
            
        }
    }

    /// <summary>
    /// 直接把目标生物（<paramref name="owner"/> 名下）身上的【一张】火花牌替换成它的进阶形态
    /// （<see cref="IFireFlower.GetUpperCard"/>），不要求“攒够 3 张”才合体。典型用途：某些效果让单张
    /// 火花原地“开花”。额外伤害同样会继承（见 <see cref="ExcessDamageOf"/>）。
    /// 流程：取上级卡 → 物化副本 → 继承伤害 → 取下旧卡（OnRemoved + RemoveSpark）→ 挂上新卡
    /// （GiveSpark + OnAttached，计数圆闪烁 / Attach 特效照常播放）。先取后挂，因此不会撞上限。
    /// </summary>
    /// <returns>进阶后的那张新火花；不可进阶/未找到该火花时返回 null。</returns>
    public static async Task<CardModel?> UpgradeSpark(PlayerChoiceContext? choiceContext, Creature target, CardModel spark)
    {
        if (spark == null || target == null || target.CurrentHp <= 0)
        {
            return null;
        }

        Player? owner = ResolveSparkOwner(choiceContext, spark, target);
        if (owner == null)
        {
            GD.PushWarning("[ReedAttachCmd] 无法判定火花牌的归属玩家，进阶被跳过。");
            return null;
        }

        // 必须确实是挂在该目标身上、且属于该玩家的那颗火花：优先引用相等，退而求其次按 Id（调用方传了规范卡时）。
        CardModel? attached = ResistanceSystem.SparksOf(target, owner).FirstOrDefault(
            s => ReferenceEquals(s, spark) || (!spark.IsMutable && s.Id == spark.Id));
        if (attached == null)
        {
            GD.PushWarning($"[ReedAttachCmd] 目标身上没有找到这张火花 {spark.Id}，进阶被跳过。");
            return null;
        }
        if (attached is not IFireFlower fire)
        {
            return null; // 这张火花没有进阶形态
        }
        CardModel? upperTemplate = fire.GetUpperCard();
        if (upperTemplate == null)
        {
            return null;
        }

        CardModel? upper = MaterializeSpark(upperTemplate, owner, target);
        if (upper == null)
        {
            return null;
        }
        AddDamage(upper, ExcessDamageOf(attached)); // 继承旧卡被养出来的额外伤害

        // 取下旧卡（OnRemoved + RemoveSpark → 卡行消散动画），再挂上新卡（GiveSpark → 计数圆闪烁 + Attach 特效）。
        await fire.OnRemoved(choiceContext!, target);
        ResistanceSystem.RemoveSpark(target, owner, attached);
        if (!ResistanceSystem.GiveSpark(target, owner, upper))
        {
            return null;
        }
        if (upper is IFireFlower upgraded)
        {
            await upgraded.OnAttached(choiceContext!, target);
        }
        return upper;
    }

    // ============ 额外伤害继承 ============

    /// <summary>
    /// 火花牌可能承载“伤害”的动态变量名，优先级与 <c>ChaseFirePower.BeforeBurn</c> 一致：
    /// 结算伤害的卡优先用 CalculatedDamage，其次是常规 Damage，最后是 OstyDamage。
    /// </summary>
    private static readonly string[] DamageVarKeys = { "CalculatedDamage", "Damage", "OstyDamage" };

    /// <summary>取出这张卡“实际用来结算伤害”的那个动态变量；三种都没有则返回 null。</summary>
    private static DynamicVar? FindDamageVar(CardModel card)
    {
        foreach (string key in DamageVarKeys)
        {
            if (card.DynamicVars.TryGetValue(key, out DynamicVar? v) && v != null)
            {
                return v;
            }
        }
        return null;
    }

    /// <summary>
    /// 这张火花比其“模板伤害”高出来的部分。模板 = <c>ModelDb</c> 里同 Id 的规范卡（即
    /// <c>ModelDb.Card&lt;T&gt;()</c> 那张）的伤害 <c>BaseValue</c>；不高于模板时返回 0。
    /// 这样 ChaseFirePower 等效果的加成在合体/进阶时不会丢失。
    /// </summary>
    private static decimal ExcessDamageOf(CardModel card)
    {
        DynamicVar? own = FindDamageVar(card);
        if (own == null)
        {
            return 0m;
        }
        CardModel? template = ModelDb.GetByIdOrNull<CardModel>(card.Id);
        DynamicVar? baseVar = template == null ? null : FindDamageVar(template);
        decimal delta = own.BaseValue - (baseVar?.BaseValue ?? 0m);
        return delta > 0m ? delta : 0m;
    }

    /// <summary>把继承来的额外伤害加到目标卡上（加到该卡实际使用的那个伤害变量）。</summary>
    private static bool AddDamage(CardModel card, decimal extra)
    {
        if (extra <= 0m)
        {
            return false;
        }
        DynamicVar? v = FindDamageVar(card);
        if (v == null)
        {
            return false;
        }
        v.BaseValue += extra;
        return true;
    }

    /// <summary>
    /// 判定这组火花挂在哪个玩家名下。优先级：火花卡的 Owner → 出牌上下文 OwnerId → 目标所在战斗的本地玩家。
    /// </summary>
    private static Player? ResolveSparkOwner(PlayerChoiceContext? choiceContext, CardModel card, Creature target)
    {
        // 1) 火花卡已属于某玩家（正常玩法里调用方多以 CreateCard(canonical, owner) 造好副本再传入）。
        if (card.IsMutable && card.Owner is { } byCard)
        {
            return byCard;
        }

        ICombatState? combat = target.CombatState;

        // 2) 出牌上下文中的行动者（多人时是谁打出的那张牌，火花就归谁）。
        if (choiceContext?.OwnerId is { } pid && combat != null && combat.GetPlayer(pid) is { } byContext)
        {
            return byContext;
        }

        // 3) 单机回退：目标所在战斗的本地玩家。
        if (combat != null && LocalContext.GetMe(combat) is { } me)
        {
            return me;
        }

        return null;
    }

    /// <summary>
    /// 把“要 attach 的卡”变成一颗可用的火花实例：
    /// canonical（规范卡）→ 物化为该玩家的一颗全新可变副本（同一张 canonical 无法在同一生物上叠同名火花，
    /// 见 <see cref="ResistanceSystem.GiveSpark"/> 的 list.Contains 去重）；已是可变卡则原样使用，
    /// 若它还没绑定 owner 则顺手登记进本场战斗。
    /// </summary>
    private static CardModel? MaterializeSpark(CardModel template, Player owner, Creature target)
    {
        if (template.IsMutable)
        {
            if (template.Owner is null && owner.Creature.CombatState != null)
            {
                // 一颗没主的可变副本：登记进本场战斗并挂到 owner 名下，保证后续事件/预览可用。
                owner.Creature.CombatState.AddCard(template, owner);
            }
            return template;
        }

        // canonical：需要可变副本才能叠加/进阶，物化必须发生在有战斗上下文时（火花本就只在战斗里出现）。
        ICombatState? combat = owner.Creature.CombatState ?? target.CombatState;
        if (combat == null)
        {
            GD.PushWarning($"[ReedAttachCmd] 非战斗上下文无法实例化火花 {template.Id} 的可变副本，attach 跳过。");
            return null;
        }
        return combat.CreateCard(template, owner);
    }

    /// <summary>让玩家从目标上已 attach 的火花里选一张引爆（只选 1 张）。选择屏无法弹出时取最后一张作默认。</summary>
    public static async Task<CardModel?> PickOneToBurst(PlayerChoiceContext? choiceContext, Creature target, Player owner)
    {
        IReadOnlyList<CardModel> sparks = ResistanceSystem.SparksOf(target, owner);
        if (sparks.Count == 0)
        {
            return null; // 已满却没有任何火花：理论上到不了这里
        }
        if (choiceContext == null)
        {
            // 非交互上下文（测试/结算等）弹不出选择屏：取最后一张（最新）作默认引爆对象，让流程仍可走通。
            return sparks[sparks.Count - 1];
        }

        // 只选 1 张；RequireManualConfirmation=false → 若恰好只有 1 张可选，FromSimpleGrid 会自动全选、免弹屏。
        CardSelectorPrefs prefs = new CardSelectorPrefs(BurstPrompt, 1);
        IEnumerable<CardModel> chosen = await CardSelectCmd.FromSimpleGrid(choiceContext, sparks, owner, prefs);
        return chosen.FirstOrDefault();
    }
}
