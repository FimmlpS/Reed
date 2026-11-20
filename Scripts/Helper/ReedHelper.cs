using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace Reed.Scripts.Helper;

public class ReedHelper
{
    /// <summary>
    /// 引爆火焰特效。目标已离开战斗 / 房间正在回收时直接跳过：
    /// 原版 <see cref="NGroundFireVfx.Create"/> 直接解引用 <see cref="NCombatRoom.Instance"/>，
    /// 战斗结束后再调会 NRE，而异常出现在出牌/结算的 await 链里会把后续牌全卡住。
    /// </summary>
    public static void CreateFireFx(Creature target, float scale = 0.8f)
    {
        if (!CanStillBurst(target))
        {
            return;
        }
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
        {
            return;
        }
        NGroundFireVfx? vfx = NGroundFireVfx.Create(target);
        if (vfx == null)
        {
            return;
        }
        SfxCmd.Play("event:/sfx/characters/attack_fire");
        vfx.Scale = Vector2.One * scale;
        if (room.CombatVfxContainer is { } container)
        {
            container.AddChildSafely(vfx);
        }
        else
        {
            room.AddChildSafely(vfx);
        }
    }

    /// <summary>还能继续吃爆发的目标：活着且仍在战斗里。死亡后剩余爆发次数没有意义。</summary>
    public static bool CanStillBurst(Creature? target)
        => target != null && target.IsAlive && target.CombatState != null;
}
