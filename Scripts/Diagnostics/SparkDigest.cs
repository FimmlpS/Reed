using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Diagnostics;

/// <summary>
/// 【诊断 · 用于定位多人状态首次分叉的动作】每次战斗内动作结束、原版生成校验和时，顺手把 mod 自己的
/// 本地状态（火花表 / 抗性 / 附着火花总数）打一行 VeryDebug 日志。
///
/// <para><b>为什么需要</b>：火花表与抗性都是 mod 本地状态，完全不进原版校验和 ——
/// <c>NetFullCombatState.FromRun</c> 只盖 HP/Block/能力、5 个牌堆、遗物、药水、球、RNG、金币。
/// 所以第一次分叉时不会有任何报错，只能等它后来以 HP 差或随机数流错位的形式炸出来
/// （典型是某次 <c>State divergence detected</c> 挂在 MeltCalamity 这类"攻击次数吃火花表"的牌上，
/// 那已经是第二次之后的事了）。</para>
///
/// <para><b>用法</b>：宿主与客户端各跑一遍同一场战斗，把两边的 <c>[Reed/Digest]</c> 行按顺序抓出来逐行对比。
/// 行首就是原版那份 context（<c>finished action execution PlayCardAction card: ...</c>），
/// <b>第一条内容不同的行</b>对应的动作就是第一次把两端弄岔的写入点；在它前一行里看是哪只生物、哪个
/// owner 的火花组先出现差异，就能直接定位到那条写入路径。</para>
///
/// <para><b>只读</b>：不写任何状态、不加 Harmony、不发网络消息；<see cref="Enabled"/> 置 false 即关闭。
/// 整个 <c>Scripts/Diagnostics/</c> 目录删掉（外加 <see cref="ResistanceSystem.HandleCombatSetUp"/> 里那一行
/// 调用）即可完全移除。</para>
/// </summary>
internal static class SparkDigest
{
    /// <summary>总开关。改 false 后不再挂任何回调（用 static readonly 而非 const，避免 const 折叠出 CS0162 死代码警告）。</summary>
    private static readonly bool Enabled = true;

    /// <summary>已挂接的校验和追踪器（run 内唯一；换 run 会换实例，因此用引用比较去重）。</summary>
    private static ChecksumTracker? _hooked;

    /// <summary>战斗开始时挂接一次校验和回调。幂等；由 <see cref="ResistanceSystem.HandleCombatSetUp"/> 调用。</summary>
    public static void HookForCombat()
    {
        if (!Enabled)
        {
            return;
        }
        // 校验和只在战斗中生成（RunManager.SendPostActionChecksum 里 IsInProgress 判断），
        // 所以战斗开始时挂一次就覆盖了本场所有会被比对的动作。
        ChecksumTracker? tracker = RunManager.Instance.ChecksumTracker;
        if (tracker == null || ReferenceEquals(_hooked, tracker))
        {
            return;
        }
        _hooked = tracker;
        tracker.ChecksumGenerated += (_, context, _) => WriteDigest(context);
    }

    private static void WriteDigest(string context)
    {
        CombatState? combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat == null)
        {
            return;
        }

        StringBuilder sb = new(320);
        sb.Append("[Reed/Digest] ").Append(context);
        sb.Append(" || attached=").Append(ResistanceSystem.AttachedSparkCount);

        foreach (Creature c in combat.Creatures)
        {
            sb.Append(" || ").Append(c.Name).Append('#').Append(c.CombatId?.ToString() ?? "-");
            sb.Append(" hp=").Append(c.CurrentHp).Append('/').Append(c.MaxHp);

            ResistanceData? d = ResistanceSystem.PeekData(c);
            if (d == null)
            {
                sb.Append(" noData"); // 这场还没为它建过数据（没挂过火花、也没建过抗性条）
                continue;
            }

            // 抗性本身也不进校验和，一并打出来：能区分"只有火花表岔了"和"整块本地状态都岔了"。
            sb.Append(" R=").Append(d.Current).Append('/').Append(d.Max);
            if (d.Burning)
            {
                sb.Append(" burning");
            }
            if (d.Changed)
            {
                sb.Append(" changed");
            }

            // 按 owner NetId 升序：与 SparkHookListeners 的遍历顺序一致，两端可逐字符对比。
            foreach (ulong ownerId in d.Sparks.Keys.OrderBy(k => k))
            {
                sb.Append(" u").Append(ownerId).Append("(max ").Append(StoredSparkMax(d, ownerId)).Append(")=[");
                List<CardModel> list = d.Sparks[ownerId];
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(Describe(list[i]));
                }
                sb.Append(']');
            }
        }

        Log.VeryDebug(sb.ToString());
    }

    /// <summary>一张火花的可读摘要：<c>模型名@伤害值</c>，末尾 <c>^</c> 表示它是进阶形态（MeltCalamity 按 2 张计）。</summary>
    private static string Describe(CardModel? card)
    {
        if (card == null)
        {
            return "<null>"; // 理论上不会出现；列表里真混进 null 时也能一眼看出来
        }
        bool topTier = card is IFireFlower flower && flower.IsAdvanced;
        return $"{card.Id.Entry}@{ResistanceSystem.SparkDamageOf(card)}{(topTier ? "^" : "")}";
    }

    /// <summary>读「本生物 × 本 owner」存下来的火花上限（不走 hook，避免诊断代码碰任何 mod 逻辑）。</summary>
    private static int StoredSparkMax(ResistanceData d, ulong ownerId)
        => d.SparkMaxes.TryGetValue(ownerId, out int max) ? max : ResistanceSystem.DefaultSparkMax;
}
