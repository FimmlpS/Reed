using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Reed.Scripts.Cards.Attack;
using Reed.Scripts.Diagnostics;
using ReedCharacter = Reed.Scripts.Characters.Reed;

namespace Reed.Scripts.Resistance;

/// <summary>
/// 抗性数值层 + 显示规则 + 状态机。
/// 每生物一份数据由 <see cref="SpireField{TKey,TVal}"/>（ConditionalWeakTable 承载）挂在 Creature 上，
/// 会随生物对象一同回收；每场战斗 Creature 都是新对象，因此抗性天然每场满 4、与血条同一时刻初始化。
///
/// 只写了两个“状态切换”函数（其余为通用数值/读取 API）：
///   <see cref="EnterBurning"/> / <see cref="ExitBurning"/>
/// 何时调用它们完全由你决定（例如抗性被打到 0 时进入、回满时退出）。
///
/// <para>这两个函数是燃烧状态的<b>唯一切换点</b>，因此也是「进入/离开燃烧」事件的广播点：
/// 切换成功后会把事件派发给全场所有 <see cref="IFireFlowerSubscriber"/>
/// （<see cref="IFireFlowerSubscriber.AfterEnterBurning"/> / <see cref="IFireFlowerSubscriber.AfterExitBurning"/>）。
/// 事件只在真正发生切换时触发，重复调用（已在燃烧中再进入）不会重复广播。</para>
/// </summary>
public static class ResistanceSystem
{
    private static readonly SpireField<Creature, ResistanceData> _data = new(() => null);

    /// <summary>抗性条根节点 → 条，供 layout 钩子反查。</summary>
    private static readonly ConditionalWeakTable<Control, ResistanceBarVisual> _visualsByRoot = new();

    private static bool _combatHooked;

    /// <summary>本场战斗玩家方是否存在 Reed（规则7 条件一，成立则全员显示）。</summary>
    public static bool ForceShowAll { get; private set; }

    // ============ 战斗事件 / 初始化 ============

    /// <summary>
    /// 惰性挂接战斗事件。第一次有生物的血条创建（SetCreature）时即触发，早于 CombatSetUp。
    /// </summary>
    private static void EnsureCombatHooked()
    {
        if (_combatHooked)
        {
            return;
        }
        CombatManager? cm = CombatManager.Instance;
        if (cm == null)
        {
            return;
        }
        cm.CombatSetUp += HandleCombatSetUp;
        cm.CombatEnded += _ => OnCombatEnded();
        cm.TurnStarted += HandleTurnStarted;
        _combatHooked = true;
    }

    /// <summary>已经跑过 <see cref="OnCombatSetUp"/> 的那场战斗（防重复：CombatSetUp 事件与 Harmony 补丁两条路都可能到）。</summary>
    private static object? _setupHandledFor;

    /// <summary>本场战斗的 state。战斗结束事件只给房间、不给 state，清空宠物火花时要靠它找回那场战斗里的生物。</summary>
    private static CombatState? _lastCombat;

    /// <summary>
    /// 战斗开始的统一入口。由 <c>CombatManager.SetUpCombat</c> 的 Harmony 补丁调用
    /// （见 Scripts/Patches/CombatLifecyclePatch.cs），同一场战斗只执行一次。
    ///
    /// <para>以前只有「第一次给某生物建血条（NHealthBar.SetCreature）时惰性订阅 CombatSetUp」这一条路 ——
    /// 那依赖 UI 建条与本事件谁先谁后，而那个顺序在多人里各客户端并不保证一致：某个客户端一旦错过
    /// CombatSetUp，它这场的火花表/监听者计数就不会被重置，两个客户端从此各算各的（结算时才炸成
    /// “State divergence detected”）。Harmony 补丁在宿主与客户端上以相同顺序执行，与 UI 无关。</para>
    /// </summary>
    public static void HandleCombatSetUp(CombatState state)
    {
        // 诊断探针（幂等）：把 mod 本地状态（火花表/抗性）跟着校验和一起打日志，
        // 用来定位多人下第一次分叉的那个动作。详见 Scripts/Diagnostics/SparkDigest.cs。
        SparkDigest.HookForCombat();

        if (state == null || ReferenceEquals(_setupHandledFor, state))
        {
            return;
        }
        _setupHandledFor = state;
        _lastCombat = state;
        _sparkRemovalsThisTurn.Clear();
        OnCombatSetUp(state);
    }

    private static void OnCombatSetUp(CombatState state)
    {
        _attachedSparkCount = 0; // 新一场战斗：上一场的火花全没了，监听者计数据此清零
        bool reedInBattle = state.Players.Any(p => p.Character is ReedCharacter);
        ForceShowAll = reedInBattle;

        // 跨战斗清理：绝大多数生物每场战斗都是新 Creature（数据天然干净），但**玩家的 Creature 整个 run
        // 都是同一个对象**（Player.Creature 在 Player 构造里建一次），挂在它身上的火花牌/抗性值/燃烧状态/
        // 上限都会残留到下一场 —— 上一场战斗附给自己的火花会带进新战斗。这里逐只重置。
        foreach (Creature creature in state.Creatures)
        {
            ResetForNewCombat(creature, state);
        }

        if (!reedInBattle)
        {
            return;
        }
        // 战斗开始、玩家方有 Reed → 给当前在场所有生物的抗性条启用显示。
        foreach (Creature creature in state.Creatures)
        {
            GetData(creature).Bar?.SetShown(true);
        }
        //TrySeedDemoSpark(state);
    }

