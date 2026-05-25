#nullable disable
using BepInEx.Logging;
using HarmonyLib;
using PowerTracker.UI;
using Manager;
using UnityEngine;

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
            PowerTrackerInjector.Inject(__instance, Log, Plugin.TrackerConfig);
        }
    }

    internal static class PauseScreenEscPatch
    {
        internal static GameObject PanelGO;
        private static int _suppressFrame = -1;
        private static MonoBehaviour _pauseScreenInstance;
        private static System.Reflection.MethodInfo _closeMethod;
        private static System.Type _pauseScreenType;

        internal static void Apply(Harmony harmony, ManualLogSource log)
        {
            _pauseScreenType = AccessTools.TypeByName("Game.UI.Screens.PauseScreen");
            if (_pauseScreenType == null) { log.LogWarning("[PT] PauseScreen type not found — ESC eating disabled"); return; }

            // Intercept Visible = true on BaseScreen before PauseScreen opens — eliminates the flash
            var baseScreenType = AccessTools.TypeByName("Game.UI.Screens.BaseScreen");
            if (baseScreenType != null)
            {
                var setVisible = AccessTools.PropertySetter(baseScreenType, "Visible");
                if (setVisible != null)
                    harmony.Patch(setVisible, prefix: new HarmonyMethod(typeof(PauseScreenEscPatch), nameof(VisibleSetPrefix)));
            }

            // Fallback: also patch Update in case PauseScreen opens via a path that bypasses set_Visible
            var prefix = new HarmonyMethod(typeof(PauseScreenEscPatch), nameof(Prefix));
            int count = 0;
            foreach (var name in new[] { "CustomUpdate", "Update", "Open", "Show", "Toggle" })
            {
                var m = AccessTools.Method(_pauseScreenType, name);
                if (m != null) { harmony.Patch(m, prefix: prefix); count++; }
            }
            if (count == 0) log.LogWarning("[PT] PauseScreen: no methods patched — ESC eating disabled");

            foreach (var name in new[] { "Awake", "Start" })
            {
                var m = AccessTools.Method(_pauseScreenType, name);
                if (m != null) { harmony.Patch(m, postfix: new HarmonyMethod(typeof(PauseScreenEscPatch), nameof(CapturePostfix))); break; }
            }
        }

        // Fires before BaseScreen.set_Visible executes — closes our panel and suppresses PauseScreen open
        static bool VisibleSetPrefix(object __instance, bool value)
        {
            if (value && _pauseScreenType != null && _pauseScreenType.IsInstanceOfType(__instance) && PanelGO != null && PanelGO.activeSelf)
            {
                PanelGO.SetActive(false);
                return false;
            }
            return true;
        }

        static void CapturePostfix(MonoBehaviour __instance)
        {
            _pauseScreenInstance = __instance;
            // BaseScreen.set_Visible restores timescale + re-enables UIManager/InputManager/Camera
            var t = __instance.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var prop = t.GetProperty("Visible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (prop?.SetMethod != null) { _closeMethod = prop.SetMethod; break; }
                t = t.BaseType;
            }
        }

        static bool Prefix()
        {
            if (PanelGO != null && PanelGO.activeSelf)
            {
                PanelGO.SetActive(false);
                _suppressFrame = Time.frameCount;
                return false;
            }
            return _suppressFrame != Time.frameCount;
        }

        internal static void LateUpdateTick()
        {
            if (_suppressFrame != Time.frameCount) return;
            if (_pauseScreenInstance == null || !_pauseScreenInstance.gameObject.activeSelf) return;
            if (_closeMethod != null)
                _closeMethod.Invoke(_pauseScreenInstance, new object[] { false });
            else
                _pauseScreenInstance.gameObject.SetActive(false);
        }
    }
}
