using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Patches;

[HarmonyPatch(typeof(Hook),nameof(Hook.AfterCardPlayed),MethodType.Async)]
public static class BurnHealPatch
{
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> BeforeStart(ILGenerator generator, IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        return AsyncMethodCall.Create(generator, instructions, original, 
        AccessTools.Method(typeof(BurnHealPatch),nameof(AfterCardPlayedHooks)),beforeState:original);
    }

    //注意日后尽量别教今天的泪白流
    private static async Task AfterCardPlayedHooks(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach(Creature creature in combatState.Creatures)
        {
            if (creature.IsAlive && creature.IsBurningResistance())
            {
                await ReedBurnCmd.Heal(1)
                .FromCard(cardPlay.Card,cardPlay)
                .Targeting(creature)
                .Execute(choiceContext);
            }
        }
    }
}