    /// <summary>
    /// 新战斗开始：清掉某生物身上"跨战斗残留"的抗性状态，并让 UI 跟上。
    ///
    /// <para>生物大多是每场新建的，此函数对它们等价于 no-op（还没建过数据，直接返回）；
    /// 真正会命中的是<b>玩家</b>（以及同样整个 run 复用 Creature 的宠物）。</para>
    ///
    /// <para>清的是一整套"每场战斗都该归零"的东西：火花牌（<see cref="ResistanceData.Sparks"/>）、
    /// 自定义火花上限（<see cref="ResistanceData.SparkMaxes"/>）、抗性值（回满）、燃烧状态、变化标记，
    /// 上限按本场房间类型重算（<see cref="ResistanceData.MaxForRoom"/>）。</para>
    /// </summary>
    private static void ResetForNewCombat(Creature c, ICombatState? combat)
    {
        ResistanceData? d = _data[c];
        if (d == null)
        {
            return; // 这场战斗才第一次出现（或还没建条）：没有历史可清
        }

        // 火花牌：整组丢掉（旧战斗的卡对象本身也随旧战斗一起作废，不通知 OnRemoved —— 那会引起旧战斗的连锁结算）。
        int removed = 0;
        foreach (List<CardModel> group in d.Sparks.Values)
        {
            removed += group.Count;
        }
        bool hadSparks = removed > 0;
        if (hadSparks)
        {
            d.Sparks.Clear();
            _attachedSparkCount = Math.Max(0, _attachedSparkCount - removed);
        }
        d.SparkMaxes.Clear();

        d.Max = ResistanceData.MaxForRoom(combat, c);
        d.Current = d.Max;
        d.Burning = false;
        d.Changed = false;

        // 上一场的抗性条/火焰驱动器：节点通常已随旧战斗房间一起释放 → 作废引用，等本场新血条建条时重建。
        //（顺带避免下面 SyncVisuals / SetShown 去碰已经释放的节点。）
        if (d.Bar != null && !GodotObject.IsInstanceValid(d.Bar.Root))
        {
            d.Fire = null;
            d.Bar = null;
        }
        d.Fire?.Stop();

        if (hadSparks)
        {
            SparksChanged?.Invoke(c); // 已建好的本场火花 UI：把卡行与计数圆清空
        }
        SyncVisuals(c, d); // 若本场条已建好：立刻显示满抗性 + 非燃烧
    }

    /// <summary>
    /// 玩家回合开始：重置「本回合移除过火花牌」的记录（驱策死亡等卡的条件判断按玩家各自计）。
    /// </summary>
    private static void HandleTurnStarted(CombatState state)
    {
        if (state == null || state.CurrentSide != CombatSide.Player)
        {
            return;
        }
        _sparkRemovalsThisTurn.Clear();
    }

    private static void OnCombatEnded()
    {
        // 宠物不能携带火花：正常附着已被 CanHostSpark 挡住，这里做兜底 —— 旧状态/复活路径残留的火花在战斗结束时
        // 一律清掉，免得宠物被带进下一场。此刻 UI 节点正在回收，只清数据，不广播 SparksChanged、不碰 Bar。
        ClearPetSparks(_lastCombat);
        _lastCombat = null;

        ForceShowAll = false;
        _sparkDemoSeeded = false;
        _attachedSparkCount = 0;
    }

    /// <summary>清空本场战斗里所有宠物身上的火花牌与火花上限。</summary>
    private static void ClearPetSparks(CombatState? combat)
    {
        if (combat == null)
        {
            return;
        }
        HashSet<Creature> seen = new();
        foreach (Player player in combat.Players)
        {
            foreach (Creature pet in player.PlayerCombatState?.Pets ?? [])
            {
                if (seen.Add(pet))
                {
                    ClearSparksOf(pet);
                }
            }
        }
        // 已死/已离场的宠物不在 Pets 列表里，补一遍战场快照（同一只只清一次）。
        foreach (Creature creature in combat.Creatures)
        {
            if (creature.IsPet && seen.Add(creature))
            {
                ClearSparksOf(creature);
            }
        }
    }

    /// <summary>只清数据、不发事件（见 <see cref="ClearPetSparks"/>）。</summary>
    private static void ClearSparksOf(Creature creature)
    {
        ResistanceData? d = _data[creature];
        if (d == null)
        {
            return;
        }
        d.Sparks.Clear();
        d.SparkMaxes.Clear();
    }

    // ============ 数据访问 ============

    public static ResistanceData GetData(Creature creature)
    {
        EnsureCombatHooked();
        ResistanceData? d = _data[creature];
        if (d != null)
        {
            return d;
        }
        d = new ResistanceData(creature);
        _data[creature] = d;
        // 死亡/复活订阅绑在数据上，而不是绑在抗性条上：条会随 UI 重建（SL 恢复、死亡藏条），
        // 复活重置不该因为「此刻没有条」就失效。
        creature.Died += OnCreatureDied;
        creature.Revived += OnCreatureRevived;
        return d;
    }

    /// <summary>只读查询：与 <see cref="GetData"/> 的区别是<b>不创建</b>数据（没建过就返回 null）。
    /// 给诊断代码用，避免观察行为本身改变被观察的状态。</summary>
    internal static ResistanceData? PeekData(Creature creature) => _data[creature];

