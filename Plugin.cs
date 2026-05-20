#nullable disable
using BepInEx;
using HarmonyLib;
using PowerTracker.UI;

namespace PowerTracker
{
    [BepInPlugin("com.mod.solarexpanse.powertracker", "PowerTracker", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static PowerTrackerConfig TrackerConfig;

        private void Awake()
        {
            TrackerConfig = new PowerTrackerConfig(Config);
            new Harmony("com.mod.solarexpanse.powertracker").PatchAll();
            Logger.LogInfo("PowerTracker loaded");
        }
    }
}
