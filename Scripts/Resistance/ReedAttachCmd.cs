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

    public static async Task Burnt(PlayerChoiceContext? choiceContext, Creature target)
    {
        foreach(Player owner in target.CombatState?.Players??[])
        {
            await Burnt(choiceContext, owner, target);
        }
    }

    public static async Task Burnt(PlayerChoiceContext? choiceContext, Player owner, Creature target)
    {
        foreach(CardModel card in ResistanceSystem.SparksOf(target, owner))
        {
            await Burnt(choiceContext, card, target);
        }
        
    }

    public static async Task Burnt(PlayerChoiceContext? choiceContext, CardModel card, Creature target)
    {
        if(choiceContext == null)
        {
            choiceContext = new ThrowingPlayerChoiceContext();
        }
        if(card is IFireFlower flower)
        {
            ReedAttachVfx.Play(target, card);
            FireBurnt burnt = new FireBurnt(target);
            List<IFireFlowerSubscriber> modifiers = new List<IFireFlowerSubscriber>();
            foreach(AbstractModel model in card.CombatState?.IterateHookListeners() ?? [])
            {
                if(model is IFireFlowerSubscriber sub)
                {
                    FireBurnt tmpBurnt = sub.ModifyBurnt(card, burnt);
                    if(tmpBurnt != burnt)
                    {
                        modifiers.Add(sub);
                    }
                    burnt = tmpBurnt;
                }
            }
            foreach(IFireFlowerSubscriber sub in modifiers)
            {
                await sub.AfterModifyBurnt(choiceContext, card, burnt);
            }
            await flower.OnBurnt(choiceContext, burnt); // 引爆：火花自带的“燃烧/迸发”效果
            foreach(AbstractModel model in card.CombatState?.IterateHookListeners() ?? [])
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
    private static async Task<CardModel?> PickOneToBurst(PlayerChoiceContext? choiceContext, Creature target, Player owner)
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