    /// <summary>
    /// 一张火花牌"携带的伤害"：按 CalculatedDamage → Damage → OstyDamage 的优先级取第一个存在的动态变量，
    /// 返回其 <see cref="DynamicVar.BaseValue"/>（即会被各种强化直接改写、且两端可能不一致的那个值）。
    /// 火花的爆发伤害就是按它算的（见各 Flowe 的 <c>OnBurnt</c>），<see cref="Powers.ChaseFirePower"/> 的加成也写进它。
    /// </summary>
    internal static decimal SparkDamageOf(CardModel card)
    {
        if (card.DynamicVars.ContainsKey("CalculatedDamage")) { return card.DynamicVars.CalculatedDamage.BaseValue; }
        if (card.DynamicVars.ContainsKey("Damage")) { return card.DynamicVars.Damage.BaseValue; }
        if (card.DynamicVars.ContainsKey("OstyDamage")) { return card.DynamicVars.OstyDamage.BaseValue; }
        return 0m;
    }

    public static int GetCurrent(Creature c) => GetData(c).Current;
    public static int GetMax(Creature c) => GetData(c).Max;
    public static bool IsBurning(Creature c) => GetData(c).Burning;
    public static bool IsFull(Creature c) => GetData(c).Current >= GetData(c).Max;
    public static bool HasChangedThisBattle(Creature c) => GetData(c).Changed;

    // ============ 火花牌（每敌人绑定若干张；按 owner=玩家 NetId 区分，每人独立一组） ============

    /// <summary>每个（生物 × owner）可绑定的火花牌上限。</summary>
    public const int DefaultSparkMax = 3;

    /// <summary>
    /// 这只生物能否承载火花牌：<b>玩家与怪物都可以，宠物不行</b>。
    /// <para>宠物同样带 <c>Monster</c>（<c>IsMonster</c> 为 true），只判 <c>IsMonster</c> 会把火花挂到玩家的宠物身上，
    /// 因此必须显式排除 <c>IsPet</c>。</para>
    /// </summary>
    public static bool CanHostSpark(Creature? c) => c != null && !c.IsPet && (c.IsMonster || c.IsPlayer);

    /// <summary>「敌人」判定（初始遗物的「所有敌人」就按这个）：怪物且不是宠物。</summary>
    public static bool IsEnemyMonster(Creature? c) => c != null && c.IsMonster && !c.IsPet;

    /// <summary>任何火花发生增删变化时广播（参数为该生物）；UI 驱动器据此重建。</summary>
    public static event Action<Creature>? SparksChanged;

    /// <summary>给某生物（owner 视角）成功塞入一张火花牌后广播（用于“添加”表现：计数圆闪烁 + Attach 特效）。</summary>
    public static event Action<Creature, Player, CardModel>? SparkAdded;

    /// <summary>从某生物（owner 视角）成功移除一张火花牌后广播（用于“移除”表现）。</summary>
    public static event Action<Creature, Player, CardModel>? SparkRemoved;

    /// <summary>
    /// 计数圆数字的自定义取值器（留好扩展接口）。默认为空 → 圆里显示“本地玩家的火花数量”。
    /// 将来想显示例如火花总伤害、牌堆数等，只需在此给函数即可（函数参数为生物）。
    /// </summary>
    public static Func<Creature, int>? SparkBadgeValueProvider;

    /// <summary>
    /// 火花牌 → 它此刻附着的生物（只在 <see cref="GiveSpark"/> 成功时写入）。
    /// 移除时不回写 null：火花一旦被取下就不在任何 SparkHookListeners 里，宿主是谁已无人过问；
    /// 若之后这张牌被挂到别的生物上，GiveSpark 会直接覆盖。表项随火花实例一起回收。
    /// </summary>
    private static readonly SpireField<CardModel, Creature> _sparkHost = new(() => null);

    /// <summary>当前附着在生物身上的火花牌总数。为 0 时 <see cref="SparkHookListeners"/> 直接空转（hook 派发热路径的廉价守卫）。</summary>
    private static int _attachedSparkCount;

    /// <summary>诊断只读暴露（见 Scripts/Diagnostics/SparkDigest.cs）：全场附着火花总数。</summary>
    internal static int AttachedSparkCount => _attachedSparkCount;

    /// <summary>读取某（生物 × owner）火花上限；没单独设过则返回默认 DefaultSparkMax。</summary>
    public static int GetSparkMax(Creature c, Player owner)
    {
        if (c == null || owner == null)
        {
            return DefaultSparkMax;
        }
        ResistanceData d = GetData(c);
        int extra = 0;
        foreach(AbstractModel model in c.CombatState?.IterateHookListeners() ?? [])
        {
            if(model is IFireFlowerSubscriber sub)
            {
                extra = sub.ModifySparkMax(c, owner, extra);
            }
        }
        return extra + (d.SparkMaxes.TryGetValue(owner.NetId, out int max) ? max : DefaultSparkMax);
    }

    /// <summary>
    /// 设置某（生物 × owner）火花上限（可动态扩容/收紧；小于 0 视为 0）。设为 0 等价于移除自定义值。
    /// 收紧上限不会清掉已存在的火花牌，只是拒绝继续添加。
    /// </summary>
    public static void SetSparkMax(Creature c, Player owner, int max)
    {
        if (c == null || owner == null)
        {
            return;
        }
        ResistanceData d = GetData(c);
        int v = Math.Max(0, max);
        if (v == 0)
        {
            d.SparkMaxes.Remove(owner.NetId);
        }
        else
        {
            d.SparkMaxes[owner.NetId] = v;
        }
    }

