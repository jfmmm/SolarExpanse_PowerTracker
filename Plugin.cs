#nullable disable
using BepInEx;
using HarmonyLib;
using PowerTracker.UI;

namespace PowerTracker
{
    [BepInPlugin("com.mod.solarexpanse.powertracker", "PowerTracker", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static PowerTrackerConfig TrackerConfig;

        private void Awake()
        {
            TrackerConfig = new PowerTrackerConfig(Config);
            var harmony = new Harmony("com.mod.solarexpanse.powertracker");
            harmony.PatchAll();
            Patches.PauseScreenEscPatch.Apply(harmony, Logger);
            Logger.LogInfo("PowerTracker loaded");
        }
    }
}
