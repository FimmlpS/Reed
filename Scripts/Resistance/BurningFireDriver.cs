using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Reed.Scripts.Nodes;

namespace Reed.Scripts.Resistance;

/// <summary>
/// “常驻火焰”驱动器。原版所有火焰特效（NFireBurningVfx / NFireSmokePuffVfx / NGroundFireVfx…）
/// 都只是“触发一次、播完即走”，没有与血量/体型绑定的常驻版本，所以这里自己维护一个
/// <see cref="NReedGroundFireVfx"/> 的生命周期：燃烧开始生成，燃烧结束交给特效自己淡出。
/// </summary>
internal sealed class BurningFireDriver
{
    private readonly Creature _creature;
    private readonly Control _scheduler;   // 借用抗性条根节点：其 Size.X 恒与血条当前宽度同步（几何钩子保证）
    private NReedGroundFireVfx? _vfx;      // 当前活着的火焰（用于宽度变化时重新铺开）
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
            RefreshFootprint();
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
    /// 把当前血条宽度交给火焰特效：由特效自己决定铺几个火簇、横向铺多宽、纵向长多高。
    /// 这里不再对整个特效做等比缩放——那会让大体型怪身上的火焰高得挡住视线。
    /// </summary>
    private void RefreshFootprint()
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
        _vfx.SetFootprintWidth(barWidth);
    }

    /// <summary>
    /// 订阅抗性条根节点的 Resized：战斗中途生物改宽（SL 恢复、巨型怪生长…）时，血条宽一变，
    /// 抗性条在几何钩子里被重排 → 这里随之重新铺开火焰。
    /// </summary>
    private void HookScheduler()
    {
        if (_hooked || _scheduler == null)
        {
            return;
        }
        _hooked = true;
        _scheduler.Resized += RefreshFootprint;
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
            _scheduler.Resized -= RefreshFootprint;
            _scheduler.TreeExited -= OnSchedulerExited;
        }
    }
}
