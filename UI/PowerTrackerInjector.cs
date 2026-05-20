#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI;
using Manager;
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

        internal static void Inject(NotificationManager nm, ManualLogSource log)
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
                panelRT.sizeDelta        = new Vector2(640f, 280f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);

                LayoutElement panelLE = panelGO.AddComponent<LayoutElement>();
                panelLE.ignoreLayout = true;

                // Steal background sprite before destroying children
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

                panelRT.sizeDelta = new Vector2(640f, 280f);

                // ── Scroll viewport ──────────────────────────────────────────────────
                GameObject viewportGO = new GameObject("ScrollViewport", typeof(RectTransform));
                viewportGO.transform.SetParent(panelGO.transform, false);
                RectTransform viewportRT = viewportGO.GetComponent<RectTransform>();
                viewportRT.anchorMin = Vector2.zero;
                viewportRT.anchorMax = Vector2.one;
                viewportRT.pivot     = new Vector2(0.5f, 0.5f);
                viewportRT.offsetMin = new Vector2(8f, 8f);
                viewportRT.offsetMax = new Vector2(-22f, -8f);
                viewportGO.AddComponent<RectMask2D>();

                // Content
                GameObject contentGO = MakeScrollContent("ScrollContent", viewportGO.transform);
                RectTransform contentRT = contentGO.GetComponent<RectTransform>();

                // ── Vertical scrollbar ───────────────────────────────────────────────
                GameObject scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform));
                scrollbarGO.transform.SetParent(panelGO.transform, false);
                RectTransform scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
                scrollbarRT.anchorMin        = new Vector2(1f, 0f);
                scrollbarRT.anchorMax        = new Vector2(1f, 1f);
                scrollbarRT.pivot            = new Vector2(1f, 0.5f);
                scrollbarRT.sizeDelta        = new Vector2(6f, -16f);
                scrollbarRT.anchoredPosition = new Vector2(-8f, 0f);
                Image scrollbarBg = scrollbarGO.AddComponent<Image>();
                Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;

                GameObject slidingAreaGO = new GameObject("SlidingArea", typeof(RectTransform));
                slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
                RectTransform slidingAreaRT = slidingAreaGO.GetComponent<RectTransform>();
                slidingAreaRT.anchorMin        = Vector2.zero;
                slidingAreaRT.anchorMax        = Vector2.one;
                slidingAreaRT.sizeDelta        = Vector2.zero;
                slidingAreaRT.anchoredPosition = Vector2.zero;

                GameObject handleGO = new GameObject("Handle", typeof(RectTransform));
                handleGO.transform.SetParent(slidingAreaGO.transform, false);
                RectTransform handleRT = handleGO.GetComponent<RectTransform>();
                handleRT.anchorMin = Vector2.zero;
                handleRT.anchorMax = Vector2.one;
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
                resizeRT.anchorMin        = new Vector2(0f, 0f);
                resizeRT.anchorMax        = new Vector2(1f, 0f);
                resizeRT.pivot            = new Vector2(0.5f, 1f);
                resizeRT.sizeDelta        = new Vector2(0f, 10f);
                resizeRT.anchoredPosition = Vector2.zero;
                resizeHandleGO.AddComponent<Image>().color = Color.clear;
                resizeHandleGO.AddComponent<ResizeHandle>().PanelRT = panelRT;

                panelGO.SetActive(false);

                PowerTrackerPanel tracker = panelGO.AddComponent<PowerTrackerPanel>();
                tracker.ContentParent = contentGO.transform;
                tracker.FontAsset     = fontAsset;
                tracker.TrackerLog    = log;
                tracker.PanelRT       = panelRT;
                tracker.ScrollRectRef = scrollRect;

                // ── Floating draggable indicator button ──────────────────────────────
                GameObject indicatorGO = new GameObject("modPowerTrackerButton", typeof(RectTransform));
                indicatorGO.transform.SetParent(btnCanvas.transform, false);
                indicatorGO.transform.SetAsLastSibling();

                LayoutElement indicatorLE = indicatorGO.AddComponent<LayoutElement>();
                indicatorLE.ignoreLayout = true;

                RectTransform indicatorRT = indicatorGO.GetComponent<RectTransform>();
                indicatorRT.anchorMin        = new Vector2(0.5f, 0.5f);
                indicatorRT.anchorMax        = new Vector2(0.5f, 0.5f);
                indicatorRT.pivot            = new Vector2(0f, 1f);
                indicatorRT.sizeDelta        = new Vector2(150f, 30f);
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
                        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
                        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
                        panelRT.pivot            = new Vector2(0f, 1f);
                        panelRT.anchoredPosition = new Vector2(
                            indicatorRT.anchoredPosition.x,
                            indicatorRT.anchoredPosition.y - indicatorRT.sizeDelta.y - 4f);
                        scrollRect.verticalNormalizedPosition = 1f;
                        tracker.RefreshRows();
                    }
                    else
                    {
                        panelGO.SetActive(false);
                    }
                };

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
            catch (Exception e)
            {
                log.LogError($"[PT] Inject exception: {e}");
            }
        }

        private static GameObject MakeScrollContent(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.spacing = 1f;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            return go;
        }

        private static TextMeshProUGUI MakeButtonLabel(GameObject parent, TMP_FontAsset font)
        {
            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(parent.transform, false);
            RectTransform lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;
            TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text          = "<color=#44BB44>●</color>  POWER";
            tmp.fontSize      = 11f;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.color         = Color.white;
            tmp.raycastTarget = false;
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

        internal const float RefreshInterval = 5.0f;

        // Column widths
        private const float ColGen     = 72f;
        private const float ColCons    = 72f;
        private const float ColBalance = 72f;
        private const float ColBattery = 96f;

        // Persistent status-tab UI rows
        private bool _headerBuilt;
        private GameObject _titleRowGO;
        private TextMeshProUGUI _titleLbl;
        private GameObject _headerRowGO;
        private GameObject _headerSepGO;
        private GameObject _emptyMsgGO;
        private TextMeshProUGUI _emptyMsgLbl;
        private readonly Dictionary<string, BodyRowCache> _rowCache = new Dictionary<string, BodyRowCache>();

        private class BodyRowCache
        {
            public GameObject GO;
            public TextMeshProUGUI NameCol, GenCol, ConsCol, BalanceCol, BatteryCol;
            public Button Btn;
            public object BoundIdentity;
        }

        private enum Severity { OK, Warning, Critical }

        internal void RefreshRows()
        {
            try
            {
                if (ContentParent == null) { TrackerLog.LogError("[PT] ContentParent null"); return; }
                EnsureHeaderRows();

                Company player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
                var allObjects = player != null
                    ? MonoBehaviourSingleton<ObjectInfoManager>.Instance?.allObjectInfos
                    : null;

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

                    bodies.Add(new BodyData
                    {
                        OI      = oi,
                        Gen     = gen,
                        Cons    = cons,
                        Balance = gen - cons,
                        Battery = battery,
                        MaxBat  = maxBat,
                    });
                }

                // Sort: deficit bodies first (worst balance first), then by name
                bodies.Sort((a, b) =>
                {
                    bool aOK = a.Balance >= 0;
                    bool bOK = b.Balance >= 0;
                    if (aOK != bOK) return aOK ? 1 : -1;
                    if (!aOK && !bOK) return a.Balance.CompareTo(b.Balance);
                    return string.Compare(a.OI.ObjectName, b.OI.ObjectName, StringComparison.Ordinal);
                });

                int count = bodies.Count;
                _titleLbl.text = $"POWER STATUS  ({count} {(count == 1 ? "body" : "bodies")})";

                bool changed = SyncRows(bodies);

                bool empty = bodies.Count == 0;
                if (_emptyMsgGO.activeSelf != empty) { _emptyMsgGO.SetActive(empty); changed = true; }
                if (empty) _emptyMsgLbl.text = "No bodies with power infrastructure found.";

                ReorderContent(bodies);
                if (changed) LayoutRebuilder.ForceRebuildLayoutImmediate(ContentParent as RectTransform);

                // Overall severity = worst single body
                Severity overall = Severity.OK;
                foreach (var b in bodies)
                {
                    Severity s = GetSeverity(b);
                    if (s > overall) overall = s;
                }
                UpdateIndicatorSeverity(overall);
            }
            catch (Exception e)
            {
                TrackerLog.LogError($"[PT] RefreshRows exception: {e}");
            }
        }

        private static Severity GetSeverity(BodyData b)
        {
            if (b.Balance >= 0) return Severity.OK;
            // critical if deficit > 20% of consumption
            double pct = b.Cons > 0 ? -b.Balance / b.Cons : 1.0;
            return pct > 0.20 ? Severity.Critical : Severity.Warning;
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
            _titleLbl.fontSize           = 12f;
            _titleLbl.fontStyle          = FontStyles.Bold;
            _titleLbl.color              = Color.white;
            _titleLbl.enableWordWrapping = false;
            _titleLbl.alignment          = TextAlignmentOptions.MidlineLeft;
            _titleLbl.margin             = new Vector4(6, 4, 6, 0);

            (_headerRowGO, _headerSepGO) = AddHeaderRow();

            _emptyMsgGO = new GameObject("MsgRow", typeof(RectTransform));
            _emptyMsgGO.transform.SetParent(ContentParent, false);
            _emptyMsgGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            _emptyMsgLbl = _emptyMsgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) _emptyMsgLbl.font = FontAsset;
            _emptyMsgLbl.fontSize           = 11f;
            _emptyMsgLbl.color              = new Color(0.65f, 0.65f, 0.65f);
            _emptyMsgLbl.enableWordWrapping = false;
            _emptyMsgLbl.alignment          = TextAlignmentOptions.MidlineLeft;
            _emptyMsgLbl.margin             = new Vector4(6, 0, 6, 0);
            _emptyMsgGO.SetActive(false);
        }

        private bool SyncRows(List<BodyData> bodies)
        {
            bool changed = false;
            var activeNames = new HashSet<string>(bodies.Select(b => b.OI.ObjectName));

            var stale = _rowCache.Keys.Where(k => !activeNames.Contains(k)).ToList();
            foreach (var key in stale)
            {
                UnityEngine.Object.DestroyImmediate(_rowCache[key].GO);
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
                else
                {
                    UpdateRow(cache, b);
                }
            }
            return changed;
        }

        private void ReorderContent(List<BodyData> bodies)
        {
            int idx = 0;
            _titleRowGO.transform.SetSiblingIndex(idx++);
            _headerRowGO.transform.SetSiblingIndex(idx++);
            _headerSepGO.transform.SetSiblingIndex(idx++);
            foreach (var b in bodies)
                if (_rowCache.TryGetValue(b.OI.ObjectName, out var row))
                    row.GO.transform.SetSiblingIndex(idx++);
            _emptyMsgGO.transform.SetSiblingIndex(idx++);
        }

        private BodyRowCache CreateRow(BodyData b)
        {
            GameObject rowGO = MakeRowContainer($"Row_{b.OI.ObjectName}", 22f);
            rowGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var cache = new BodyRowCache
            {
                GO         = rowGO,
                NameCol    = AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft, "", 120f),
                GenCol     = AddColumn(rowGO.transform, ColGen,     0f, TextAlignmentOptions.MidlineRight, ""),
                ConsCol    = AddColumn(rowGO.transform, ColCons,    0f, TextAlignmentOptions.MidlineRight, ""),
                BalanceCol = AddColumn(rowGO.transform, ColBalance, 0f, TextAlignmentOptions.MidlineRight, ""),
                BatteryCol = AddColumn(rowGO.transform, ColBattery, 0f, TextAlignmentOptions.MidlineRight, ""),
                Btn        = rowGO.AddComponent<Button>(),
            };
            UpdateRow(cache, b);
            return cache;
        }

        private void UpdateRow(BodyRowCache cache, BodyData b)
        {
            if (!ReferenceEquals(cache.BoundIdentity, b.OI))
            {
                cache.Btn.onClick.RemoveAllListeners();
                ObjectInfo oiRef = b.OI;
                cache.Btn.onClick.AddListener(() =>
                {
                    try { UIManager.Instance.Open(EWindowType.ObjectInfo, oiRef); }
                    catch (Exception e) { TrackerLog.LogError($"[PT] row click: {e.Message}"); }
                });
                cache.BoundIdentity = b.OI;
            }

            Severity sev    = GetSeverity(b);
            string dotHex   = sev == Severity.Critical ? "#FF3333"
                : sev == Severity.Warning ? "#FF9900"
                : "#44BB44";
            string spriteName = b.OI.ImagePlanetUI?.name ?? "";
            string icon       = spriteName.Length > 0 ? $"<sprite name={spriteName}> " : "";

            cache.NameCol.text  = $"<color={dotHex}>●</color>  {icon}{b.OI.ObjectName}";
            cache.NameCol.color = Color.white;

            cache.GenCol.text  = FormatPower(b.Gen);
            cache.GenCol.color = new Color(0.85f, 0.85f, 0.85f);

            cache.ConsCol.text  = FormatPower(b.Cons);
            cache.ConsCol.color = new Color(0.85f, 0.85f, 0.85f);

            if (b.Balance >= 0)
            {
                cache.BalanceCol.text  = $"<color=#44BB44>+{FormatPowerRaw(b.Balance)}</color>";
                cache.BalanceCol.color = Color.white;
            }
            else
            {
                string balHex = sev == Severity.Critical ? "#FF3333" : "#FF9900";
                cache.BalanceCol.text  = $"<color={balHex}>{FormatPowerRaw(b.Balance)}</color>";
                cache.BalanceCol.color = Color.white;
            }

            cache.BatteryCol.text  = FormatBattery(b.Battery, b.MaxBat);
            cache.BatteryCol.color = new Color(0.85f, 0.85f, 0.85f);
        }

        private (GameObject row, GameObject sep) AddHeaderRow()
        {
            GameObject rowGO = MakeRowContainer("HeaderRow", 20f);
            Color hc = new Color(0.55f, 0.55f, 0.55f);
            AddColumn(rowGO.transform, 0f, 1f, TextAlignmentOptions.MidlineLeft,  "BODY",    120f).color = hc;
            AddColumn(rowGO.transform, ColGen,     0f, TextAlignmentOptions.MidlineRight, "GEN/DAY").color     = hc;
            AddColumn(rowGO.transform, ColCons,    0f, TextAlignmentOptions.MidlineRight, "CONS/DAY").color    = hc;
            AddColumn(rowGO.transform, ColBalance, 0f, TextAlignmentOptions.MidlineRight, "BALANCE").color     = hc;
            AddColumn(rowGO.transform, ColBattery, 0f, TextAlignmentOptions.MidlineRight, "BATTERY").color     = hc;

            GameObject sep = new GameObject("Separator", typeof(RectTransform));
            sep.transform.SetParent(ContentParent, false);
            sep.AddComponent<LayoutElement>().preferredHeight = 1f;
            sep.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f, 1f);
            return (rowGO, sep);
        }

        private GameObject MakeRowContainer(string name, float height)
        {
            GameObject rowGO = new GameObject(name, typeof(RectTransform));
            rowGO.transform.SetParent(ContentParent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth      = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
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
            tmp.text               = text;
            tmp.fontSize           = 11f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.alignment          = align;
            tmp.raycastTarget      = false;
            return tmp;
        }

        private const string U  = "<color=#888888>";
        private const string UE = "</color>";

        private static string FormatPower(double v)
        {
            if (v <= 0)          return $"0{U}kW{UE}";
            if (v >= 1_000_000)  return $"{v / 1_000_000:F1}{U}GW{UE}";
            if (v >= 1_000)      return $"{v / 1_000:F1}{U}MW{UE}";
            return $"{v:F0}{U}kW{UE}";
        }

        private static string FormatPowerRaw(double v)
        {
            double abs = Math.Abs(v);
            string sign = v < 0 ? "-" : "";
            if (abs >= 1_000_000)  return $"{sign}{abs / 1_000_000:F1}{U}GW{UE}";
            if (abs >= 1_000)      return $"{sign}{abs / 1_000:F1}{U}MW{UE}";
            return $"{sign}{abs:F0}{U}kW{UE}";
        }

        private static string FormatBattery(double current, double max)
        {
            if (max <= 0) return $"{U}—{UE}";
            string cur = CompactEnergy(current);
            string mx  = CompactEnergy(max);
            int pct    = max > 0 ? (int)Math.Round(current / max * 100.0) : 0;
            return $"{cur}{U}/{mx} ({pct}%){UE}";
        }

        private static string CompactEnergy(double v)
        {
            if (v >= 1_000_000) return $"{v / 1_000_000:F1}G";
            if (v >= 1_000)     return $"{v / 1_000:F1}M";
            return $"{v:F0}k";
        }

        private void UpdateIndicatorSeverity(Severity severity)
        {
            if (IndicatorLabel == null) return;
            bool critical = severity == Severity.Critical;
            string dotHex = severity == Severity.Critical ? "#FF3333"
                : severity == Severity.Warning ? "#FF9900"
                : "#44BB44";
            IndicatorLabel.text = $"<color={dotHex}>●</color>  POWER";
            if (Mover != null) Mover.IsCritical = critical;
        }

        private struct BodyData
        {
            public ObjectInfo OI;
            public double Gen, Cons, Balance, Battery, MaxBat;
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
        internal Color NormalColor;
        internal Color HoverColor;
        internal Color PressColor;

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
                    _flashTimer = 0f;
                    _flashOn = !_flashOn;
                    FlashLabel.text = _flashOn
                        ? "<color=#FF3333>●</color>  POWER"
                        : "<color=#1A0000>●</color>  POWER";
                }
            }

            if (_canvasRT != null)
            {
                Vector2 sz = _canvasRT.rect.size;
                if (sz != _lastCanvasSize)
                {
                    _lastCanvasSize = sz;
                    Clamp();
                    RepositionPanel();
                }
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

            // Position below the LifeSupportTracker button (offset by one button height + gap)
            float x = btnTopLeft.x - 10f - _rt.sizeDelta.x;
            _rt.anchoredPosition = new Vector2(x, btnTopLeft.y - 5f - 34f);
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

        public void OnBeginDrag(PointerEventData e)
        {
            _dragStartAnchoredPos = _rt.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rt.anchoredPosition = _dragStartAnchoredPos + (e.position - _pressScreenPos) / scale;
            Clamp();
            RepositionPanel();
        }

        public void OnEndDrag(PointerEventData e)
        {
            Clamp();
            RepositionPanel();
            if (Bg) Bg.color = NormalColor;
        }

        private void Clamp()
        {
            if (_canvasRT == null) return;
            Rect cr   = _canvasRT.rect;
            Vector2 s = _rt.sizeDelta;
            Vector2 p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, cr.xMin,        cr.xMax - s.x);
            p.y = Mathf.Clamp(p.y, cr.yMin + s.y,  cr.yMax);
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
            _dragging        = true;
            _dragStartScreen = e.position;
            _dragStartHeight = PanelRT.sizeDelta.y;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _dragging = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
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
            const int S  = 32;
            const int cx = 15;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color[] px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            void Dot(int x, int y, Color c) { if (x >= 0 && x < S && y >= 0 && y < S) px[y * S + x] = c; }
            void Line(int x, bool outline) { Color core = Color.white; Color ol = Color.black; for (int y = 9; y < S - 9; y++) Dot(x, y, outline ? ol : core); }

            Line(cx - 1, true); Line(cx + 1, true); Line(cx, false);

            for (int i = 0; i < 6; i++)
            {
                int y = S - 3 - i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black);
                Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, S - 2, Color.black);

            for (int i = 0; i < 6; i++)
            {
                int y = 2 + i;
                for (int x = cx - i; x <= cx + i; x++) Dot(x, y, Color.white);
                Dot(cx - i - 1, y, Color.black);
                Dot(cx + i + 1, y, Color.black);
            }
            for (int x = cx - 1; x <= cx + 1; x++) Dot(x, 1, Color.black);

            tex.SetPixels(px);
            tex.Apply();
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
            if (_timer >= PowerTrackerPanel.RefreshInterval)
            {
                _timer = 0f;
                Tracker?.RefreshRows();
            }
        }
    }
}