    /// <summary>给某生物的火花组（owner 视角）塞入一张火花牌；超出（该 owner 自定义/默认）上限或重复返回 false。</summary>
    public static bool GiveSpark(Creature c, Player owner, CardModel spark)
    {
        if (c == null || owner == null || spark == null)
        {
            return false;
        }
        ResistanceData d = GetData(c);
        if (!d.Sparks.TryGetValue(owner.NetId, out List<CardModel>? list))
        {
            list = new List<CardModel>();
            d.Sparks[owner.NetId] = list;
        }
        int max = GetSparkMax(c, owner);
        if (list.Count >= max || list.Contains(spark))
        {
            return false;
        }
        list.Add(spark);
        // 多人：火花牌不在任何牌堆里，引擎的 NetCombatCardDb 从没给它分配过网络 id，而所有要把一张卡
        // 发过网络的路径（队友悬停高亮 HoveredModelData、选择结果 PlayerChoiceResult.combatCards、
        // PlayCardAction…）都会走 NetCombatCard.FromModel → GetCardId，拿不到 id 就抛
        // InvalidOperationException —— 这就是「火花牌一爆/一选就炸」。两端按同样顺序补登记，id 才一致。
        RegisterSparkForNet(spark, owner);
        // 记下它此刻挂在谁身上：火花牌的「持续」效果要据此判断宿主（见 HostOf）。
        _sparkHost[spark] = c;
        _attachedSparkCount++;
        SparksChanged?.Invoke(c);
        SparkAdded?.Invoke(c, owner, spark);
        return true;
    }

