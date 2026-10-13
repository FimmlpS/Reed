using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace Reed.Scripts.Helper;

public class ReedHelper
{
    public static void CreateFireFx(Creature target, float scale = 0.8f)
    {
        NGroundFireVfx nGroundFireVfx = NGroundFireVfx.Create(target);
        if (nGroundFireVfx == null)
        {
            return;
        }
        SfxCmd.Play("event:/sfx/characters/attack_fire");
        nGroundFireVfx.Scale = Vector2.One * scale;
        NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(nGroundFireVfx);
    }
}