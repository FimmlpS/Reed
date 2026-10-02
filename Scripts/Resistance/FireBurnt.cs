using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Reed.Scripts.Resistance;

public class FireBurnt
{
    private decimal _blv;
    private Creature _target;
    private int _times;

    public FireBurnt(Creature target, decimal blv = 1m, int times = 1)
    {
        _target = target;
        _blv = blv;
        _times = times;
    }

    public FireBurnt(FireBurnt fireBurnt)
    {
        _target = fireBurnt.Target;
        _blv = fireBurnt.Blv;
        _times = fireBurnt.Times;
    }

    public FireBurnt ModifyBlv(decimal blv)
    {
        _blv += blv;
        return this;
    }

    public FireBurnt ModifyTimes(int times)
    {
        _times += times;
        return this;
    }

    public decimal Blv => _blv;
    public Creature Target => _target;
    public int Times => _times;
}