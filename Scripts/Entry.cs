using BaseLib.Config;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using Reed.Scripts.Resistance;

namespace Reed.Scripts;

[ModInitializer("Init")]
public class Entry
{
	private static Harmony? _harmony;

	public static void Init()
	{
		ModConfigRegistry.Register("Reed", new ReedModConfig());

		// 让挂在生物身上的火花牌也能收到 combat hook（它们不在任何牌堆里，原版不会收集它们）。
		ResistanceSystem.RegisterHookSubscription();

		_harmony = new Harmony("sts2.fimmlps.reed");
		//SINGLE

		_harmony.PatchAll();
		ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
		Log.Debug("Reed Mod initialized!");
	}
}
