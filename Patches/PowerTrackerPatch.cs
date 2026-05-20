using BepInEx.Logging;
using HarmonyLib;
using PowerTracker.UI;
using Manager;

namespace PowerTracker.Patches
{
    [HarmonyPatch(typeof(NotificationManager), "Awake")]
    internal static class PowerTrackerPatch
    {
        internal static ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("PowerTracker");

        [HarmonyPostfix]
        static void Postfix(NotificationManager __instance)
        {
            Log.LogInfo("[PT] NotificationManager.Awake postfix — injecting");
            PowerTrackerInjector.Inject(__instance, Log);
        }
    }
}
