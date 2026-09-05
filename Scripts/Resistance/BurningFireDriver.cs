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
    /// <summary>火焰特效设计基准宽度（像素）。按 血条宽 / 该值 得到根节点统一缩放。见 <see cref="RefreshScale"/>。</summary>
    private const float BaseWidth = 240f;

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
            if(node is NReedGroundFireVfx rVfx)
            {
                rVfx.FinishBurning();
            }
        }
    }

    /// <summary>
    /// 按当前血条宽度重缩放存活的火焰：scale = 血条宽 / 240。
    /// 宽度为 0（尚未布局）时保持上次值；极端值限幅防爆。
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
        _vfx.Scale = Vector2.One * Mathf.Clamp(barWidth / BaseWidth, 0.2f, 5f);
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