    /// <summary>
    /// 把一张火花牌登记进本场战斗的网络卡表（<see cref="NetCombatCardDb"/>），让引擎的联网路径能够引用它
    /// —— 见 <see cref="GiveSpark"/> 里的说明。幂等；两端都在同样的位置调用，因此分配到的 id 一致。
    /// 只接受「可变且已有主」的卡：NetCombatCardDb 对无主卡只会报错并留空，随后取 id 还会抛异常。
    /// </summary>
    public static void RegisterSparkForNet(CardModel? spark, Player? owner)
    {
        if (spark == null || owner == null || !spark.IsMutable || spark.Owner == null)
        {
            return;
        }
        try
        {
            // 这是 NetCombatCardDb 唯一的公开「现在给这张卡分配 id」入口（名字带 ForTesting，但行为就是登记）。
            NetCombatCardDb.Instance.IdCardForTesting(spark);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Reed] 火花牌 {spark.Id} 登记网络 id 失败：{e.Message}");
        }
    }

    /// <summary>本回合移除过火花牌的次数（按施予者玩家计；每个玩家回合开始时清零）。</summary>
    private static readonly Dictionary<ulong, int> _sparkRemovalsThisTurn = new();

    /// <summary>
    /// 该玩家本回合是否移除过火花牌 —— 「驱策死亡」这类卡的金色高亮与额外效果条件。
    /// 记录点在 <see cref="RemoveSpark"/>：引爆后取下、进阶、腾位以及各张直接取火花的效果都会计到。
    /// </summary>
    public static bool HasRemovedSparkThisTurn(Player? owner)
        => owner != null && _sparkRemovalsThisTurn.TryGetValue(owner.NetId, out int n) && n > 0;

    /// <summary>移除某张火花牌；不存在返回 false。</summary>
    public static bool RemoveSpark(Creature c, Player owner, CardModel spark)
    {
        if (c == null || owner == null || spark == null)
        {
            return false;
        }
        ResistanceData d = GetData(c);
        if (!d.Sparks.TryGetValue(owner.NetId, out List<CardModel>? list) || !list.Remove(spark))
        {
            return false;
        }
        _attachedSparkCount = Math.Max(0, _attachedSparkCount - 1);
        _sparkRemovalsThisTurn[owner.NetId] = _sparkRemovalsThisTurn.GetValueOrDefault(owner.NetId) + 1;
        SparksChanged?.Invoke(c);
        SparkRemoved?.Invoke(c, owner, spark);
        return true;
    }

    /// <summary>清空某 owner 在该生物上的全部火花牌；原本就没有则返回 false（不发事件）。</summary>
    public static bool ClearSparks(Creature c, Player owner)
    {
        if (c == null || owner == null)
        {
            return false;
        }
        ResistanceData d = GetData(c);
        if (!d.Sparks.Remove(owner.NetId, out List<CardModel>? removed))
        {
            return false;
        }
        _attachedSparkCount = Math.Max(0, _attachedSparkCount - removed.Count);
        SparksChanged?.Invoke(c);
        return true;
    }

    /// <summary>读取某生物（owner 视角）的火花牌列表（只读引用）。</summary>
    public static IReadOnlyList<CardModel> SparksOf(Creature c, Player owner)
    {
        if (c == null || owner == null)
        {
            return Array.Empty<CardModel>();
        }
        ResistanceData d = GetData(c);
        return d.Sparks.TryGetValue(owner.NetId, out List<CardModel>? list) ? list : Array.Empty<CardModel>();
    }

    public static int CountSparks(Creature c, Player owner)
        => SparksOf(c, owner).Count;

    public static bool CanAddSpark(Creature c, Player owner)
        => CountSparks(c, owner) < GetSparkMax(c, owner);

    /// <summary>这张火花牌此刻附着的生物；从未 attach 过（或被取下后再没挂上）返回 null。</summary>
    public static Creature? HostOf(CardModel? spark) => spark == null ? null : _sparkHost[spark];

    /// <summary>
    /// 把「已 attach 的火花牌」注册为 combat hook 的监听者来源（mod 初始化时调用一次即可）。
    ///
    /// <para><b>为什么需要</b>：原版只把<b>牌堆里</b>的卡当作 hook 监听者（见 <c>CombatState.IterateHookListeners</c>），
    /// 而火花牌不在任何牌堆里 —— 不登记的话，火花自己的 <c>ModifyDamageMultiplicative</c> 之类的 hook
    /// 永远不会被调用，「持续」效果无从生效。登记之后它们才和力量/易伤一样参与伤害结算。</para>
    ///
    /// <para>走的是游戏给 mod 留的正规口子 <see cref="ModHelper.SubscribeForCombatStateHooks"/>：它被
    /// <c>CombatState.IterateHookListeners</c> 在枚举末尾原样接上，因此既不用 Harmony 补丁，也绕过了
    /// 那一层「模型必须属于本场战斗」的过滤。顺带覆盖 runState 那条路径 ——
    /// <c>RunState.IterateHookListeners(combatState)</c> 末尾同样会 yield 这个枚举。</para>
    /// </summary>
    public static void RegisterHookSubscription()
        => ModHelper.SubscribeForCombatStateHooks("Reed.Sparks", combatState => SparkHookListeners(combatState));

    /// <summary>本场战斗中所有「已 attach 的火花牌」（由 <see cref="RegisterHookSubscription"/> 接进 hook 派发）。</summary>
    public static IEnumerable<AbstractModel> SparkHookListeners(ICombatState? combat)
    {
        if (combat == null || _attachedSparkCount <= 0)
        {
            yield break;
        }
        foreach (Creature creature in combat.Creatures)
        {
            ResistanceData? d = _data[creature];
            if (d == null || d.Sparks.Count == 0)
            {
                continue;
            }
            // 按 owner.NetId 排序而不是按字典枚举顺序：hook 监听者的顺序（进而 ModifyBurnt 之类的链式结算
            // 顺序）必须与插入顺序无关，多人下两端才一定一致。
            foreach (ulong ownerId in d.Sparks.Keys.OrderBy(k => k))
            {
                foreach (CardModel spark in d.Sparks[ownerId])
                {
                    yield return spark;
                }
            }
        }
    }

    // ============ 火花牌 测试种子（给第一只怪物放若干张本地玩家的“打击”） ============

    private const bool EnableDemoSparkSeed = false;
    private static bool _sparkDemoSeeded;

    /// <summary>演示：等战斗界面就绪后再给第一张火花（留出建条/订阅时间）。</summary>
    private const float DemoFirstSparkDelay = 0.9f;
    /// <summary>演示：连续添加火花之间的间隔（方便逐张看清圆闪烁 + Attach 特效）。</summary>
    private const float DemoSparkGap = 0.7f;

    /// <summary>
    /// 每场战斗只执行一次：把本地玩家自己手里的若干张“打击”作为火花牌挂到第一只怪物上，
    /// 用来肉眼验证计数圆、悬停卡行、出现动画、以及 GiveSpark 的圆闪烁 + Attach 特效
    /// （SparksChanged → SparkAdded 事件时序）。规则落地后可整段删除（或置 EnableDemoSparkSeed=false）。
    /// </summary>
    private static void TrySeedDemoSpark(CombatState state)
    {
        if (!EnableDemoSparkSeed || _sparkDemoSeeded)
        {
            return;
        }
        _sparkDemoSeeded = true;
        Player? me = LocalContext.GetMe(state);
        Creature? enemy = state.Creatures.FirstOrDefault(IsEnemyMonster);
        if (me == null || enemy == null)
        {
            return;
        }
        AttemptSeedLater(me, enemy, 0);
    }

    /// <summary>延迟到起手牌发齐后再真正放火花（CombatSetUp 若早于发牌则逐帧重试，最多 ~30 帧）。</summary>
    private static void AttemptSeedLater(Player me, Creature enemy, int attempt)
    {
        if (attempt > 30 || me.PlayerCombatState == null)
        {
            return;
        }
        List<CardModel> strikes = [];
        for(int i = 0; i < 3; i++)
        {
            strikes.Add(me.Creature.CombatState?.CreateCard<Strike>(me));
        }
        if (strikes.Count > 0)
        {
            ScheduleDemoAdds(me, enemy, strikes);
            return;
        }
        Callable.From(() => AttemptSeedLater(me, enemy, attempt + 1)).CallDeferred();
    }

    /// <summary>
    /// 用 SceneTreeTimer 依次延时 GiveSpark，保证每次添加都发生在主线程、且抗性条已建好
    /// （SparkHudDriver 已订阅 SparkAdded）——否则看不到圆闪烁与 Attach 特效。
    /// </summary>
    private static void ScheduleDemoAdds(Player me, Creature enemy, IReadOnlyList<CardModel> strikes)
    {
        SceneTree? tree = NCombatRoom.Instance?.GetTree();
        if (tree == null)
        {
            return;
        }
        int i = 0;
        void Step()
        {
            if (i >= strikes.Count)
            {
                return;
            }
            GiveSpark(enemy, me, strikes[i]);
            i++;
            if (i < strikes.Count)
            {
                SceneTreeTimer timer = tree.CreateTimer(DemoSparkGap);
                timer.Timeout += Step;
            }
        }
        SceneTreeTimer starter = tree.CreateTimer(DemoFirstSparkDelay);
        starter.Timeout += Step;
    }

    // ============ 数值修改 API ============

    /// <summary>
    /// 把一次抗性变化的最终数值交给场上订阅者修正（<see cref="IFireFlowerSubscriber.ModifyResistanceChange"/>）。
    ///
    /// <para>调用点是 <see cref="ReedBurnCmd"/> 的结算入口（灼烧/恢复共用一个 Execute）：
    /// 修正后的值同时喂给 <see cref="IFireFlowerSubscriber.BeforeBurn"/>、<see cref="IFireFlowerSubscriber.AfterBurn"/>、
    /// 抗性结算与数值飘字 —— 「灼烧 1」在带修正时对三方而言都是 2。
    /// <see cref="Reduce"/> / <see cref="Restore"/> 本身<b>不再</b>修正，调用方传入的必须是最终值（防双重加成）。
    /// 单个订阅者抛异常只记警告，不中断结算。</para>
    /// </summary>
    internal static int ModifyChangeAmount(Creature c, int amount, bool isLoss)
    {
        if (c.CombatState == null)
        {
            return amount;
        }
        foreach (AbstractModel model in c.CombatState.IterateHookListeners())
        {
            if (model is not IFireFlowerSubscriber sub)
            {
                continue;
            }
            try
            {
                amount = sub.ModifyResistanceChange(c, amount, isLoss);
            }
            catch (Exception e)
            {
                GD.PushWarning($"[ResistanceSystem] 抗性变化修正失败（{model.Id}）：{e.Message}");
            }
        }
        return Math.Max(0, amount);
    }

    /// <summary>扣抗性（最低 0）。仅当非灼烧状态才会执行。
    /// <paramref name="amount"/> 必须是<b>已含订阅者修正</b>的最终值（见 <see cref="ModifyChangeAmount"/>），这里不再二次修正。</summary>
    public static async Task Reduce(Creature c, int amount, PlayerChoiceContext? choiceContext = null)
    {
        if (amount <= 0 || c.IsBurningResistance())
        {
            return;
        }
        if (c.CombatState == null)
        {
            return; // 已离开战斗：抗性不再结算
        }
        ResistanceData d = GetData(c);
        Node vfxContainer = c.GetVfxContainer();
        NDamageNumVfx nDamageNumVfx = NDamageNumVfx.Create(c, amount);
        if (nDamageNumVfx != null)
        {
            if (vfxContainer != null)
            {
                vfxContainer.AddChildSafely(nDamageNumVfx);
            }
            else
            {
                NRun.Instance?.GlobalUi.AddChildSafely(nDamageNumVfx);
            }
        }
        int nv = Math.Max(0, d.Current - amount);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
        if (d.Current == 0)
        {
            await EnterBurning(c, choiceContext);
        }
    }

    /// <summary>恢复抗性（最高回满 max）。按需调用 ExitBurning。
    /// <paramref name="amount"/> 必须是<b>已含订阅者修正</b>的最终值（见 <see cref="ModifyChangeAmount"/>），这里不再二次修正。</summary>
    public static async Task Restore(Creature c, int amount, PlayerChoiceContext? choiceContext = null)
    {
        if (amount <= 0)
        {
            return;
        }
        if (c.CombatState == null)
        {
            return; // 已离开战斗：抗性不再结算
        }
        ResistanceData d = GetData(c);
        Node vfxContainer = c.GetVfxContainer();
        NHealNumVfx nDamageNumVfx = NHealNumVfx.Create(c, amount);
        if (nDamageNumVfx != null)
        {
            if (vfxContainer != null)
            {
                vfxContainer.AddChildSafely(nDamageNumVfx);
            }
            else
            {
                NRun.Instance?.GlobalUi.AddChildSafely(nDamageNumVfx);
            }
        }
        int nv = Math.Min(d.Max, d.Current + amount);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
        if (d.Current == d.Max)
        {
            await ExitBurning(c, choiceContext);
        }
    }

    /// <summary>直接设定当前抗性（自动收敛到 0..max）。</summary>
    public static async Task SetCurrent(Creature c, int value, PlayerChoiceContext? choiceContext = null)
    {
        ResistanceData d = GetData(c);
        int nv = Math.Clamp(value, 0, d.Max);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
        await JudgeBurning(c, choiceContext);
    }

    public static async Task JudgeBurning(Creature c, PlayerChoiceContext? choiceContext = null)
    {
        ResistanceData d = GetData(c);
        if (d.Current == d.Max)
        {
            await ExitBurning(c, choiceContext);
        }
        else if(d.Current == 0)
        {
            await EnterBurning(c, choiceContext);
        }
    }

    /// <summary>修改抗性上限（最少为1点）</summary>
    public static async Task ChangeMax(Creature c, int offset, PlayerChoiceContext? choiceContext = null)
    {
        ResistanceData d = GetData(c);
        int finalMax = Math.Max(1,d.Max+offset);
        d.Max = finalMax;
        if (d.Current > d.Max)
        {
            d.Current = d.Max;
        }
        AfterMutation(c, d);
        await JudgeBurning(c, choiceContext);
    }

    // ============ 两个状态切换函数（规则2/3：蓝 ↔ 紫 + 燃烧特效）============

    /// <summary>
    /// 进入燃烧状态：条变紫，生物播放常驻火焰（原版特效循环铺放），
    /// 随后结算「进入燃烧」的火花爆发，最后广播 <see cref="IFireFlowerSubscriber.AfterEnterBurning"/>。
    /// 已在燃烧中时是 no-op（不会重复爆发、也不会重复广播）。
    /// </summary>
    public static async Task EnterBurning(Creature c, PlayerChoiceContext? choiceContext = null)
    {
        ResistanceData d = GetData(c);
        if (d.Burning)
        {
            return;
        }
        if (c.CombatState == null || c.IsDead)
        {
            return; // 已离场/已倒下：不进入燃烧（否则会去引爆一个已回收的生物）
        }
        choiceContext ??= new ThrowingPlayerChoiceContext();
        d.Burning = true;
        SyncVisuals(c, d);
        await ReedAttachCmd.Burnt(choiceContext,c,triggerByBurning:true);
        await NotifyBurningChanged(c, choiceContext, entered: true);
    }

    /// <summary>
    /// 退出燃烧状态：条回蓝，停止火焰，随后广播 <see cref="IFireFlowerSubscriber.AfterExitBurning"/>。
    /// 本就不在燃烧中时是 no-op（不会重复广播）。
    ///
    /// <para>注意：这是燃烧状态的<b>唯一出口</b>，且只有「抗性条回满」（Current == Max）才会走到这里
    /// （见 <see cref="Restore"/> / <see cref="JudgeBurning"/>）。新战斗重置
    /// （<see cref="ResetForNewCombat"/>）是直接写 <c>Burning = false</c> 的，<b>不</b>算离开燃烧、不广播。</para>
    /// </summary>
    public static async Task ExitBurning(Creature c, PlayerChoiceContext? choiceContext = null)
    {
        ResistanceData d = GetData(c);
        if (!d.Burning)
        {
            return;
        }
        choiceContext ??= new ThrowingPlayerChoiceContext();
        d.Burning = false;
        d.Fire?.Stop();
        SyncVisuals(c, d);
        await NotifyBurningChanged(c, choiceContext, entered: false);
    }

    /// <summary>
    /// 把「某生物进入/离开燃烧状态」派发给全场所有 <see cref="IFireFlowerSubscriber"/>。
    /// 与 <see cref="ReedAttachCmd.Burnt(Creature, decimal, int, bool)"/> 同一套约定：
    /// 事件发生时才用 IterateHookListeners 现查订阅者，没有注册/注销生命周期。
    /// 单个订阅者抛异常只记一条警告，不影响状态机本身与其他订阅者。
    /// </summary>
    private static async Task NotifyBurningChanged(Creature c, PlayerChoiceContext choiceContext, bool entered)
    {
        foreach (AbstractModel model in c.CombatState?.IterateHookListeners() ?? [])
        {
            if (model is not IFireFlowerSubscriber sub)
            {
                continue;
            }
            try
            {
                if (entered)
                {
                    await sub.AfterEnterBurning(choiceContext, c);
                }
                else
                {
                    await sub.AfterExitBurning(choiceContext, c);
                }
            }
            catch (Exception e)
            {
                GD.PushWarning($"[ResistanceSystem] 燃烧状态订阅者失败（{model.Id}）：{e.Message}");
            }
        }
    }

    // ============ 生物死亡 / 复活 ============

    private static void OnCreatureDied(Creature c)
    {
        ResistanceData? d = _data[c];
        if (d == null)
        {
            return;
        }
        d.Fire?.Stop();
        d.Fire = null; // 驱动器随死亡作废；复活时 SyncVisuals 会按 Burning 重建
        d.Bar?.SetShown(false);
    }

    /// <summary>
    /// 复活（统一入口 = <see cref="Creature.Revived"/>，在 <c>Creature.HealInternal</c> 里「死者被治活」时触发）：
    /// 抗性回到初始状态 —— <b>不燃烧、条回满</b>，火焰驱动器作废。
    ///
    /// <para>实验体、幻影这类「死亡后不离开战斗、下一回合原地重生」的敌人全部走
    /// <c>CreatureCmd.Heal</c> 复活，因此这里一处理就覆盖了二阶段/三阶段切换。</para>
    ///
    /// <para>与 <see cref="ExitBurning"/> 不同，这里<b>直接写状态、不广播</b>
    /// <see cref="IFireFlowerSubscriber.AfterExitBurning"/>：死亡期间被打空不是一次正常的「离开燃烧」，
    /// 不该触发花/能力的离开燃烧结算（同 <see cref="ResetForNewCombat"/> 的处理）。</para>
    /// </summary>
    private static void OnCreatureRevived(Creature c)
    {
        ResistanceData? d = _data[c];
        if (d == null)
        {
            return;
        }
        d.Burning = false;
        d.Current = d.Max;
        d.Fire?.Stop();
        d.Fire = null;
        SyncVisuals(c, d); // 条恢复显示（死亡时被 SetShown(false) 藏起来了）
    }

    // ============ UI 建条 / 布局钩子（由 Harmony Patch 调用）============

    /// <summary>
    /// NHealthBar.SetCreature 后置：只处理“生物状态条”里的血条（非多人顶部栏等）。
    /// 此时血条已 _Ready，HpLabel 等子节点可用；抗性数据同一时刻随血条初始化。
    /// </summary>
    public static void OnNHealthBarCreated(NHealthBar healthBar, Creature creature)
    {
        NCreatureStateDisplay? display = FindStateDisplayAncestor(healthBar);
        if (display == null)
        {
            return; // 不是生物头顶的状态条（如多人栏、调试条）
        }
        ResistanceData d = GetData(creature);
        ResistanceBarVisual? existing = d.Bar;
        if (existing != null)
        {
            // SL 后视觉节点被重建、而生物模型（乃至 ResistanceData）被沿用：
            // 旧条要么已随旧场景释放，要么挂在旧的 display 下，都不属于当前这条新血条 → 作废重建。
            bool stale = !GodotObject.IsInstanceValid(existing.Root)
                || FindStateDisplayAncestor(existing.Root) != display;
            if (!stale)
            {
                return; // 就是当前这条血条，已建过
            }
            d.Fire?.Stop();
            d.Fire = null;
            d.Bar = null;
            if (GodotObject.IsInstanceValid(existing.Root))
            {
                existing.Root.QueueFree(); // TreeExited 会自动解绑事件与 _visualsByRoot
            }
        }

        ResistanceBarVisual bar = ResistanceBarVisual.Create(display, healthBar, creature);
        d.Bar = bar;
        bar.Spark = SparkHudDriver.Attach(display, bar, creature);
        SyncVisuals(creature, d);
    }

    /// <summary>
    /// NCreatureStateDisplay.SetCreatureBounds 后置：把抗性条对齐到该生物血条下方。
    /// </summary>
    public static void OnStateDisplayBounds(NCreatureStateDisplay display)
    {
        RelayoutBarsOfDisplay(display);
    }

    /// <summary>
    /// NHealthBar 真正落定条宽的瞬间（SetHpBarContainerSizeWithOffsetsImmediately，含其内部
    /// 延迟一帧的那次调用）后置：血条几何一改，抗性条立刻跟随。
    /// 覆盖 SL 后恢复条宽 / 战斗中生物变大等任何改宽路径，不依赖是否恰好触发 SetCreatureBounds。
    /// </summary>
    public static void OnHealthBarGeometryChanged(NHealthBar healthBar)
    {
        NCreatureStateDisplay? display = FindStateDisplayAncestor(healthBar);
        if (display != null)
        {
            RelayoutBarsOfDisplay(display);
        }
    }

    private static void RelayoutBarsOfDisplay(NCreatureStateDisplay display)
    {
        foreach (Node child in display.GetChildren())
        {
            if (child is not Control c || c.Name != ResistanceBarVisual.NodeName)
            {
                continue;
            }
            if (_visualsByRoot.TryGetValue(c, out ResistanceBarVisual? bar) && bar is not null)
            {
                bar.LayoutUnderHpBar();
            }
        }
    }

    internal static void RegisterBar(ResistanceBarVisual bar, Creature creature)
    {
        _visualsByRoot.Add(bar.Root, bar);

        // 条离开场景（生物死亡/战斗结束移除节点）时解绑视觉映射；Died/Revived 由 GetData 统一订阅，不在这里动。
        bar.Root.TreeExited += () =>
        {
            bar.Spark?.Detach();
            _visualsByRoot.Remove(bar.Root);
        };
    }

    private static NCreatureStateDisplay? FindStateDisplayAncestor(Control start)
    {
        Node? n = start;
        while (n != null)
        {
            if (n is NCreatureStateDisplay sd)
            {
                return sd;
            }
            n = n.GetParent();
        }
        return null;
    }

    // ============ 内部 ============

    private static void AfterMutation(Creature c, ResistanceData d)
    {
        d.Changed = true; // 规则7 条件二：变化过 → 显示到战斗结束
        SyncVisuals(c, d);
    }

    private static void SyncVisuals(Creature c, ResistanceData d)
    {
        bool show = ForceShowAll || d.Changed || d.Burning;
        d.Bar?.UpdateAll(d, show);

        if (d.Burning)
        {
            if (d.Fire == null && d.Bar != null)
            {
                d.Fire = new BurningFireDriver(c, d.Bar.Root);
            }
            d.Fire?.Start();
        }
        else
        {
            d.Fire?.Stop();
        }
    }
}

