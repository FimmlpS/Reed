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
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Reed.Scripts.Cards.Attack;
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
        cm.CombatSetUp += OnCombatSetUp;
        cm.CombatEnded += _ => OnCombatEnded();
        _combatHooked = true;
    }

    private static void OnCombatSetUp(CombatState state)
    {
        bool reedInBattle = state.Players.Any(p => p.Character is ReedCharacter);
        ForceShowAll = reedInBattle;
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

    private static void OnCombatEnded()
    {
        ForceShowAll = false;
        _sparkDemoSeeded = false;
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
        d = new ResistanceData();
        _data[creature] = d;
        return d;
    }

    public static int GetCurrent(Creature c) => GetData(c).Current;
    public static int GetMax(Creature c) => GetData(c).Max;
    public static bool IsBurning(Creature c) => GetData(c).Burning;
    public static bool IsFull(Creature c) => GetData(c).Current >= GetData(c).Max;
    public static bool HasChangedThisBattle(Creature c) => GetData(c).Changed;

    // ============ 火花牌（每敌人绑定若干张；按 owner=玩家 NetId 区分，每人独立一组） ============

    /// <summary>每个（生物 × owner）可绑定的火花牌上限。</summary>
    public const int DefaultSparkMax = 3;

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

    /// <summary>读取某（生物 × owner）火花上限；没单独设过则返回默认 DefaultSparkMax。</summary>
    public static int GetSparkMax(Creature c, Player owner)
    {
        if (c == null || owner == null)
        {
            return DefaultSparkMax;
        }
        ResistanceData d = GetData(c);
        return d.SparkMaxes.TryGetValue(owner.NetId, out int max) ? max : DefaultSparkMax;
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
        SparksChanged?.Invoke(c);
        SparkAdded?.Invoke(c, owner, spark);
        return true;
    }

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
        if (!d.Sparks.Remove(owner.NetId))
        {
            return false;
        }
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

    // ============ 火花牌 测试种子（给第一只怪物放若干张本地玩家的“打击”） ============

    private const bool EnableDemoSparkSeed = true;
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
        Creature? enemy = state.Creatures.FirstOrDefault(c => c.IsMonster);
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

    /// <summary>扣抗性（最低 0）。不自动触发燃烧状态——按需调用 EnterBurning。</summary>
    public static void Reduce(Creature c, int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        ResistanceData d = GetData(c);
        int nv = Math.Max(0, d.Current - amount);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
        if (d.Current == 0)
        {
            EnterBurning(c);
        }
    }

    /// <summary>恢复抗性（最高回满 max）。按需调用 ExitBurning。</summary>
    public static void Restore(Creature c, int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        ResistanceData d = GetData(c);
        int nv = Math.Min(d.Max, d.Current + amount);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
        if (d.Current == d.Max)
        {
            ExitBurning(c);
        }
    }

    /// <summary>直接设定当前抗性（自动收敛到 0..max）。</summary>
    public static void SetCurrent(Creature c, int value)
    {
        ResistanceData d = GetData(c);
        int nv = Math.Clamp(value, 0, d.Max);
        if (nv == d.Current)
        {
            return;
        }
        d.Current = nv;
        AfterMutation(c, d);
    }

    // ============ 两个状态切换函数（规则2/3：蓝 ↔ 紫 + 燃烧特效）============

    /// <summary>进入燃烧状态：条变紫，生物播放常驻火焰（原版特效循环铺放）。</summary>
    public static void EnterBurning(Creature c)
    {
        ResistanceData d = GetData(c);
        if (d.Burning)
        {
            return;
        }
        d.Burning = true;
        SyncVisuals(c, d);
    }

    /// <summary>退出燃烧状态：条回蓝，停止火焰。</summary>
    public static void ExitBurning(Creature c)
    {
        ResistanceData d = GetData(c);
        if (!d.Burning)
        {
            return;
        }
        d.Burning = false;
        d.Fire?.Stop();
        SyncVisuals(c, d);
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
        d.Bar?.SetShown(false);
    }

    private static void OnCreatureRevived(Creature c)
    {
        ResistanceData? d = _data[c];
        if (d == null)
        {
            return;
        }
        SyncVisuals(c, d);
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
        creature.Died += OnCreatureDied;
        creature.Revived += OnCreatureRevived;

        // 条离开场景（生物死亡/战斗结束移除节点）时解绑，避免长期持有。
        bar.Root.TreeExited += () =>
        {
            bar.Spark?.Detach();
            creature.Died -= OnCreatureDied;
            creature.Revived -= OnCreatureRevived;
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

    public static void DamageResistance(this Creature c, int amount) => ResistanceSystem.Reduce(c, amount);
    public static void RestoreResistance(this Creature c, int amount) => ResistanceSystem.Restore(c, amount);
    public static void SetResistance(this Creature c, int value) => ResistanceSystem.SetCurrent(c, value);

    /// <summary>状态切换：进入燃烧。</summary>
    public static void EnterBurningState(this Creature c) => ResistanceSystem.EnterBurning(c);

    /// <summary>状态切换：退出燃烧。</summary>
    public static void ExitBurningState(this Creature c) => ResistanceSystem.ExitBurning(c);
}
