using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Reed.Scripts.Nodes;

namespace Reed.Scripts.Resistance;

/// <summary>
/// “常驻火焰”驱动器。原版所有火焰特效（NFireBurningVfx / NFireSmokePuffVfx / NGroundFireVfx…）
/// </summary>
internal sealed class BurningFireDriver
{
    /// <summary>火焰特效设计基准宽度（像素）。血条正好这么宽时缩放为 1。见 <see cref="RefreshScale"/>。</summary>
    private const float BaseWidth = 240f;

    /// <summary>
    /// 宽血条的放缓指数：宽度超出 <see cref="BaseWidth"/> 后，缩放按 (血条宽 / 240) 的该次幂增长。
    /// 取 1 就是原来的线性；越小增长越慢。0.65 是「明显放缓、但比 ln 那种程度强」的取值：
    /// 480 宽 ×1.57（线性 2.0）、720 宽 ×2.04（3.0）、1200 宽 ×2.85（5.0）。
    /// </summary>
    private const float ScaleExponent = 0.65f;

    private readonly Creature _creature;
    private readonly Control _scheduler;   // 借用抗性条根节点：其 Size.X 恒与血条当前宽度同步（几何钩子保证）
    private NReedGroundFireVfx? _vfx;      // 当前活着的火焰（用于宽度变化时实时重缩放）
    private bool _hooked;                  // Resized 订阅是否已挂

    public bool Active { get; private set; }

    public BurningFireDriver(Creature creature, Control scheduler)
    {
        _creature = creature;
        _scheduler = scheduler;
        HookScheduler();
    }

    public void Start()
    {
        if (Active)
        {
            return;
        }
        Active = true;
        SpawnFlame();
    }

    public void Stop()
    {
        Active = false;
        StopFlame();
    }

    private void SpawnFlame()
    {
        NReedGroundFireVfx? vfx = NReedGroundFireVfx.Create(_creature);
        if (vfx != null)
        {
            _vfx = vfx;
            RefreshScale();
            _creature.GetVfxContainer()?.AddChildSafely(vfx);
        }
    }

    private void StopFlame()
    {
        _vfx = null;
        Control? control = _creature.GetVfxContainer();
        foreach(Node node in control?.GetChildren()??[])
        {
            if(node is NReedGroundFireVfx rVfx && rVfx.CreatureBelonged == _creature)
            {
                rVfx.FinishBurning();
            }
        }
    }

    /// <summary>
    /// 按当前血条宽度重缩放存活的火焰。缩放 = (血条宽 / 240) 的 <see cref="ScaleExponent"/> 次幂——
    /// 比线性增长慢，且血条越宽越慢，所以大血条不会顶着巨大的火。基准宽（240）处恰好为 1，
    /// 更窄的条保持原等比映射不变；宽度为 0（尚未布局）时保持上次值；极端值限幅防爆。
    /// </summary>
    private void RefreshScale()
    {
        if (_vfx == null || !GodotObject.IsInstanceValid(_vfx))
        {
            return;
        }
        float barWidth = _scheduler?.Size.X ?? 0f;
        if (barWidth <= 0f)
        {
            return;
        }
        float ratio = barWidth / BaseWidth;
        float scale = ratio <= 1f ? ratio : Mathf.Pow(ratio, ScaleExponent);
        _vfx.Scale = Vector2.One * Mathf.Clamp(scale, 0.2f, 5f);
    }

    /// <summary>
    /// 订阅抗性条根节点的 Resized：战斗中途生物改宽（SL 恢复、巨型怪生长…）时，血条宽一变，
    /// 抗性条在几何钩子里被重排 → 这里随之把火焰缩放到新宽度。
    /// </summary>
    private void HookScheduler()
    {
        if (_hooked || _scheduler == null)
        {
            return;
        }
        _hooked = true;
        _scheduler.Resized += RefreshScale;
        _scheduler.TreeExited += OnSchedulerExited;
    }

    private void OnSchedulerExited()
    {
        if (!_hooked)
        {
            return;
        }
        _hooked = false;
        if (GodotObject.IsInstanceValid(_scheduler))
        {
            _scheduler.Resized -= RefreshScale;
            _scheduler.TreeExited -= OnSchedulerExited;
        }
    }
}
