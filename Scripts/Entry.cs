using BaseLib.Config;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Reed.Scripts;

[ModInitializer("Init")]
public class Entry
{
	private static Harmony? _harmony;

	public static void Init()
	{
		//ModConfigRegistry.Register("Reed", new ReedModConfig());

		_harmony = new Harmony("sts2.fimmlps.reed");
		//SINGLE

		_harmony.PatchAll();
		ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
		Log.Debug("Reed Mod initialized!");
	}
}
