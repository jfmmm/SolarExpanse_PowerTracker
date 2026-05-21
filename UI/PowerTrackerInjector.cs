#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using Extensions;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.ObjectInfoDataScripts.CustomFacilitiesAndModules;
using Game.UI;
using Manager;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PowerTracker.UI
{
    internal static class PowerTrackerInjector
    {
        private static readonly FieldInfo FieldShowBtn =
            typeof(NotificationManager).GetField("showNotificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldHistoryGO =
            typeof(NotificationManager).GetField("notificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Inject(NotificationManager nm, ManualLogSource log, PowerTrackerConfig config)
        {
            try
            {
                Button showBtn = FieldShowBtn?.GetValue(nm) as Button;
                if (showBtn == null) { log.LogError("[PT] showNotificationHistory not found"); return; }

                GameObject historyGO = FieldHistoryGO?.GetValue(nm) as GameObject;
                if (historyGO == null) { log.LogError("[PT] notificationHistory GO not found"); return; }

                RectTransform showBtnRT = showBtn.GetComponent<RectTransform>();
                Canvas btnCanvas = showBtn.GetComponentInParent<Canvas>();
                if (btnCanvas == null) { log.LogError("[PT] could not find canvas"); return; }

                TMP_FontAsset fontAsset = FindFontAsset(nm, historyGO, log);

                // ── Clone notification history as our panel ──────────────────────────
                GameObject panelGO = UnityEngine.Object.Instantiate(historyGO, btnCanvas.transform);
                panelGO.name = "modPowerTrackerPanel";
                panelGO.transform.SetAsLastSibling();
                RectTransform panelRT = panelGO.GetComponent<RectTransform>();

                panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
                panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
                panelRT.pivot            = new Vector2(0f, 1f);
                panelRT.sizeDelta        = new Vector2(720f, 280f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);

                LayoutElement panelLE = panelGO.AddComponent<LayoutElement>();
                panelLE.ignoreLayout = true;

                Image bgSource = null;
                foreach (Image img in panelGO.GetComponentsInChildren<Image>(includeInactive: true))
                    if (img.sprite != null) { bgSource = img; break; }

                Image panelBg = panelGO.GetComponent<Image>() ?? panelGO.AddComponent<Image>();
                if (bgSource != null)
                {
                    panelBg.sprite   = bgSource.sprite;
                    panelBg.color    = bgSource.color;
                    panelBg.type     = bgSource.type;
                    panelBg.material = bgSource.material;
                }
                else panelBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
                panelBg.raycastTarget = true;

                for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(panelGO.transform.GetChild(i).gameObject);

                foreach (CanvasGroup cg in panelGO.GetComponents<CanvasGroup>())
                { cg.interactable = true; cg.blocksRaycasts = true; }

                foreach (ScrollRect sr in panelGO.GetComponents<ScrollRect>())
                    UnityEngine.Object.DestroyImmediate(sr);
                foreach (LayoutGroup lg in panelGO.GetComponents<LayoutGroup>())
                    UnityEngine.Object.DestroyImmediate(lg);
                ContentSizeFitter existingCSF = panelGO.GetComponent<ContentSizeFitter>();
                if (existingCSF != null) UnityEngine.Object.DestroyImmediate(existingCSF);

                panelRT.sizeDelta = new Vector2(720f, 280f);

                // ── Tab bar ──────────────────────────────────────────────────────────
                const float TabH = 24f;
                Color tabActive   = new Color(0.20f, 0.35f, 0.40f, 1f);
                Color tabInactive = new Color(0.10f, 0.15f, 0.18f, 1f);

                GameObject tabBarGO = new GameObject("TabBar", typeof(RectTransform));
                tabBarGO.transform.SetParent(panelGO.transform, false);
                RectTransform tabBarRT = tabBarGO.GetComponent<RectTransform>();
                tabBarRT.anchorMin        = new Vector2(0f, 1f);
                tabBarRT.anchorMax        = new Vector2(1f, 1f);
                tabBarRT.pivot            = new Vector2(0.5f, 1f);
                tabBarRT.sizeDelta        = new Vector2(-16f, TabH);
                tabBarRT.anchoredPosition = new Vector2(0f, -8f);

                HorizontalLayoutGroup tabHLG = tabBarGO.AddComponent<HorizontalLayoutGroup>();
                tabHLG.childControlWidth      = true;
                tabHLG.childForceExpandWidth  = true;
                tabHLG.childControlHeight     = true;
                tabHLG.childForceExpandHeight = true;
                tabHLG.spacing = 4f;

                (Button statusTabBtn,   Image statusTabImg)   = MakeTabButton(tabBarGO.transform, fontAsset, "STATUS",           tabActive);
                (Button settingsTabBtn, Image settingsTabImg) = MakeTabButton(tabBarGO.transform, fontAsset, "ALERT THRESHOLDS", tabInactive);

                // ── Scroll viewport — top offset = 8 border + TabH + 4 gap ─────────
                GameObject viewportGO = new GameObject("ScrollViewport", typeof(RectTransform));
                viewportGO.transform.SetParent(panelGO.transform, false);
                viewportGO.transform.SetAsLastSibling();
                RectTransform viewportRT = viewportGO.GetComponent<RectTransform>();
                viewportRT.anchorMin = Vector2.zero;
                viewportRT.anchorMax = Vector2.one;
                viewportRT.pivot     = new Vector2(0.5f, 0.5f);
                viewportRT.offsetMin = new Vector2(8f, 8f);
                viewportRT.offsetMax = new Vector2(-22f, -(8f + TabH + 4f));
                viewportGO.AddComponent<RectMask2D>();

                // STATUS content
                GameObject contentGO = MakeScrollContent("ScrollContent", viewportGO.transform);
                RectTransform contentRT = contentGO.GetComponent<RectTransform>();

                // SETTINGS content
                GameObject settingsContentGO = MakeScrollContent("SettingsContent", viewportGO.transform);
                RectTransform settingsContentRT = settingsContentGO.GetComponent<RectTransform>();
                settingsContentGO.SetActive(false);

                // ── Vertical scrollbar ───────────────────────────────────────────────
                GameObject scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform));
                scrollbarGO.transform.SetParent(panelGO.transform, false);
                RectTransform scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
                scrollbarRT.anchorMin        = new Vector2(1f, 0f);
                scrollbarRT.anchorMax        = new Vector2(1f, 1f);
                scrollbarRT.pivot            = new Vector2(1f, 0.5f);
                scrollbarRT.sizeDelta        = new Vector2(6f, -(8f + TabH + 4f + 8f));
                scrollbarRT.anchoredPosition = new Vector2(-8f, -(8f + TabH + 4f - 8f) / 2f);
                Image scrollbarBg = scrollbarGO.AddComponent<Image>();
                Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;

                GameObject slidingAreaGO = new GameObject("SlidingArea", typeof(RectTransform));
                slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
                RectTransform slidingAreaRT = slidingAreaGO.GetComponent<RectTransform>();
                slidingAreaRT.anchorMin = Vector2.zero; slidingAreaRT.anchorMax = Vector2.one;
                slidingAreaRT.sizeDelta = Vector2.zero; slidingAreaRT.anchoredPosition = Vector2.zero;

                GameObject handleGO = new GameObject("Handle", typeof(RectTransform));
                handleGO.transform.SetParent(slidingAreaGO.transform, false);
                RectTransform handleRT = handleGO.GetComponent<RectTransform>();
                handleRT.anchorMin = Vector2.zero; handleRT.anchorMax = Vector2.one;
                handleRT.sizeDelta = Vector2.zero;
                Image handleImg = handleGO.AddComponent<Image>();

                scrollbar.handleRect    = handleRT;
                scrollbar.targetGraphic = handleImg;
                CopyGameScrollbarStyle(scrollbarBg, handleImg, log);

                ScrollRect scrollRect = panelGO.AddComponent<ScrollRect>();
                scrollRect.viewport                    = viewportRT;
                scrollRect.content                     = contentRT;
                scrollRect.verticalScrollbar           = scrollbar;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scrollRect.horizontal        = false;
                scrollRect.vertical          = true;
                scrollRect.scrollSensitivity = 30f;
                scrollRect.movementType      = ScrollRect.MovementType.Clamped;

                // ── Bottom resize handle ─────────────────────────────────────────────
                GameObject resizeHandleGO = new GameObject("ResizeHandle", typeof(RectTransform));
                resizeHandleGO.transform.SetParent(panelGO.transform, false);
                RectTransform resizeRT = resizeHandleGO.GetComponent<RectTransform>();
                resizeRT.anchorMin = new Vector2(0f, 0f); resizeRT.anchorMax = new Vector2(1f, 0f);
                resizeRT.pivot = new Vector2(0.5f, 1f);
                resizeRT.sizeDelta = new Vector2(0f, 10f); resizeRT.anchoredPosition = Vector2.zero;
                resizeHandleGO.AddComponent<Image>().color = Color.clear;
                resizeHandleGO.AddComponent<ResizeHandle>().PanelRT = panelRT;

                panelGO.SetActive(false);

                PowerTrackerPanel tracker = panelGO.AddComponent<PowerTrackerPanel>();
                tracker.ContentParent     = contentGO.transform;
                tracker.FontAsset         = fontAsset;
                tracker.TrackerLog        = log;
                tracker.PanelRT           = panelRT;
                tracker.Config            = config;
                tracker.SettingsContentGO = settingsContentGO;
                tracker.SettingsContentRT = settingsContentRT;
                tracker.StatusContentRT   = contentRT;
                tracker.ScrollRectRef     = scrollRect;
                tracker.StatusTabImg      = statusTabImg;
                tracker.SettingsTabImg    = settingsTabImg;
                tracker.TabActiveColor    = tabActive;
                tracker.TabInactiveColor  = tabInactive;

                // ── Floating draggable indicator button ──────────────────────────────
                GameObject indicatorGO = new GameObject("modPowerTrackerButton", typeof(RectTransform));
                indicatorGO.transform.SetParent(btnCanvas.transform, false);
                indicatorGO.transform.SetAsLastSibling();
                indicatorGO.AddComponent<LayoutElement>().ignoreLayout = true;

                RectTransform indicatorRT = indicatorGO.GetComponent<RectTransform>();
                indicatorRT.anchorMin = new Vector2(0.5f, 0.5f); indicatorRT.anchorMax = new Vector2(0.5f, 0.5f);
                indicatorRT.pivot     = new Vector2(0f, 1f);
                indicatorRT.sizeDelta = new Vector2(150f, 30f);
                indicatorRT.anchoredPosition = new Vector2(-9999f, -9999f);

                Image bg = indicatorGO.AddComponent<Image>();
                Image origBtnImg = showBtn.GetComponent<Image>();
                if (origBtnImg != null) { bg.sprite = origBtnImg.sprite; bg.type = origBtnImg.type; bg.color = origBtnImg.color; }
                else bg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

                TextMeshProUGUI indicatorLabel = MakeButtonLabel(indicatorGO, fontAsset);

                DraggableMover mover = indicatorGO.AddComponent<DraggableMover>();
                mover.Bg          = bg;
                mover.NormalColor = bg.color;
                mover.HoverColor  = bg.color * 1.3f;
                mover.PressColor  = bg.color * 0.7f;

                mover.OnClick = () =>
                {
                    bool open = panelGO.activeSelf;
                    if (!open)
                    {
                        panelGO.SetActive(true);
                        panelRT.anchorMin = new Vector2(0.5f, 0.5f); panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                        panelRT.pivot     = new Vector2(0f, 1f);
                        panelRT.anchoredPosition = new Vector2(
                            indicatorRT.anchoredPosition.x,
                            indicatorRT.anchoredPosition.y - indicatorRT.sizeDelta.y - 4f);
                        scrollRect.verticalNormalizedPosition = 1f;
                        tracker.RefreshRows();
                    }
                    else panelGO.SetActive(false);
                };

                statusTabBtn.onClick.AddListener(()  => tracker.ShowStatusTab());
                settingsTabBtn.onClick.AddListener(() => tracker.ShowSettingsTab());

                mover.PanelRT   = panelRT;
                mover.PanelGO   = panelGO;
                mover.ShowBtnRT = showBtnRT;
                mover.Log       = log;

                tracker.IndicatorLabel = indicatorLabel;
                tracker.IndicatorRT    = indicatorRT;
                tracker.Mover          = mover;
                mover.FlashLabel       = indicatorLabel;

                indicatorGO.AddComponent<PowerTrackerUpdater>().Tracker = tracker;
                log.LogInfo("[PT] Injection complete");
            }
            catch (Exception e) { log.LogError($"[PT] Inject exception: {e}"); }
        }

        private static GameObject MakeScrollContent(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f); rt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            vlg.spacing = 1f; vlg.padding = new RectOffset(4, 4, 4, 4);

            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            return go;
        }

        private static (Button btn, Image img) MakeTabButton(Transform parent, TMP_FontAsset font, string label, Color color)
        {
            GameObject go = new GameObject($"Tab_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None;

            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text      = label;
            tmp.fontSize  = 10f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = Color.white;
            tmp.raycastTarget = false;
            return (btn, img);
        }

        private static TextMeshProUGUI MakeButtonLabel(GameObject parent, TMP_FontAsset font)
        {
            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(parent.transform, false);
            RectTransform lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = "<color=#44BB44>●</color>  POWER";
            tmp.fontSize = 11f; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white; tmp.raycastTarget = false;
            return tmp;
        }

        private static void CopyGameScrollbarStyle(Image track, Image handle, ManualLogSource log)
        {
            Scrollbar src = null;
            foreach (Scrollbar sb in Resources.FindObjectsOfTypeAll<Scrollbar>())
            {
                if (sb.name == "Scrollbar" && sb.handleRect != null) continue;
                if (sb.handleRect != null) { src = sb; break; }
            }
            if (src == null) { log.LogWarning("[PT] No game Scrollbar — using fallback"); ApplyFallbackScrollbarStyle(track, handle); return; }
            Image srcTrack  = src.GetComponent<Image>();
            Image srcHandle = src.handleRect.GetComponent<Image>();
            if (srcTrack  != null) { track.sprite  = srcTrack.sprite;  track.color  = srcTrack.color;  track.type  = srcTrack.type; }
            if (srcHandle != null) { handle.sprite = srcHandle.sprite; handle.color = srcHandle.color; handle.type = srcHandle.type; }
        }

        private static void ApplyFallbackScrollbarStyle(Image track, Image handle)
        {
            track.color  = new Color(0.06f, 0.12f, 0.14f, 0.9f);
            handle.color = new Color(0.05f, 0.62f, 0.68f, 0.9f);
        }

        private static TMP_FontAsset FindFontAsset(NotificationManager nm, GameObject historyGO, ManualLogSource log)
        {
            TextMeshProUGUI src = historyGO.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (src?.font != null) { log.LogInfo($"[PT] font '{src.font.name}'"); return src.font; }
            try
            {
                var prefabField = typeof(NotificationManager).GetField("notificationUIPrefab",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var prefab = prefabField?.GetValue(nm);
                if (prefab != null)
                {
                    var textField = prefab.GetType().GetField("text", BindingFlags.Instance | BindingFlags.NonPublic);
                    src = textField?.GetValue(prefab) as TextMeshProUGUI;
                    if (src?.font != null) { log.LogInfo($"[PT] font from prefab '{src.font.name}'"); return src.font; }
                }
            }
            catch (Exception e) { log.LogWarning($"[PT] font fallback: {e.Message}"); }
            log.LogWarning("[PT] No font found");
            return null;
        }
    }

    // ── Power tracker panel ───────────────────────────────────────────────────
    internal class PowerTrackerPanel : MonoBehaviour
    {
        internal Transform ContentParent;
        internal TMP_FontAsset FontAsset;
        internal ManualLogSource TrackerLog;
        internal TextMeshProUGUI IndicatorLabel;
        internal RectTransform PanelRT;
        internal RectTransform IndicatorRT;
        internal DraggableMover Mover;
        internal ScrollRect ScrollRectRef;
        internal PowerTrackerConfig Config;

        // Settings tab
        internal GameObject SettingsContentGO;
        internal RectTransform SettingsContentRT;
        internal RectTransform StatusContentRT;
        internal Image StatusTabImg;
        internal Image SettingsTabImg;
        internal Color TabActiveColor;
        internal Color TabInactiveColor;

        internal const float RefreshInterval = 5.0f;

        // ── Main table column widths ──────────────────────────────────────────
        private const float ColGen     = 72f;
        private const float ColCons    = 72f;
        private const float ColBalance = 72f;
        private const float ColBattery = 155f;

        // ── Sub-table column widths ───────────────────────────────────────────
        private const float SubColGen     = 65f;
        private const float SubColFuel    = 90f;
        private const float SubColWorkers = 72f;
        private const float SubColSupply  = 60f;
        private const float SubColStock   = 110f;

        // Header rows
        private bool _headerBuilt;
        private GameObject _titleRowGO;
        private TextMeshProUGUI _titleLbl;
        private GameObject _titleSepGO;
        private GameObject _headerRowGO;
        private GameObject _headerSepGO;
        private GameObject _emptyMsgGO;
        private TextMeshProUGUI _emptyMsgLbl;
        private readonly Dictionary<string, BodyRowCache> _rowCache = new Dictionary<string, BodyRowCache>();

        // bodyName → distinct fuel-consuming facility names (populated each RefreshRows)
        private Dictionary<string, List<string>> _lastBodyFacilityNames  = new Dictionary<string, List<string>>();
        private Dictionary<string, string>        _lastBodySprites        = new Dictionary<string, string>();
        private Dictionary<string, string>        _lastFacilitySpriteIds  = new Dictionary<string, string>();
        // receiver facility → source body name (rebuilt every RefreshRows)
        private Dictionary<EnergyReceiverFacility, string> _receiverSources = new Dictionary<EnergyReceiverFacility, string>();

        // ── Body-level row ────────────────────────────────────────────────────
        private class BodyRowCache
        {
            public GameObject GO;
            public TextMeshProUGUI NameCol, GenCol, ConsCol, BalanceCol, BatteryCol;
            public Button Btn;
            public bool Expanded;
            public bool HasFacilities;
            public GameObject TopSepGO;
            public GameObject SubHeaderRowGO;
            public GameObject SubHeaderSepGO;
            public GameObject BotSepGO;
            public List<FacilityRowCache> FacilityRows = new List<FacilityRowCache>();
        }

        // ── Facility-level sub-row ────────────────────────────────────────────
        private class FacilityRowCache
        {
            public GameObject GO;
            public Button Btn;
            public TextMeshProUGUI NameCol, GenCol, FuelCol, WorkersCol, SupplyCol, StockCol;
            // For transmitter/receiver facilities
            public bool IsTransfer;
            public bool TransferExpanded;
            public bool IsXmitBox; // whether the box was built for xmit (header differs)
            public GameObject TransferBoxGO;
            public List<TransferRowCache> LinkRows = new List<TransferRowCache>();
        }

        // ── Power transfer sub-row (body column, ratio column, power column) ────
        private class TransferRowCache
        {
            public GameObject GO;
            public TextMeshProUGUI BodyCol, RatioCol, PowerCol;
        }

        // ── Per-facility display data ─────────────────────────────────────────
        private struct TransferLink
        {
            public bool   IsXmit;
            public string PeerName;
            public double Power;
            public float  Ratio; // 0-1, only meaningful for xmit
        }

        private struct FacilityInfo
        {
            public Facility           Fac;
            public string             SpriteId;
            public double             Gen;
            public ResourceDefinition PrimaryResource;
            public double             ConsRatePerDay;
            public double             ResourceStock;
            public long               Workers;
            public long               WorkersNeeded;
            public long               TotalQty;
            // For transmitter/receiver facilities
            public bool IsTransfer;
            public List<TransferLink> TransferLinks;
        }

        private enum Severity { OK, Warning, Critical }

        // ── Tab switching ─────────────────────────────────────────────────────

        internal void ShowStatusTab()
        {
            SettingsContentGO.SetActive(false);
            ContentParent.gameObject.SetActive(true);
            ScrollRectRef.content = StatusContentRT;
            ScrollRectRef.verticalNormalizedPosition = 1f;
            StatusTabImg.color   = TabActiveColor;
            SettingsTabImg.color = TabInactiveColor;
        }

        internal void ShowSettingsTab()
        {
            ContentParent.gameObject.SetActive(false);
            SettingsContentGO.SetActive(true);
            ScrollRectRef.content = SettingsContentRT;
            ScrollRectRef.verticalNormalizedPosition = 1f;
            StatusTabImg.color   = TabInactiveColor;
            SettingsTabImg.color = TabActiveColor;
            RebuildSettingsContent();
        }

        // ── Settings content builder ──────────────────────────────────────────

        private void RebuildSettingsContent()
        {
            for (int i = SettingsContentGO.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(SettingsContentGO.transform.GetChild(i).gameObject);

            if (!Config.InfoDismissed)
                AddSettingsInfoBox(
                    "ENERGY BALANCE: alert when production deficit exceeds a % of total consumption. " +
                    "0% = alert on any shortfall. 100% = never alert (even total blackout).\n" +
                    "FUEL SUPPLY: alert when fuel remaining falls below the set time. " +
                    "WARNING fires first; CRITICAL fires when more urgent. Per-body overrides the global default.");

            // ─── Section 1: ENERGY BALANCE ───────────────────────────────────
            AddSettingsSectionHeader("ENERGY BALANCE THRESHOLDS");

            var (defBW, defBC) = Config.DefaultBalance;
            AddBalanceRow(null, "Global Default", defBW, defBC);
            AddSettingsGlobalSep();

            var sortedBodies = _lastBodyFacilityNames.Keys.OrderBy(k => k).ToList();
            foreach (string bName in sortedBodies)
            {
                var (bw, bc) = Config.GetBalanceThresholds(bName);
                string captured = bName;
                AddBalanceRow(captured, captured, bw, bc);
            }

            if (_lastBodyFacilityNames.Count == 0)
                AddSettingsMessage("No bodies found. Open the Status tab first.");

            AddSettingsDivider();

            // ─── Section 2: FUEL SUPPLY ──────────────────────────────────────
            AddSettingsSectionHeader("FUEL SUPPLY THRESHOLDS");

            var (defFW, defFC) = Config.DefaultFuel;
            AddFuelRow(null, null, "Global Default", defFW, defFC);
            AddSettingsGlobalSep();

            foreach (string bName in sortedBodies)
            {
                List<string> facNames;
                if (!_lastBodyFacilityNames.TryGetValue(bName, out facNames) || facNames.Count == 0)
                    continue;

                AddSettingsBodyLabel(bName);
                foreach (string fName in facNames)
                {
                    var (fw, fc) = Config.GetFuelThresholds(bName, fName);
                    string cb = bName, cf = fName;
                    AddFuelRow(cb, cf, cf, fw, fc);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(SettingsContentRT);
        }

        private void AddSettingsSectionHeader(string title)
        {
            GameObject go = new GameObject("SHdr", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            TextMeshProUGUI lbl = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) lbl.font = FontAsset;
            lbl.text               = title;
            lbl.fontSize           = 11f;
            lbl.fontStyle          = FontStyles.Bold;
            lbl.color              = new Color(0.7f, 0.7f, 0.7f);
            lbl.enableWordWrapping = false;
            lbl.alignment          = TextAlignmentOptions.MidlineLeft;
            lbl.margin             = new Vector4(6, 2, 6, 0);
            lbl.raycastTarget      = false;
        }

        private void AddBalanceRow(string bodyName, string label, double warnPct, double critPct)
        {
            bool isDefault = (bodyName == null);
            Color nameColor = isDefault ? new Color(1f, 0.82f, 0.35f) : Color.white;

            string icon = "";
            if (!isDefault && _lastBodySprites.TryGetValue(bodyName, out string sp) && !string.IsNullOrEmpty(sp))
                icon = $"<sprite name={sp}> ";

            GameObject rowGO = new GameObject($"SBal_{label}", typeof(RectTransform));
            rowGO.transform.SetParent(SettingsContentGO.transform, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.spacing = 4f; hlg.padding = new RectOffset(6, 6, 2, 2);

            MakeSettingsLabel(rowGO.transform, $"{icon}{label}", 0f, 1f, TextAlignmentOptions.MidlineLeft, nameColor);
            MakeSettingsLabel(rowGO.transform, "WARNING", 56f, 0f, TextAlignmentOptions.MidlineRight, new Color(0.7f, 0.7f, 0.7f));
            TMP_InputField warnField = MakeInputField(rowGO.transform, ((int)warnPct).ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "%", 12f, 0f, TextAlignmentOptions.MidlineLeft, new Color(0.5f, 0.5f, 0.5f));
            MakeSettingsLabel(rowGO.transform, "", 12f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);
            MakeSettingsLabel(rowGO.transform, "CRITICAL", 56f, 0f, TextAlignmentOptions.MidlineRight, new Color(0.7f, 0.7f, 0.7f));
            TMP_InputField critField = MakeInputField(rowGO.transform, ((int)critPct).ToString(), 36f);
            MakeSettingsLabel(rowGO.transform, "%", 12f, 0f, TextAlignmentOptions.MidlineLeft, new Color(0.5f, 0.5f, 0.5f));

            if (!isDefault)
            {
                MakeSettingsLabel(rowGO.transform, "", 8f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);
                MakeResetButton(rowGO.transform, 48f, () => { Config.ClearBalance(bodyName); RebuildSettingsContent(); });
            }
            else
                MakeSettingsLabel(rowGO.transform, "", 56f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);

            double[] w = { warnPct };
            double[] c = { critPct };

            void Apply()
            {
                if (w[0] < 0) w[0] = 0;
                if (c[0] < 0) c[0] = 0;
                if (w[0] > c[0]) w[0] = c[0]; // warn% <= crit%
                warnField.text = ((int)w[0]).ToString();
                critField.text = ((int)c[0]).ToString();
                Config.SetBalance(bodyName, w[0], c[0]);
            }

            warnField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) w[0] = v;
                else warnField.text = ((int)w[0]).ToString();
                Apply();
            });

            critField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) c[0] = v;
                else critField.text = ((int)c[0]).ToString();
                Apply();
            });
        }

        private void AddSettingsBodyLabel(string bodyName)
        {
            GameObject go = new GameObject($"SBodyLbl_{bodyName}", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            TextMeshProUGUI lbl = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) lbl.font = FontAsset;
            string bodyIcon = _lastBodySprites.TryGetValue(bodyName, out string bsp) && !string.IsNullOrEmpty(bsp)
                ? $"<sprite name={bsp}> " : "";
            lbl.text               = $"{bodyIcon}{bodyName}";
            lbl.fontSize           = 10f;
            lbl.fontStyle          = FontStyles.Bold;
            lbl.color              = new Color(0.55f, 0.75f, 0.85f);
            lbl.enableWordWrapping = false;
            lbl.alignment          = TextAlignmentOptions.MidlineLeft;
            lbl.margin             = new Vector4(6, 1, 6, 0);
            lbl.raycastTarget      = false;
        }

        private void AddFuelRow(string bodyName, string facName, string label, double warnDays, double critDays)
        {
            bool isDefault = (bodyName == null);
            Color nameColor = isDefault ? new Color(1f, 0.82f, 0.35f) : new Color(0.75f, 0.75f, 0.75f);
            Color grayLbl   = new Color(0.7f, 0.7f, 0.7f);
            Color dimLbl    = new Color(0.5f, 0.5f, 0.5f);

            string facIcon = "";
            if (!isDefault && facName != null && _lastFacilitySpriteIds.TryGetValue(facName, out string sid) && !string.IsNullOrEmpty(sid))
                facIcon = $"<sprite name=\"{sid}\" color=white> ";

            int warnYrs = (int)(warnDays / 365);
            int warnD   = (int)(warnDays % 365);
            int critYrs = (int)(critDays / 365);
            int critD   = (int)(critDays % 365);

            GameObject rowGO = new GameObject($"SFuel_{label}", typeof(RectTransform));
            rowGO.transform.SetParent(SettingsContentGO.transform, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.spacing = 4f; hlg.padding = new RectOffset(isDefault ? 6 : 20, 6, 2, 2);

            string displayLabel = !string.IsNullOrEmpty(facIcon) ? $"{facIcon}{label.TrimStart()}" : label;
            MakeSettingsLabel(rowGO.transform, displayLabel, 0f, 1f, TextAlignmentOptions.MidlineLeft, nameColor);
            MakeSettingsLabel(rowGO.transform, "WARNING", 56f, 0f, TextAlignmentOptions.MidlineRight, grayLbl);
            TMP_InputField warnYrsField  = MakeInputField(rowGO.transform, warnYrs.ToString(), 30f);
            MakeSettingsLabel(rowGO.transform, "y", 10f, 0f, TextAlignmentOptions.MidlineLeft, dimLbl);
            TMP_InputField warnDaysField = MakeInputField(rowGO.transform, warnD.ToString(), 30f);
            MakeSettingsLabel(rowGO.transform, "d", 10f, 0f, TextAlignmentOptions.MidlineLeft, dimLbl);
            MakeSettingsLabel(rowGO.transform, "", 12f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);
            MakeSettingsLabel(rowGO.transform, "CRITICAL", 56f, 0f, TextAlignmentOptions.MidlineRight, grayLbl);
            TMP_InputField critYrsField  = MakeInputField(rowGO.transform, critYrs.ToString(), 30f);
            MakeSettingsLabel(rowGO.transform, "y", 10f, 0f, TextAlignmentOptions.MidlineLeft, dimLbl);
            TMP_InputField critDaysField = MakeInputField(rowGO.transform, critD.ToString(), 30f);
            MakeSettingsLabel(rowGO.transform, "d", 10f, 0f, TextAlignmentOptions.MidlineLeft, dimLbl);

            if (!isDefault)
            {
                MakeSettingsLabel(rowGO.transform, "", 8f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);
                MakeResetButton(rowGO.transform, 48f, () => { Config.ClearFuel(bodyName, facName); RebuildSettingsContent(); });
            }
            else
                MakeSettingsLabel(rowGO.transform, "", 56f, 0f, TextAlignmentOptions.MidlineLeft, Color.clear);

            int[] wyArr = { warnYrs };
            int[] wdArr = { warnD };
            int[] cyArr = { critYrs };
            int[] cdArr = { critD };

            void Apply()
            {
                int w = wyArr[0] * 365 + wdArr[0];
                int c = cyArr[0] * 365 + cdArr[0];
                if (w < 0) w = 0;
                if (c < 0) c = 0;
                if (w < c) w = c; // warnDays >= critDays
                wyArr[0] = w / 365; wdArr[0] = w % 365;
                cyArr[0] = c / 365; cdArr[0] = c % 365;
                warnYrsField.text  = wyArr[0].ToString();
                warnDaysField.text = wdArr[0].ToString();
                critYrsField.text  = cyArr[0].ToString();
                critDaysField.text = cdArr[0].ToString();
                Config.SetFuel(bodyName, facName, w, c);
            }

            warnYrsField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) wyArr[0] = v;
                else warnYrsField.text = wyArr[0].ToString();
                Apply();
            });
            warnDaysField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) wdArr[0] = v;
                else warnDaysField.text = wdArr[0].ToString();
                Apply();
            });
            critYrsField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) cyArr[0] = v;
                else critYrsField.text = cyArr[0].ToString();
                Apply();
            });
            critDaysField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int v) && v >= 0) cdArr[0] = v;
                else critDaysField.text = cdArr[0].ToString();
                Apply();
            });
        }

        private void MakeSettingsLabel(Transform parent, string text, float preferred, float flexible,
            TextAlignmentOptions align, Color color)
        {
            GameObject go = new GameObject("SLbl", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferred;
            le.flexibleWidth  = flexible;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.color              = color;
            tmp.alignment          = align;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget      = false;
        }

        private TMP_InputField MakeInputField(Transform parent, string initialValue, float width)
        {
            GameObject go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            go.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f, 1f);

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.contentType = TMP_InputField.ContentType.IntegerNumber;

            GameObject areaGO = new GameObject("Text Area", typeof(RectTransform));
            areaGO.transform.SetParent(go.transform, false);
            RectTransform areaRT = areaGO.GetComponent<RectTransform>();
            areaRT.anchorMin = Vector2.zero; areaRT.anchorMax = Vector2.one;
            areaRT.offsetMin = new Vector2(4f, 2f); areaRT.offsetMax = new Vector2(-4f, -2f);
            areaGO.AddComponent<RectMask2D>();

            GameObject phGO = new GameObject("Placeholder", typeof(RectTransform));
            phGO.transform.SetParent(areaGO.transform, false);
            StretchFill(phGO.GetComponent<RectTransform>());
            TextMeshProUGUI phTMP = phGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) phTMP.font = FontAsset;
            phTMP.text = "0"; phTMP.fontSize = 11f;
            phTMP.color = new Color(0.4f, 0.4f, 0.4f);
            phTMP.alignment = TextAlignmentOptions.Center;
            phTMP.enableWordWrapping = false;

            GameObject txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(areaGO.transform, false);
            StretchFill(txtGO.GetComponent<RectTransform>());
            TextMeshProUGUI txtTMP = txtGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) txtTMP.font = FontAsset;
            txtTMP.text = initialValue; txtTMP.fontSize = 11f;
            txtTMP.color = Color.white;
            txtTMP.alignment = TextAlignmentOptions.Center;
            txtTMP.enableWordWrapping = false;

            field.textViewport  = areaRT;
            field.textComponent = txtTMP;
            field.placeholder   = phTMP;
            field.text          = initialValue;
            field.caretWidth    = 2;
            field.customCaretColor = true;
            field.caretColor    = Color.white;
            field.selectionColor = new Color(0.2f, 0.4f, 0.8f, 0.45f);
            field.interactable  = true;
            field.enabled = false;
            field.enabled = true;
            return field;
        }

        private void MakeResetButton(Transform parent, float width, Action onClick)
        {
            GameObject go = new GameObject("RSTBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.12f, 0.12f, 1f);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            StretchFill(labelGO.GetComponent<RectTransform>());
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text      = "RESET";
            tmp.fontSize  = 9f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = new Color(0.9f, 0.5f, 0.5f);
            tmp.raycastTarget = false;
        }

        private void AddSettingsDivider()
        {
            GameObject wrapper = new GameObject("SDividerWrapper", typeof(RectTransform));
            wrapper.transform.SetParent(SettingsContentGO.transform, false);
            wrapper.AddComponent<LayoutElement>().preferredHeight = 13f;

            GameObject line = new GameObject("SDivider", typeof(RectTransform));
            line.transform.SetParent(wrapper.transform, false);
            RectTransform lineRT = line.GetComponent<RectTransform>();
            lineRT.anchorMin        = new Vector2(0f, 0.5f);
            lineRT.anchorMax        = new Vector2(1f, 0.5f);
            lineRT.pivot            = new Vector2(0.5f, 0.5f);
            lineRT.offsetMin        = new Vector2(6f, 0f);
            lineRT.offsetMax        = new Vector2(-6f, 1f);
            line.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f, 1f);
        }

        private void AddSettingsGlobalSep()
        {
            GameObject wrapper = new GameObject("SGlobalSep", typeof(RectTransform));
            wrapper.transform.SetParent(SettingsContentGO.transform, false);
            wrapper.AddComponent<LayoutElement>().preferredHeight = 9f;

            GameObject line = new GameObject("SGlobalSepLine", typeof(RectTransform));
            line.transform.SetParent(wrapper.transform, false);
            RectTransform lineRT = line.GetComponent<RectTransform>();
            lineRT.anchorMin = new Vector2(0f, 0.5f);
            lineRT.anchorMax = new Vector2(1f, 0.5f);
            lineRT.pivot     = new Vector2(0.5f, 0.5f);
            lineRT.offsetMin = new Vector2(6f, 0f);
            lineRT.offsetMax = new Vector2(-6f, 1f);
            line.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
        }

        private void AddSettingsInfoBox(string text)
        {
            GameObject go = new GameObject("SInfoBox", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<Image>().color = new Color(0.10f, 0.14f, 0.18f, 1f);
            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(8, 8, 6, 6);

            GameObject textGO = new GameObject("SInfoText", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            TextMeshProUGUI lbl = textGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) lbl.font = FontAsset;
            lbl.text               = text;
            lbl.fontSize           = 9f;
            lbl.color              = new Color(0.6f, 0.6f, 0.6f);
            lbl.enableWordWrapping = true;
            lbl.overflowMode       = TextOverflowModes.Overflow;
            lbl.alignment          = TextAlignmentOptions.TopLeft;
            lbl.raycastTarget      = false;

            // Centered dismiss button row
            GameObject btnRow = new GameObject("SInfoBtnRow", typeof(RectTransform));
            btnRow.transform.SetParent(go.transform, false);
            HorizontalLayoutGroup bhlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            bhlg.childControlWidth = true; bhlg.childForceExpandWidth = false;
            bhlg.childControlHeight = true; bhlg.childForceExpandHeight = true;
            bhlg.spacing = 0f; bhlg.padding = new RectOffset(0, 0, 4, 2);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 22f;

            // flex spacers to center the button
            GameObject leftFlex = new GameObject("L", typeof(RectTransform));
            leftFlex.transform.SetParent(btnRow.transform, false);
            leftFlex.AddComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject btnGO = new GameObject("DismissBtn", typeof(RectTransform));
            btnGO.transform.SetParent(btnRow.transform, false);
            btnGO.AddComponent<LayoutElement>().preferredWidth = 80f;
            Image btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(0.20f, 0.35f, 0.50f, 1f);
            Button btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => { Config.DismissInfo(); RebuildSettingsContent(); });

            GameObject btnLbl = new GameObject("Label", typeof(RectTransform));
            btnLbl.transform.SetParent(btnGO.transform, false);
            StretchFill(btnLbl.GetComponent<RectTransform>());
            TextMeshProUGUI btnTmp = btnLbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) btnTmp.font = FontAsset;
            btnTmp.text = "GOT IT";
            btnTmp.fontSize = 9f;
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.color = new Color(0.55f, 0.75f, 0.95f);
            btnTmp.raycastTarget = false;

            GameObject rightFlex = new GameObject("R", typeof(RectTransform));
            rightFlex.transform.SetParent(btnRow.transform, false);
            rightFlex.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // bottom margin
            GameObject spacer = new GameObject("SInfoSpacer", typeof(RectTransform));
            spacer.transform.SetParent(SettingsContentGO.transform, false);
            spacer.AddComponent<LayoutElement>().preferredHeight = 4f;
        }

        private void AddSettingsMessage(string text)
        {
            GameObject go = new GameObject("SMsg", typeof(RectTransform));
            go.transform.SetParent(SettingsContentGO.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.color              = new Color(0.55f, 0.55f, 0.55f);
            tmp.enableWordWrapping = false;
            tmp.alignment          = TextAlignmentOptions.MidlineLeft;
            tmp.margin             = new Vector4(6, 0, 6, 0);
            tmp.raycastTarget      = false;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        }

        // ── Status tab ────────────────────────────────────────────────────────

        internal void RefreshRows()
        {
            try
            {
                if (ContentParent == null) { TrackerLog.LogError("[PT] ContentParent null"); return; }
                EnsureHeaderRows();

                Company player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
                var allObjects = player != null
                    ? MonoBehaviourSingleton<ObjectInfoManager>.Instance?.allObjectInfos : null;

                if (player == null || allObjects == null)
                {
                    SyncRows(new List<BodyData>());
                    _emptyMsgLbl.text = player == null ? "Not in game yet." : "Loading game data...";
                    _emptyMsgGO.SetActive(true);
                    _titleLbl.text = "POWER STATUS";
                    LayoutRebuilder.ForceRebuildLayoutImmediate(ContentParent as RectTransform);
                    return;
                }

                var bodies = new List<BodyData>();
                foreach (ObjectInfo oi in allObjects)
                {
                    ObjectInfoData data = oi.GetObjectInfoData(player);
                    if (data == null) continue;

                    double gen     = data.EnergyProduction;
                    double cons    = data.EnergyConsumptionMax;
                    double battery = data.EnergyInBattery;
                    double maxBat  = data.EnergyMaxCapacityBattery;
                    if (gen <= 0 && cons <= 0 && maxBat <= 0) continue;

                    // Collect per-facility fuel days
                    var facilityFuelDays = new List<ValueTuple<string, double>>();
                    if (data.ListFacility != null)
                    {
                        foreach (var fac in data.ListFacility)
                        {
                            if (!(fac is EnergyProductionFacility) && !(fac is EnergyProductionModule)) continue;
                            if (fac.Enabled <= 0) continue;
                            var inp = fac.facilityDescriptor?.energyProductionData?.input;
                            if (inp == null || inp.Length == 0) continue;
                            var res = inp[0].resource;
                            if (res == null) continue;
                            try
                            {
                                var rb  = fac.GetResourceBalance(forceEstimation: true);
                                var bal = rb?.Select(MyExtensions.ResourceStorageType.Storage);
                                if (bal == null || !bal.TryGetValue(res, out double r)) continue;
                                double rate = Math.Abs(r);
                                if (rate <= 0) continue;
                                double days = data.CheckResources(res) / rate;
                                facilityFuelDays.Add(new ValueTuple<string, double>(fac.Name ?? "?", days));
                            }
                            catch { }
                        }
                    }

                    bodies.Add(new BodyData
                    {
                        OI               = oi,
                        Data             = data,
                        Gen              = gen,
                        Cons             = cons,
                        Balance          = gen - cons,
                        Battery          = battery,
                        MaxBat           = maxBat,
                        FacilityFuelDays = facilityFuelDays,
                        HasFacilities    = HasEnergyFacilities(data),
                    });
                }

                // Build receiver-to-source-body map (used by GetFacilityInfos)
                _receiverSources = new Dictionary<EnergyReceiverFacility, string>();
                foreach (var bd in bodies)
                {
                    if (bd.Data?.ListFacility == null) continue;
                    foreach (Facility fac in bd.Data.ListFacility)
                    {
                        if (!(fac is EnergyTransferFacility xmit) || xmit.Enabled <= 0) continue;
                        foreach (var t in xmit.Targets)
                        {
                            if (t.receiverFacility != null)
                            {
                                _receiverSources[t.receiverFacility] = bd.OI.ObjectName;
                            }
                            else
                            {
                                ObjectInfo tgt = t.target?.Object;
                                if (tgt == null) continue;
                                string tName = tgt.ObjectName;
                                var tBody = bodies.FirstOrDefault(x => x.OI.ObjectName == tName);
                                if (tBody.Data?.ListFacility != null)
                                    foreach (Facility tf in tBody.Data.ListFacility)
                                        if (tf is EnergyReceiverFacility rf && !_receiverSources.ContainsKey(rf))
                                            _receiverSources[rf] = bd.OI.ObjectName;
                            }
                        }
                    }
                }

                bodies.Sort((a, b) =>
                {
                    bool aOK = a.Balance >= 0;
                    bool bOK = b.Balance >= 0;
                    if (aOK != bOK) return aOK ? 1 : -1;
                    if (!aOK && !bOK) return a.Balance.CompareTo(b.Balance);
                    return string.Compare(a.OI.ObjectName, b.OI.ObjectName, StringComparison.Ordinal);
                });

                // Populate lookup tables for settings icons (cheaply, no resource queries)
                _lastBodyFacilityNames = new Dictionary<string, List<string>>();
                _lastBodySprites       = new Dictionary<string, string>();
                _lastFacilitySpriteIds = new Dictionary<string, string>();
                foreach (var b in bodies)
                {
                    _lastBodySprites[b.OI.ObjectName] = b.OI.ImagePlanetUI?.name ?? "";

                    var facNames = new List<string>();
                    if (b.Data?.ListFacility != null)
                    {
                        foreach (var fac in b.Data.ListFacility)
                        {
                            if (!(fac is EnergyProductionFacility) && !(fac is EnergyProductionModule)) continue;
                            if (fac.Enabled <= 0) continue;
                            var inp = fac.facilityDescriptor?.energyProductionData?.input;
                            if (inp == null || inp.Length == 0) continue;
                            if (inp[0].resource == null) continue;
                            if (!string.IsNullOrEmpty(fac.Name) && !facNames.Contains(fac.Name))
                            {
                                facNames.Add(fac.Name);
                                if (!_lastFacilitySpriteIds.ContainsKey(fac.Name))
                                {
                                    try
                                    {
                                        string sid = fac.facilityDescriptor?.SpriteId;
                                        if (!string.IsNullOrEmpty(sid)) _lastFacilitySpriteIds[fac.Name] = sid;
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    _lastBodyFacilityNames[b.OI.ObjectName] = facNames;
                }

                int count = bodies.Count;
                _titleLbl.text = $"POWER STATUS  ({count} {(count == 1 ? "body" : "bodies")})";

                bool changed = SyncRows(bodies);

                bool empty = bodies.Count == 0;
                if (_emptyMsgGO.activeSelf != empty) { _emptyMsgGO.SetActive(empty); changed = true; }
                if (empty) _emptyMsgLbl.text = "No bodies with power infrastructure found.";

                ReorderContent(bodies);
                if (changed) LayoutRebuilder.ForceRebuildLayoutImmediate(ContentParent as RectTransform);

                Severity overall = Severity.OK;
                foreach (var b in bodies) { Severity s = GetSeverity(b); if (s > overall) overall = s; }
                UpdateIndicatorSeverity(overall);
            }
            catch (Exception e) { TrackerLog.LogError($"[PT] RefreshRows exception: {e}"); }
        }

        private Severity GetSeverity(BodyData b)
        {
            var (warnPct, critPct) = Config.GetBalanceThresholds(b.OI.ObjectName);
            Severity energySev = Severity.OK;
            if (b.Balance < 0)
            {
                double defPct = b.Cons > 0 ? -b.Balance / b.Cons * 100.0 : 100.0;
                energySev = defPct > critPct ? Severity.Critical
                          : defPct > warnPct ? Severity.Warning : Severity.OK;
            }

            Severity fuelSev = Severity.OK;
            if (b.FacilityFuelDays != null)
            {
                foreach (var item in b.FacilityFuelDays)
                {
                    var (wDays, cDays) = Config.GetFuelThresholds(b.OI.ObjectName, item.Item1);
                    Severity s = item.Item2 < cDays ? Severity.Critical
                               : item.Item2 < wDays ? Severity.Warning : Severity.OK;
                    if (s > fuelSev) fuelSev = s;
                }
            }
            return energySev > fuelSev ? energySev : fuelSev;
        }

        private void EnsureHeaderRows()
        {
            if (_headerBuilt) return;
            _headerBuilt = true;

            _titleRowGO = new GameObject("TitleRow", typeof(RectTransform));
            _titleRowGO.transform.SetParent(ContentParent, false);
            _titleRowGO.AddComponent<LayoutElement>().preferredHeight = 26f;
            _titleLbl = _titleRowGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) _titleLbl.font = FontAsset;
            _titleLbl.fontSize = 12f; _titleLbl.fontStyle = FontStyles.Bold;
            _titleLbl.color = Color.white; _titleLbl.enableWordWrapping = false;
            _titleLbl.alignment = TextAlignmentOptions.MidlineLeft;
            _titleLbl.margin = new Vector4(6, 4, 6, 0);

            _titleSepGO = MakeSepLine("TitleSep");
            (_headerRowGO, _headerSepGO) = AddMainHeaderRow();

            _emptyMsgGO = new GameObject("MsgRow", typeof(RectTransform));
            _emptyMsgGO.transform.SetParent(ContentParent, false);
            _emptyMsgGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            _emptyMsgLbl = _emptyMsgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) _emptyMsgLbl.font = FontAsset;
            _emptyMsgLbl.fontSize = 11f; _emptyMsgLbl.color = new Color(0.65f, 0.65f, 0.65f);
            _emptyMsgLbl.enableWordWrapping = false;
            _emptyMsgLbl.alignment = TextAlignmentOptions.MidlineLeft;
            _emptyMsgLbl.margin = new Vector4(6, 0, 6, 0);
            _emptyMsgGO.SetActive(false);
        }

        private bool SyncRows(List<BodyData> bodies)
        {
            bool changed = false;
            var activeNames = new HashSet<string>(bodies.Select(b => b.OI.ObjectName));

            var stale = _rowCache.Keys.Where(k => !activeNames.Contains(k)).ToList();
            foreach (var key in stale)
            {
                var c = _rowCache[key];
                foreach (var fr in c.FacilityRows)
                {
                    if (fr.TransferBoxGO != null)
                    {
                        foreach (var lr in fr.LinkRows) UnityEngine.Object.DestroyImmediate(lr.GO);
                        UnityEngine.Object.DestroyImmediate(fr.TransferBoxGO);
                    }
                    UnityEngine.Object.DestroyImmediate(fr.GO);
                }
                if (c.TopSepGO       != null) UnityEngine.Object.DestroyImmediate(c.TopSepGO);
                if (c.SubHeaderRowGO != null) UnityEngine.Object.DestroyImmediate(c.SubHeaderRowGO);
                if (c.SubHeaderSepGO != null) UnityEngine.Object.DestroyImmediate(c.SubHeaderSepGO);
                if (c.BotSepGO       != null) UnityEngine.Object.DestroyImmediate(c.BotSepGO);
                UnityEngine.Object.DestroyImmediate(c.GO);
                _rowCache.Remove(key);
                changed = true;
            }

            foreach (var b in bodies)
            {
                if (!_rowCache.TryGetValue(b.OI.ObjectName, out var cache))
                {
                    _rowCache[b.OI.ObjectName] = CreateRow(b);
                    changed = true;
                }
                else UpdateRow(cache, b);

                SyncFacilityRows(_rowCache[b.OI.ObjectName], b);
                if (_rowCache[b.OI.ObjectName].Expanded) changed = true;
            }
            return changed;
        }

        private void SyncFacilityRows(BodyRowCache bodyCache, BodyData b)
        {
            bool exp = bodyCache.Expanded;
            if (bodyCache.TopSepGO       != null) bodyCache.TopSepGO.SetActive(exp);
            if (bodyCache.SubHeaderRowGO != null) bodyCache.SubHeaderRowGO.SetActive(exp);
            if (bodyCache.SubHeaderSepGO != null) bodyCache.SubHeaderSepGO.SetActive(exp);
            if (bodyCache.BotSepGO       != null) bodyCache.BotSepGO.SetActive(exp);

            if (!exp)
            {
                foreach (var fr in bodyCache.FacilityRows)
                {
                    fr.GO.SetActive(false);
                    if (fr.TransferBoxGO != null) fr.TransferBoxGO.SetActive(false);
                }
                return;
            }

            var infos = GetFacilityInfos(b.Data);

            while (bodyCache.FacilityRows.Count > infos.Count)
            {
                int last = bodyCache.FacilityRows.Count - 1;
                var stale = bodyCache.FacilityRows[last];
                if (stale.TransferBoxGO != null)
                {
                    foreach (var lr in stale.LinkRows) UnityEngine.Object.DestroyImmediate(lr.GO);
                    UnityEngine.Object.DestroyImmediate(stale.TransferBoxGO);
                }
                UnityEngine.Object.DestroyImmediate(stale.GO);
                bodyCache.FacilityRows.RemoveAt(last);
            }
            while (bodyCache.FacilityRows.Count < infos.Count)
                bodyCache.FacilityRows.Add(CreateFacilityRow());

            for (int i = 0; i < infos.Count; i++)
            {
                var fr = bodyCache.FacilityRows[i];
                UpdateFacilityRow(fr, infos[i], b.OI.ObjectName);
                fr.GO.SetActive(true);

                if (infos[i].IsTransfer)
                {
                    var links = infos[i].TransferLinks ?? new List<TransferLink>();
                    bool subExp = fr.TransferExpanded;
                    bool isXmit = infos[i].Fac is EnergyTransferFacility;

                    // Create box on first use (or if xmit/recv type changed)
                    if (links.Count > 0 && (fr.TransferBoxGO == null || fr.IsXmitBox != isXmit))
                    {
                        if (fr.TransferBoxGO != null)
                        {
                            foreach (var lr in fr.LinkRows) UnityEngine.Object.DestroyImmediate(lr.GO);
                            fr.LinkRows.Clear();
                            UnityEngine.Object.DestroyImmediate(fr.TransferBoxGO);
                        }
                        fr.TransferBoxGO = CreateTransferBox(isXmit);
                        fr.IsXmitBox = isXmit;
                    }

                    if (fr.TransferBoxGO != null)
                    {
                        Transform boxT = fr.TransferBoxGO.transform.GetChild(1); // child(1) = inner dark box
                        while (fr.LinkRows.Count > links.Count)
                        {
                            int last = fr.LinkRows.Count - 1;
                            UnityEngine.Object.DestroyImmediate(fr.LinkRows[last].GO);
                            fr.LinkRows.RemoveAt(last);
                        }
                        while (fr.LinkRows.Count < links.Count)
                            fr.LinkRows.Add(CreateTransferLinkRow(boxT));

                        for (int j = 0; j < links.Count; j++)
                            UpdateTransferLinkRow(fr.LinkRows[j], links[j]);

                        fr.TransferBoxGO.SetActive(subExp);
                    }
                }
                else if (fr.TransferBoxGO != null)
                {
                    foreach (var lr in fr.LinkRows) UnityEngine.Object.DestroyImmediate(lr.GO);
                    fr.LinkRows.Clear();
                    UnityEngine.Object.DestroyImmediate(fr.TransferBoxGO);
                    fr.TransferBoxGO = null;
                }
            }
        }

        private static bool HasEnergyFacilities(ObjectInfoData data)
        {
            if (data?.ListFacility == null) return false;
            foreach (var fac in data.ListFacility)
            {
                if (!(fac is EnergyProductionFacility) && !(fac is EnergyProductionModule)) continue;
                if (fac.Quantity <= 0) continue;
                if (fac.facilityDescriptor?.energyProductionData == null) continue;
                if (fac.facilityDescriptor.energyProductionData.energyProduction > 0) return true;
                if (fac is EnergyTransferFacility || fac is EnergyReceiverFacility) return true;
            }
            return false;
        }

        private List<FacilityInfo> GetFacilityInfos(ObjectInfoData data)
        {
            var result = new List<FacilityInfo>();
            if (data?.ListFacility == null) return result;

            foreach (var fac in data.ListFacility)
            {
                if (!(fac is EnergyProductionFacility) && !(fac is EnergyProductionModule)) continue;
                if (fac.Quantity <= 0) continue;
                if (fac.facilityDescriptor?.energyProductionData == null) continue;

                bool isTransfer = fac is EnergyTransferFacility || fac is EnergyReceiverFacility;
                if (!isTransfer && fac.facilityDescriptor.energyProductionData.energyProduction <= 0) continue;

                double gen;
                if (fac is EnergyTransferFacility xmit2)
                    gen = xmit2.PowerTransfered;
                else
                    gen = fac is EnergyProductionFacility epf ? epf.EnergyProduction
                        : fac is EnergyProductionModule   epm ? epm.EnergyProduction : 0.0;

                var inputArr = fac.facilityDescriptor.energyProductionData.input;
                ResourceDefinition primaryRes = (!isTransfer && inputArr != null && inputArr.Length > 0)
                    ? inputArr[0].resource : null;

                double consRate = 0;
                if (primaryRes != null)
                {
                    try
                    {
                        var rb  = fac.GetResourceBalance(forceEstimation: true);
                        var bal = rb?.Select(MyExtensions.ResourceStorageType.Storage);
                        if (bal != null && bal.TryGetValue(primaryRes, out double r))
                            consRate = Math.Abs(r);
                    }
                    catch { }
                }

                double stock = primaryRes != null ? data.CheckResources(primaryRes) : 0;

                string spriteId = null;
                try { if (fac.facilityDescriptor?.Sprite != null) spriteId = fac.facilityDescriptor.SpriteId; }
                catch { }

                List<TransferLink> transferLinks = null;
                if (fac is EnergyTransferFacility xmit3)
                {
                    transferLinks = new List<TransferLink>();
                    foreach (var t in xmit3.Targets)
                    {
                        string tName = t.target?.Object?.ObjectName;
                        if (tName == null) continue;
                        double pwr = xmit3.PowerTransfered * (double)t.ratio;
                        transferLinks.Add(new TransferLink { IsXmit = true, PeerName = tName, Power = pwr, Ratio = t.ratio });
                    }
                }
                else if (fac is EnergyReceiverFacility recv)
                {
                    transferLinks = new List<TransferLink>();
                    string src;
                    if (!_receiverSources.TryGetValue(recv, out src)) src = "?";
                    transferLinks.Add(new TransferLink { IsXmit = false, PeerName = src, Power = recv.EnergyProduction, Ratio = 0f });
                }

                result.Add(new FacilityInfo
                {
                    Fac             = fac,
                    SpriteId        = spriteId,
                    Gen             = gen,
                    PrimaryResource = primaryRes,
                    ConsRatePerDay  = consRate,
                    ResourceStock   = stock,
                    Workers         = fac.HaveWorkers,
                    WorkersNeeded   = fac.TotalWorkersNeeded,
                    TotalQty        = fac.Quantity,
                    IsTransfer      = isTransfer,
                    TransferLinks   = transferLinks,
                });
            }
            return result;
        }

        private void ReorderContent(List<BodyData> bodies)
        {
            int idx = 0;
            _titleRowGO.transform.SetSiblingIndex(idx++);
            _titleSepGO.transform.SetSiblingIndex(idx++);
            _headerRowGO.transform.SetSiblingIndex(idx++);
            _headerSepGO.transform.SetSiblingIndex(idx++);
            foreach (var b in bodies)
            {
                if (!_rowCache.TryGetValue(b.OI.ObjectName, out var row)) continue;
                row.GO.transform.SetSiblingIndex(idx++);
                if (row.TopSepGO       != null) row.TopSepGO.transform.SetSiblingIndex(idx++);
                if (row.SubHeaderRowGO != null) row.SubHeaderRowGO.transform.SetSiblingIndex(idx++);
                if (row.SubHeaderSepGO != null) row.SubHeaderSepGO.transform.SetSiblingIndex(idx++);
                foreach (var fr in row.FacilityRows)
                {
                    fr.GO.transform.SetSiblingIndex(idx++);
                    if (fr.TransferBoxGO != null) fr.TransferBoxGO.transform.SetSiblingIndex(idx++);
                }
                if (row.BotSepGO != null) row.BotSepGO.transform.SetSiblingIndex(idx++);
            }
            _emptyMsgGO.transform.SetSiblingIndex(idx++);
        }

        private BodyRowCache CreateRow(BodyData b)
        {
            GameObject rowGO = MakeRowContainer($"Row_{b.OI.ObjectName}", 22f, topPad: 2, botPad: 2);
            rowGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            GameObject topSep = MakeSepLine("TopSep");
            topSep.SetActive(false);

            GameObject subHeader = MakeSubHeaderRow();
            subHeader.SetActive(false);

            GameObject subHeaderSep = MakeSepLine("SubHeaderSep");
            subHeaderSep.SetActive(false);

            GameObject botSep = MakeSepLine("BotSep");
            botSep.SetActive(false);

            var cache = new BodyRowCache
            {
                GO             = rowGO,
                TopSepGO       = topSep,
                SubHeaderRowGO = subHeader,
                SubHeaderSepGO = subHeaderSep,
                BotSepGO       = botSep,
                NameCol        = AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 120f),
                GenCol         = AddColumn(rowGO.transform, ColGen,     0f, TextAlignmentOptions.MidlineRight, ""),
                ConsCol        = AddColumn(rowGO.transform, ColCons,    0f, TextAlignmentOptions.MidlineRight, ""),
                BalanceCol     = AddColumn(rowGO.transform, ColBalance, 0f, TextAlignmentOptions.MidlineRight, ""),
                BatteryCol     = AddColumn(rowGO.transform, ColBattery, 0f, TextAlignmentOptions.MidlineRight, ""),
                Btn            = rowGO.AddComponent<Button>(),
            };
            cache.Btn.onClick.AddListener(() => { if (cache.HasFacilities) { cache.Expanded = !cache.Expanded; RefreshRows(); } });
            UpdateRow(cache, b);
            return cache;
        }

        private FacilityRowCache CreateFacilityRow()
        {
            GameObject rowGO = MakeRowContainer("FacilityRow", 22f, topPad: 1, botPad: 1);
            Image img = rowGO.AddComponent<Image>();
            img.color = new Color(0.06f, 0.08f, 0.11f, 1f);
            Button btn = rowGO.AddComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = img;
            return new FacilityRowCache
            {
                GO         = rowGO,
                Btn        = btn,
                NameCol    = AddSubColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 90f),
                GenCol     = AddSubColumn(rowGO.transform, SubColGen,     0f, TextAlignmentOptions.MidlineRight, ""),
                FuelCol    = AddSubColumn(rowGO.transform, SubColFuel,    0f, TextAlignmentOptions.MidlineRight, ""),
                WorkersCol = AddSubColumn(rowGO.transform, SubColWorkers, 0f, TextAlignmentOptions.MidlineRight, ""),
                SupplyCol  = AddSubColumn(rowGO.transform, SubColSupply,  0f, TextAlignmentOptions.MidlineRight, ""),
                StockCol   = AddSubColumn(rowGO.transform, SubColStock,   0f, TextAlignmentOptions.MidlineRight, ""),
            };
        }

        private const float TransferRatioCol = 40f;

        private GameObject CreateTransferBox(bool isXmit)
        {
            // Wrapper is the ContentParent child — gives the left indent for visual hierarchy.
            // child(0) = spacer, child(1) = dark box (where link rows are added).
            GameObject wrapperGO = new GameObject("TransferBox", typeof(RectTransform));
            wrapperGO.transform.SetParent(ContentParent, false);
            HorizontalLayoutGroup whlg = wrapperGO.AddComponent<HorizontalLayoutGroup>();
            whlg.childControlWidth = true; whlg.childForceExpandWidth = false;
            whlg.childControlHeight = true; whlg.childForceExpandHeight = true;
            whlg.spacing = 0f; whlg.padding = new RectOffset(0, 0, 0, 0);
            ContentSizeFitter wcf = wrapperGO.AddComponent<ContentSizeFitter>();
            wcf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Left indent spacer
            GameObject spacer = new GameObject("Indent", typeof(RectTransform));
            spacer.transform.SetParent(wrapperGO.transform, false);
            LayoutElement sle = spacer.AddComponent<LayoutElement>();
            sle.minWidth = 32f; sle.preferredWidth = 32f; sle.flexibleWidth = 0f;

            // Dark box
            GameObject boxGO = new GameObject("Box", typeof(RectTransform));
            boxGO.transform.SetParent(wrapperGO.transform, false);
            boxGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            boxGO.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 1f);

            VerticalLayoutGroup vlg = boxGO.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;  vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;  vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset(6, 6, 2, 3);

            ContentSizeFitter csf = boxGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Header row
            GameObject hdrGO = new GameObject("BoxHdr", typeof(RectTransform));
            hdrGO.transform.SetParent(boxGO.transform, false);
            hdrGO.AddComponent<LayoutElement>().preferredHeight = 16f;
            HorizontalLayoutGroup hhlg = hdrGO.AddComponent<HorizontalLayoutGroup>();
            hhlg.childControlWidth = true; hhlg.childForceExpandWidth = false;
            hhlg.childControlHeight = true; hhlg.childForceExpandHeight = true;
            hhlg.spacing = 4f;

            Color hc = new Color(0.38f, 0.38f, 0.38f);
            AddSubColumn(hdrGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "BODY", 90f).color = hc;
            AddSubColumn(hdrGO.transform, TransferRatioCol, 0f, TextAlignmentOptions.MidlineRight,
                isXmit ? "RATIO" : "").color = hc;
            AddSubColumn(hdrGO.transform, SubColGen, 0f, TextAlignmentOptions.MidlineRight, "POWER").color = hc;

            return wrapperGO;
        }

        private TransferRowCache CreateTransferLinkRow(Transform parent)
        {
            GameObject rowGO = new GameObject("LinkRow", typeof(RectTransform));
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 18f;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.spacing = 4f;

            return new TransferRowCache
            {
                GO       = rowGO,
                BodyCol  = AddSubColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 90f),
                RatioCol = AddSubColumn(rowGO.transform, TransferRatioCol, 0f, TextAlignmentOptions.MidlineRight, ""),
                PowerCol = AddSubColumn(rowGO.transform, SubColGen, 0f, TextAlignmentOptions.MidlineRight, ""),
            };
        }

        private void UpdateTransferLinkRow(TransferRowCache row, TransferLink link)
        {
            string bodyIcon = "";
            string sp;
            if (_lastBodySprites.TryGetValue(link.PeerName, out sp) && !string.IsNullOrEmpty(sp))
                bodyIcon = $"<sprite name={sp}> ";

            string arrow = link.IsXmit ? "<color=#5599BB>→</color>" : "<color=#55BB88>←</color>";
            row.BodyCol.text  = $"{arrow} {bodyIcon}{link.PeerName}";
            row.BodyCol.color = new Color(0.75f, 0.75f, 0.75f);

            row.RatioCol.text  = link.IsXmit && link.Ratio > 0
                ? $"<color=#888888>{link.Ratio * 100f:0}%</color>"
                : $"{U}—{UE}";
            row.RatioCol.color = Color.white;

            row.PowerCol.text = link.IsXmit
                ? (link.Power > 0 ? $"<color=#5599BB>-{FormatPowerRaw(link.Power)}</color>" : $"{U}—{UE}")
                : (link.Power > 0 ? $"<color=#55BB88>+{FormatPowerRaw(link.Power)}</color>" : $"{U}—{UE}");
            row.PowerCol.color = Color.white;
        }

        private void UpdateRow(BodyRowCache cache, BodyData b)
        {
            Severity sev  = GetSeverity(b);
            string dotHex = sev == Severity.Critical ? "#FF3333"
                : sev == Severity.Warning ? "#FF9900" : "#44BB44";
            cache.HasFacilities = b.HasFacilities;
            string spriteName = b.OI.ImagePlanetUI?.name ?? "";
            string icon       = spriteName.Length > 0 ? $"<sprite name={spriteName}> " : "";
            string toggle     = !b.HasFacilities ? "  "
                : cache.Expanded ? "<color=#888888>-</color> " : "<color=#888888>+</color> ";

            cache.NameCol.text  = $"{toggle}<color={dotHex}>●</color>  {icon}{b.OI.ObjectName}";
            cache.NameCol.color = Color.white;

            cache.GenCol.text   = FormatPower(b.Gen);
            cache.GenCol.color  = new Color(0.85f, 0.85f, 0.85f);
            cache.ConsCol.text  = FormatPower(b.Cons);
            cache.ConsCol.color = new Color(0.85f, 0.85f, 0.85f);

            if (b.Balance >= 0)
                cache.BalanceCol.text = $"<color=#44BB44>+{FormatPowerRaw(b.Balance)}</color>";
            else
            {
                string balHex = sev == Severity.Critical ? "#FF3333" : "#FF9900";
                cache.BalanceCol.text = $"<color={balHex}>{FormatPowerRaw(b.Balance)}</color>";
            }
            cache.BalanceCol.color = Color.white;

            cache.BatteryCol.text  = FormatBattery(b.Battery, b.MaxBat);
            cache.BatteryCol.color = new Color(0.85f, 0.85f, 0.85f);
        }

        private void UpdateFacilityRow(FacilityRowCache fr, FacilityInfo fi, string bodyName)
        {
            fr.IsTransfer = fi.IsTransfer;
            fr.Btn.onClick.RemoveAllListeners();

            // Name + icon
            bool disabled = fi.Fac.Enabled <= 0;
            string facIcon = !string.IsNullOrEmpty(fi.SpriteId)
                ? $"<sprite name=\"{fi.SpriteId}\" color={(disabled ? "#555" : "white")}> " : "";

            if (fi.IsTransfer)
            {
                bool hasLinks = fi.TransferLinks != null && fi.TransferLinks.Count > 0;
                string toggle = !hasLinks ? "  "
                    : fr.TransferExpanded ? "<color=#888888>-</color> " : "<color=#888888>+</color> ";
                bool isXmit = fi.Fac is EnergyTransferFacility;
                string arrow = isXmit ? "<color=#5599BB>→</color>" : "<color=#55BB88>←</color>";
                fr.NameCol.text  = $"   {toggle}{arrow} {facIcon}{fi.Fac.Name ?? "?"}";
                fr.NameCol.color = disabled ? new Color(0.40f, 0.40f, 0.40f) : new Color(0.70f, 0.70f, 0.70f);
                fr.GenCol.text   = isXmit
                    ? (fi.Gen > 0 ? $"<color=#5599BB>-{FormatPowerRaw(fi.Gen)}</color>" : $"{U}—{UE}")
                    : (fi.Gen > 0 ? $"<color=#55BB88>+{FormatPowerRaw(fi.Gen)}</color>" : $"{U}—{UE}");
                fr.GenCol.color  = Color.white;
                fr.FuelCol.text    = $"{U}—{UE}"; fr.FuelCol.color    = Color.white;
                fr.WorkersCol.text = fi.WorkersNeeded > 0
                    ? (fi.Workers < fi.WorkersNeeded
                        ? $"<color=#FF9900>{fi.Workers}</color>{U}/{fi.WorkersNeeded}{UE}"
                        : $"<color=#44BB44>{fi.Workers}</color>{U}/{fi.WorkersNeeded}{UE}")
                    : $"{U}—{UE}";
                fr.WorkersCol.color = Color.white;
                fr.SupplyCol.text  = $"{U}—{UE}"; fr.SupplyCol.color  = Color.white;
                fr.StockCol.text   = $"{U}—{UE}"; fr.StockCol.color   = Color.white;
                if (hasLinks)
                {
                    var frRef = fr;
                    fr.Btn.onClick.AddListener(() => { frRef.TransferExpanded = !frRef.TransferExpanded; RefreshRows(); });
                }
                return;
            }

            string qty;
            if (fi.Fac.Enabled < fi.TotalQty)
                qty = $" <color=#666>({fi.Fac.Enabled}/{fi.TotalQty})</color>";
            else if (fi.TotalQty > 1)
                qty = $" <color=#555>x{fi.TotalQty}</color>";
            else
                qty = "";
            fr.NameCol.text  = $"   {facIcon}{fi.Fac.Name ?? "?"}{qty}";
            fr.NameCol.color = disabled ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.75f, 0.75f, 0.75f);

            // GEN/DAY
            fr.GenCol.text  = FormatPower(fi.Gen);
            fr.GenCol.color = new Color(0.75f, 0.75f, 0.75f);

            // FUEL/DAY
            if (fi.PrimaryResource != null && fi.ConsRatePerDay > 0)
            {
                string resIcon = $"<sprite index={fi.PrimaryResource.IdSpritAttlastextMeshPro} color=white>";
                fr.FuelCol.text  = FormatTons(fi.ConsRatePerDay) + resIcon;
                fr.FuelCol.color = Color.white;
            }
            else
            {
                fr.FuelCol.text  = $"{U}—{UE}";
                fr.FuelCol.color = Color.white;
            }

            // WORKERS
            if (fi.WorkersNeeded > 0)
            {
                bool under = fi.Workers < fi.WorkersNeeded;
                string wColor = under ? "#FF9900" : "#44BB44";
                fr.WorkersCol.text = $"<color={wColor}>{fi.Workers}</color>{U}/{fi.WorkersNeeded}{UE}";
            }
            else
            {
                fr.WorkersCol.text = $"{U}—{UE}";
            }
            fr.WorkersCol.color = Color.white;

            // SUPPLY (days of fuel remaining)
            if (fi.PrimaryResource != null && fi.ConsRatePerDay > 0)
            {
                double days = fi.ResourceStock / fi.ConsRatePerDay;
                var (wDays, cDays) = Config.GetFuelThresholds(bodyName, fi.Fac.Name ?? "?");
                string dColor = days < cDays ? "#FF3333" : days < wDays ? "#FF9900" : "#44BB44";
                fr.SupplyCol.text  = $"<color={dColor}>{FormatDays(days)}</color>";
                fr.SupplyCol.color = Color.white;
            }
            else
            {
                fr.SupplyCol.text  = $"{U}—{UE}";
                fr.SupplyCol.color = Color.white;
            }

            // STOCK (total resource on body)
            if (fi.PrimaryResource != null)
            {
                string resIcon = $"<sprite index={fi.PrimaryResource.IdSpritAttlastextMeshPro} color=white>";
                fr.StockCol.text  = FormatTons(fi.ResourceStock) + resIcon;
                fr.StockCol.color = new Color(0.75f, 0.75f, 0.75f);
            }
            else
            {
                fr.StockCol.text  = $"{U}—{UE}";
                fr.StockCol.color = Color.white;
            }
        }

        private (GameObject row, GameObject sep) AddMainHeaderRow()
        {
            GameObject rowGO = MakeRowContainer("HeaderRow", 20f);
            Color hc = new Color(0.55f, 0.55f, 0.55f);
            AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft,  "BODY",    120f).color = hc;
            AddColumn(rowGO.transform, ColGen,     0f, TextAlignmentOptions.MidlineRight, "GEN/DAY").color  = hc;
            AddColumn(rowGO.transform, ColCons,    0f, TextAlignmentOptions.MidlineRight, "CONS/DAY").color = hc;
            AddColumn(rowGO.transform, ColBalance, 0f, TextAlignmentOptions.MidlineRight, "BALANCE").color  = hc;
            AddColumn(rowGO.transform, ColBattery, 0f, TextAlignmentOptions.MidlineRight, "BATTERY").color  = hc;

            GameObject sep = MakeSepLine("MainHeaderSep");
            return (rowGO, sep);
        }

        private GameObject MakeSubHeaderRow()
        {
            GameObject rowGO = MakeRowContainer("SubHeader", 18f);
            rowGO.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f, 1f);
            Color hc = new Color(0.38f, 0.38f, 0.38f);
            AddSubColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft,  "FACILITY", 90f).color  = hc;
            AddSubColumn(rowGO.transform, SubColGen,     0f, TextAlignmentOptions.MidlineRight, "GEN/DAY").color  = hc;
            AddSubColumn(rowGO.transform, SubColFuel,    0f, TextAlignmentOptions.MidlineRight, "FUEL/DAY").color = hc;
            AddSubColumn(rowGO.transform, SubColWorkers, 0f, TextAlignmentOptions.MidlineRight, "WORKERS").color  = hc;
            AddSubColumn(rowGO.transform, SubColSupply,  0f, TextAlignmentOptions.MidlineRight, "SUPPLY").color   = hc;
            AddSubColumn(rowGO.transform, SubColStock,   0f, TextAlignmentOptions.MidlineRight, "STOCK").color    = hc;
            return rowGO;
        }

        private GameObject MakeSepLine(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(ContentParent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            return go;
        }

        private GameObject MakeRowContainer(string name, float height, int topPad = 0, int botPad = 0)
        {
            GameObject rowGO = new GameObject(name, typeof(RectTransform));
            rowGO.transform.SetParent(ContentParent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, topPad, botPad);
            return rowGO;
        }

        private TextMeshProUGUI AddColumn(Transform parent, float preferredWidth, float flexibleWidth,
            TextAlignmentOptions align, string text, float minWidth = -1f)
        {
            GameObject go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            if (minWidth >= 0f) le.minWidth = minWidth;
            le.preferredWidth = preferredWidth;
            le.flexibleWidth  = flexibleWidth;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text = text; tmp.fontSize = 11f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.alignment = align; tmp.raycastTarget = false;
            return tmp;
        }

        private TextMeshProUGUI AddSubColumn(Transform parent, float preferredWidth, float flexibleWidth,
            TextAlignmentOptions align, string text, float minWidth = -1f)
        {
            var tmp = AddColumn(parent, preferredWidth, flexibleWidth, align, text, minWidth);
            tmp.fontSize = 10f;
            return tmp;
        }

        private const string U  = "<color=#888888>";
        private const string UE = "</color>";

        private static string FormatPower(double v)
        {
            if (v <= 0)         return $"0{U}E{UE}";
            if (v >= 1_000_000) return $"{v / 1_000_000:0.##}{U}ME{UE}";
            if (v >= 1_000)     return $"{v / 1_000:0.##}{U}KE{UE}";
            return $"{v:0.##}{U}E{UE}";
        }

        private static string FormatPowerRaw(double v)
        {
            double abs = Math.Abs(v); string sign = v < 0 ? "-" : "";
            if (abs >= 1_000_000) return $"{sign}{abs / 1_000_000:0.##}{U}ME{UE}";
            if (abs >= 1_000)     return $"{sign}{abs / 1_000:0.##}{U}KE{UE}";
            return $"{sign}{abs:0.##}{U}E{UE}";
        }

        private static string FormatBattery(double current, double max)
        {
            if (max <= 0) return $"{U}—{UE}";
            string cur = CompactEnergy(current);
            string mx  = CompactEnergy(max);
            int pct    = (int)Math.Round(current / max * 100.0);
            return $"{cur}{U}/{mx} ({pct}%){UE}";
        }

        private static string CompactEnergy(double v)
        {
            if (v >= 1_000_000) return $"{v / 1_000_000:0.##}ME";
            if (v >= 1_000)     return $"{v / 1_000:0.##}KE";
            return $"{v:0.##}E";
        }

        private static string FormatTons(double v)
        {
            if (v <= 0)         return $"0{U}T{UE}";
            if (v >= 1_000_000) return $"{v / 1_000_000:0.##}{U}MT{UE}";
            if (v >= 1_000)     return $"{v / 1_000:0.##}{U}KT{UE}";
            return $"{v:0.##}{U}T{UE}";
        }

        private static string FormatDays(double days)
        {
            if (days < 0)      return $"{U}—{UE}";
            if (days >= 36500) return $">100{U}y{UE}";
            if (days >= 365)   return $"{days / 365:0.#}{U}y{UE}";
            return $"{days:0.#}{U}d{UE}";
        }

        private void UpdateIndicatorSeverity(Severity severity)
        {
            if (IndicatorLabel == null) return;
            string dotHex = severity == Severity.Critical ? "#FF3333"
                : severity == Severity.Warning ? "#FF9900" : "#44BB44";
            IndicatorLabel.text = $"<color={dotHex}>●</color>  POWER";
            if (Mover != null) Mover.IsCritical = severity == Severity.Critical;
        }

        private struct BodyData
        {
            public ObjectInfo     OI;
            public ObjectInfoData Data;
            public double Gen, Cons, Balance, Battery, MaxBat;
            public List<ValueTuple<string, double>> FacilityFuelDays;
            public bool HasFacilities;
        }
    }

    // ── Draggable floating button ─────────────────────────────────────────────
    internal class DraggableMover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action OnClick;
        internal Image Bg;
        internal Color NormalColor, HoverColor, PressColor;
        internal TextMeshProUGUI FlashLabel;
        internal bool IsCritical;
        private float _flashTimer;
        private bool _flashOn = true;

        internal RectTransform ShowBtnRT;
        internal ManualLogSource Log;
        internal RectTransform PanelRT;
        internal GameObject PanelGO;

        private RectTransform _rt;
        private Canvas _canvas;
        private RectTransform _canvasRT;
        private Vector2 _dragStartAnchoredPos;
        private Vector2 _pressScreenPos;
        private Vector2 _lastCanvasSize;

        private void Awake()
        {
            _rt       = GetComponent<RectTransform>();
            _canvas   = GetComponentInParent<Canvas>();
            _canvasRT = _canvas?.GetComponent<RectTransform>();
        }

        private IEnumerator Start()
        {
            yield return null;
            PositionNextToNotificationButton();
        }

        private void Update()
        {
            if (FlashLabel != null && IsCritical)
            {
                _flashTimer += Time.deltaTime;
                if (_flashTimer >= 0.5f)
                {
                    _flashTimer = 0f; _flashOn = !_flashOn;
                    FlashLabel.text = _flashOn
                        ? "<color=#FF3333>●</color>  POWER"
                        : "<color=#1A0000>●</color>  POWER";
                }
            }
            if (_canvasRT != null)
            {
                Vector2 sz = _canvasRT.rect.size;
                if (sz != _lastCanvasSize) { _lastCanvasSize = sz; Clamp(); RepositionPanel(); }
            }
        }

        private void PositionNextToNotificationButton()
        {
            if (ShowBtnRT == null || _rt == null) return;
            Camera cam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _canvas?.worldCamera;
            Vector3[] corners = new Vector3[4];
            ShowBtnRT.GetWorldCorners(corners);
            Vector2 btnTopLeft;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRT, new Vector2(corners[1].x, corners[1].y), cam, out btnTopLeft))
            {
                Log?.LogWarning("[PT] RectTransformUtility failed — keeping parked position");
                return;
            }
            float x = btnTopLeft.x - 10f - _rt.sizeDelta.x;
            _rt.anchoredPosition = new Vector2(x, btnTopLeft.y - 5f - 17f);
            Log?.LogInfo($"[PT] indicator at {_rt.anchoredPosition}");
        }

        public void OnPointerEnter(PointerEventData e) { if (Bg) Bg.color = HoverColor; }
        public void OnPointerExit(PointerEventData e)  { if (Bg) Bg.color = NormalColor; }

        public void OnPointerDown(PointerEventData e)
        {
            _pressScreenPos = e.position;
            if (Bg) Bg.color = PressColor;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (Bg) Bg.color = HoverColor;
            if (Vector2.Distance(e.position, _pressScreenPos) < EventSystem.current.pixelDragThreshold)
                OnClick?.Invoke();
        }

        public void OnBeginDrag(PointerEventData e) { _dragStartAnchoredPos = _rt.anchoredPosition; }

        public void OnDrag(PointerEventData e)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rt.anchoredPosition = _dragStartAnchoredPos + (e.position - _pressScreenPos) / scale;
            Clamp(); RepositionPanel();
        }

        public void OnEndDrag(PointerEventData e) { Clamp(); RepositionPanel(); if (Bg) Bg.color = NormalColor; }

        private void Clamp()
        {
            if (_canvasRT == null) return;
            Rect cr = _canvasRT.rect; Vector2 s = _rt.sizeDelta; Vector2 p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, cr.xMin, cr.xMax - s.x);
            p.y = Mathf.Clamp(p.y, cr.yMin + s.y, cr.yMax);
            _rt.anchoredPosition = p;
        }

        private void RepositionPanel()
        {
            if (PanelGO == null || !PanelGO.activeSelf || PanelRT == null) return;
            PanelRT.anchoredPosition = new Vector2(
                _rt.anchoredPosition.x,
                _rt.anchoredPosition.y - _rt.sizeDelta.y - 4f);
        }
    }

    // ── Resize handle ─────────────────────────────────────────────────────────
    internal class ResizeHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IDragHandler
    {
        internal RectTransform PanelRT;
        private const float MinHeight = 150f;
        private static Texture2D _cursor;
        private Canvas _canvas;
        private bool _dragging;
        private Vector2 _dragStartScreen;
        private float _dragStartHeight;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_cursor == null) _cursor = BuildCursor();
        }

        public void OnPointerEnter(PointerEventData e) =>
            Cursor.SetCursor(_cursor, new Vector2(16, 16), CursorMode.Auto);

        public void OnPointerExit(PointerEventData e)
        {
            if (!_dragging) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        public void OnPointerDown(PointerEventData e)
        {
            _dragging = true; _dragStartScreen = e.position; _dragStartHeight = PanelRT.sizeDelta.y;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _dragging = false; Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        public void OnDrag(PointerEventData e)
        {
            float scale  = _canvas != null ? _canvas.scaleFactor : 1f;
            float delta  = (e.position.y - _dragStartScreen.y) / scale;
            float height = Mathf.Max(MinHeight, _dragStartHeight - delta);
            PanelRT.sizeDelta = new Vector2(PanelRT.sizeDelta.x, height);
        }

        private static Texture2D BuildCursor()
        {
            const int S = 32; const int cx = 15;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color[] px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;
            void Dot(int x, int y, Color c) { if (x >= 0 && x < S && y >= 0 && y < S) px[y * S + x] = c; }
            void Line(int x, bool outline) { for (int y = 9; y < S - 9; y++) Dot(x, y, outline ? Color.black : Color.white); }
            Line(cx - 1, true); Line(cx + 1, true); Line(cx, false);
            for (int i = 0; i < 6; i++)
            {
                int y = S - 3 - i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black); Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, S - 2, Color.black);
            for (int i = 0; i < 6; i++)
            {
                int y = 2 + i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black); Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, 1, Color.black);
            tex.SetPixels(px); tex.Apply();
            return tex;
        }
    }

    // ── Persistent updater on the always-active indicator GO ─────────────────
    internal class PowerTrackerUpdater : MonoBehaviour
    {
        internal PowerTrackerPanel Tracker;
        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= PowerTrackerPanel.RefreshInterval) { _timer = 0f; Tracker?.RefreshRows(); }
        }
    }

    // ── Per-body/facility alert threshold config ──────────────────────────────
    internal class PowerTrackerConfig
    {
        private const double DefaultBalanceWarnPct = 0.0;
        private const double DefaultBalanceCritPct = 20.0;
        private const double DefaultFuelWarnDays   = 730.0; // 2 years
        private const double DefaultFuelCritDays   = 365.0; // 1 year

        private readonly ConfigEntry<string> _balanceEntry;
        private readonly ConfigEntry<string> _fuelEntry;
        private ConfigEntry<bool>            _infoDismissed;

        private (double warnPct, double critPct)     _defaultBalance;
        private (double warnDays, double critDays)   _defaultFuel;
        private readonly Dictionary<string, (double warnPct, double critPct)>   _perBodyBalance
            = new Dictionary<string, (double, double)>();
        // Key: bodyName + TAB + facilityName (TAB never appears in game names)
        private readonly Dictionary<string, (double warnDays, double critDays)> _perFuel
            = new Dictionary<string, (double, double)>();

        internal PowerTrackerConfig(ConfigFile cfg)
        {
            _balanceEntry = cfg.Bind("PowerTracker", "BalanceThresholds", "",
                "Per-body energy balance warn/crit % thresholds. Managed by the in-game settings tab.");
            _fuelEntry    = cfg.Bind("PowerTracker", "FuelThresholds", "",
                "Per-(body,facility) fuel supply warn/crit day thresholds. Managed by the in-game settings tab.");
            _infoDismissed = cfg.Bind("PowerTracker", "InfoDismissed", false,
                "Whether the threshold panel info box has been permanently dismissed.");
            _defaultBalance = (DefaultBalanceWarnPct, DefaultBalanceCritPct);
            _defaultFuel    = (DefaultFuelWarnDays,   DefaultFuelCritDays);
            Load();
        }

        internal (double warnPct, double critPct)   DefaultBalance => _defaultBalance;
        internal (double warnDays, double critDays) DefaultFuel    => _defaultFuel;
        internal bool InfoDismissed => _infoDismissed.Value;
        internal void DismissInfo() { _infoDismissed.Value = true; }

        internal (double warnPct, double critPct) GetBalanceThresholds(string bodyName)
            => _perBodyBalance.TryGetValue(bodyName, out var v) ? v : _defaultBalance;

        internal (double warnDays, double critDays) GetFuelThresholds(string bodyName, string facilityName)
        {
            string key = FuelKey(bodyName, facilityName);
            return _perFuel.TryGetValue(key, out var v) ? v : _defaultFuel;
        }

        internal void SetBalance(string bodyName, double warnPct, double critPct)
        {
            if (bodyName == null)
                _defaultBalance = (Math.Max(0, warnPct), Math.Max(0, critPct));
            else
                _perBodyBalance[bodyName] = (Math.Max(0, warnPct), Math.Max(0, critPct));
            SaveBalance();
        }

        internal void SetFuel(string bodyName, string facilityName, double warnDays, double critDays)
        {
            if (bodyName == null)
                _defaultFuel = (Math.Max(0, warnDays), Math.Max(0, critDays));
            else
                _perFuel[FuelKey(bodyName, facilityName)] = (Math.Max(0, warnDays), Math.Max(0, critDays));
            SaveFuel();
        }

        internal void ClearBalance(string bodyName)
        {
            if (_perBodyBalance.Remove(bodyName)) SaveBalance();
        }

        internal void ClearFuel(string bodyName, string facilityName)
        {
            if (_perFuel.Remove(FuelKey(bodyName, facilityName))) SaveFuel();
        }

        // ── Serialization ─────────────────────────────────────────────────────

        // Format: "__DEFAULT__=0,20;Earth=5,15;Mars=0,30"
        private void SaveBalance()
        {
            var sb = new StringBuilder();
            sb.Append($"__DEFAULT__={_defaultBalance.warnPct:F1},{_defaultBalance.critPct:F1}");
            foreach (var kv in _perBodyBalance)
                sb.Append($";{Esc(kv.Key)}={kv.Value.warnPct:F1},{kv.Value.critPct:F1}");
            _balanceEntry.Value = sb.ToString();
        }

        // Format: "__DEFAULT__=30,7;Earth\tNuclear Reactor=60,14"  (TAB as separator)
        private void SaveFuel()
        {
            var sb = new StringBuilder();
            sb.Append($"__DEFAULT__={_defaultFuel.warnDays:F0},{_defaultFuel.critDays:F0}");
            foreach (var kv in _perFuel)
                sb.Append($";{Esc(kv.Key)}={kv.Value.warnDays:F0},{kv.Value.critDays:F0}");
            _fuelEntry.Value = sb.ToString();
        }

        private void Load()
        {
            LoadEntry(_balanceEntry.Value, isBalance: true);
            LoadEntry(_fuelEntry.Value,    isBalance: false);
        }

        private void LoadEntry(string raw, bool isBalance)
        {
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = Unesc(part.Substring(0, eq));
                string val = part.Substring(eq + 1);
                int comma  = val.IndexOf(',');
                if (comma < 0) continue;
                if (!double.TryParse(val.Substring(0, comma),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out double w)) continue;
                if (!double.TryParse(val.Substring(comma + 1),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out double c)) continue;

                if (isBalance)
                {
                    if (key == "__DEFAULT__") _defaultBalance = (w, c);
                    else _perBodyBalance[key] = (w, c);
                }
                else
                {
                    if (key == "__DEFAULT__") _defaultFuel = (w, c);
                    else _perFuel[key] = (w, c);
                }
            }
        }

        // TAB as inner separator: never appears in body/facility names
        private static string FuelKey(string bodyName, string facilityName)
            => (bodyName ?? "") + "\t" + (facilityName ?? "");

        // Outer escape: protect ; = \ in keys
        private static string Esc(string s)
            => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace("=", "\\=");
        private static string Unesc(string s)
            => s.Replace("\\=", "=").Replace("\\;", ";").Replace("\\\\", "\\");
    }
}