/// <summary>
/// Creature 上的便捷扩展：便于卡/效果/事件直接写 creature.xxx()。
/// 别忘了在调用处 using Reed.Scripts.Resistance;
/// </summary>
public static class CreatureResistanceExtensions
{
    public static int GetResistance(this Creature c) => ResistanceSystem.GetCurrent(c);
    public static int GetResistanceMax(this Creature c) => ResistanceSystem.GetMax(c);
    public static bool IsBurningResistance(this Creature c) => ResistanceSystem.IsBurning(c);
    public static bool IsResistanceFull(this Creature c) => ResistanceSystem.IsFull(c);

    public static async Task DamageResistance(this Creature c, int amount, PlayerChoiceContext? choiceContext = null) => await ResistanceSystem.Reduce(c, amount, choiceContext);
    public static async Task RestoreResistance(this Creature c, int amount, PlayerChoiceContext? choiceContext = null) => await ResistanceSystem.Restore(c, amount, choiceContext);
    public static async Task SetResistance(this Creature c, int value, PlayerChoiceContext? choiceContext = null) => await ResistanceSystem.SetCurrent(c, value, choiceContext);
    public static async Task ChangeMaxResistance(this Creature c, int offset, PlayerChoiceContext? choiceContext = null) => await ResistanceSystem.ChangeMax(c, offset, choiceContext);

    /// <summary>状态切换：进入燃烧。</summary>
    public static void EnterBurningState(this Creature c, PlayerChoiceContext? choiceContext = null) => ResistanceSystem.EnterBurning(c, choiceContext);

    /// <summary>状态切换：退出燃烧。</summary>
    public static void ExitBurningState(this Creature c, PlayerChoiceContext? choiceContext = null) => ResistanceSystem.ExitBurning(c, choiceContext);
}
