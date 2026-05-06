using System;
using System.Collections.Generic;
using System.Linq;
using FloorManagerCopyPaste.Models;
using FloorManagerCopyPaste.Services;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FloorManagerCopyPaste.UI;

internal sealed class RackPlannerScreenController
{
    public static RackPlannerScreenController Instance { get; } = new();

    private enum Page { Main, FloorPlan, PlanRacks, ViewTemplates }

    // ------------------------------------------------------------------ state
    private readonly List<RackRuntimeInfo> _racks = new();
    private readonly List<RackTemplate> _templates = new();
    private RackRuntimeInfo _floorPlanSelectedRack;
    private RackRuntimeInfo _planRacksSelectedRack;
    private int _planTemplateIndex = -1;
    private int _vtSelectedIndex = -1;
    private Page _currentPage = Page.Main;
    private float _fpZoom = 1f;
    private float _prZoom = 1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 2.5f;

    // ------------------------------------------------------------------ ui refs
    private GameObject _screenRoot;
    private GameObject _mainPage, _floorPlanPage, _planRacksPage, _viewTemplatesPage;

    private GameObject _fpFloorContent, _fpSidebarContent;
    private TextMeshProUGUI _fpHeader, _fpStatus;

    private GameObject _prFloorContent, _prSidebarContent;
    private TextMeshProUGUI _prHeader, _prStatus, _prTemplateLabel;

    private GameObject _vtListContent, _vtPreviewContent;
    private TextMeshProUGUI _vtHeader, _vtStatus;

    // simple modal text input
    private GameObject _modalRoot;
    private TMP_InputField _modalInput;
    private TextMeshProUGUI _modalPrompt;
    private Action<string> _modalCallback;

    // ============================================================================
    // PUBLIC API (called by the patches)
    // ============================================================================

    public GameObject EnsureScreen(GameObject mainScreen)
    {
        if (_screenRoot != null) return _screenRoot;
        _screenRoot = BuildScreen(mainScreen);
        return _screenRoot;
    }

    public void Open(ComputerShop shop, GameObject mainScreen)
    {
        var screenRoot = EnsureScreen(mainScreen);
        ClearSelectionAndHighlight();
        shop.ButtonReturnMainScreen();
        mainScreen.SetActive(false);
        screenRoot.SetActive(true);
        ShowPage(Page.Main);
    }

    public void Close(GameObject mainScreen = null)
    {
        // Clear selection BEFORE deactivating so OnDeselect/OnPointerExit fire on the
        // currently selected/highlighted button. Otherwise the outline + highlight
        // colour remain "stuck" on the button as a ghost the next time the UI is shown.
        ClearSelectionAndHighlight();
        if (_screenRoot != null) _screenRoot.SetActive(false);
        if (mainScreen != null) mainScreen.SetActive(true);
    }

    /// <summary>
    /// Deselects the currently focused UI element and clears any pointer-highlight state
    /// on it. Must be called BEFORE deactivating the GameObject containing the selection,
    /// because Unity does not dispatch OnDeselect/OnPointerExit to inactive objects, which
    /// causes the selection outline / highlighted colour to remain visible ("ghost outline")
    /// when the UI is re-opened.
    /// </summary>
    private static void ClearSelectionAndHighlight()
    {
        var es = EventSystem.current;
        if (es == null) return;

        var selected = es.currentSelectedGameObject;
        if (selected != null)
        {
            // Fire OnDeselect on the selected element
            ExecuteEvents.Execute(selected, new BaseEventData(es), ExecuteEvents.deselectHandler);

            // Fire OnPointerExit so any hover/highlight tint is cleared as well
            var ped = new PointerEventData(es) { pointerEnter = selected };
            ExecuteEvents.Execute(selected, ped, ExecuteEvents.pointerExitHandler);
        }

        es.SetSelectedGameObject(null);
    }

    // ============================================================================
    // NAVIGATION
    // ============================================================================

    private void ShowPage(Page page)
    {
        // Same reason as in Close(): clear selection/highlight before any page is
        // deactivated so the outline/highlight does not stick on the button that
        // triggered the navigation.
        ClearSelectionAndHighlight();

        _currentPage = page;
        if (_mainPage != null) _mainPage.SetActive(page == Page.Main);
        if (_floorPlanPage != null) _floorPlanPage.SetActive(page == Page.FloorPlan);
        if (_planRacksPage != null) _planRacksPage.SetActive(page == Page.PlanRacks);
        if (_viewTemplatesPage != null) _viewTemplatesPage.SetActive(page == Page.ViewTemplates);

        switch (page)
        {
            case Page.FloorPlan: RefreshFloorPlan(); break;
            case Page.PlanRacks: RefreshPlanRacks(); break;
            case Page.ViewTemplates: RefreshViewTemplates(); break;
        }
    }

    private void RefreshAll()
    {
        _racks.Clear();
        _racks.AddRange(RackPlannerService.GetRackInfos());
        _templates.Clear();
        _templates.AddRange(RackPlannerService.LoadTemplates());

        _floorPlanSelectedRack = ResolveRack(_floorPlanSelectedRack?.Rack) ?? _racks.FirstOrDefault();
        _planRacksSelectedRack = ResolveRack(_planRacksSelectedRack?.Rack) ?? _racks.FirstOrDefault(r => r.UsedSlots == 0) ?? _racks.FirstOrDefault();

        if (_planTemplateIndex >= _templates.Count) _planTemplateIndex = _templates.Count - 1;
        if (_vtSelectedIndex >= _templates.Count) _vtSelectedIndex = _templates.Count - 1;
        if (_vtSelectedIndex < 0 && _templates.Count > 0) _vtSelectedIndex = 0;
    }

    private RackRuntimeInfo ResolveRack(Rack rack) =>
        rack == null ? null : _racks.FirstOrDefault(r => r.Rack == rack);

    // ============================================================================
    // SCREEN ROOT
    // ============================================================================

    private GameObject BuildScreen(GameObject mainScreen)
    {
        var root = CreateUiObject(FloorManagerCopyPasteMod.FloorManagerScreenObjectName, mainScreen.transform.parent);
        root.SetActive(false);
        Stretch(root.GetComponent<RectTransform>());
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.06f, 0.09f, 0.99f);

        _mainPage = BuildMainPage(root.transform);
        _floorPlanPage = BuildFloorPlanPage(root.transform);
        _planRacksPage = BuildPlanRacksPage(root.transform);
        _viewTemplatesPage = BuildViewTemplatesPage(root.transform);
        _modalRoot = BuildModal(root.transform);
        _modalRoot.SetActive(false);

        _floorPlanPage.SetActive(false);
        _planRacksPage.SetActive(false);
        _viewTemplatesPage.SetActive(false);
        return root;
    }

    // ============================================================================
    // MAIN MENU PAGE
    // ============================================================================

    private GameObject BuildMainPage(Transform parent)
    {
        var page = CreateUiObject("MainPage", parent);
        Stretch(page.GetComponent<RectTransform>());

        var col = CreateUiObject("Center", page.transform);
        var colRt = col.GetComponent<RectTransform>();
        colRt.anchorMin = new Vector2(0.5f, 0.5f);
        colRt.anchorMax = new Vector2(0.5f, 0.5f);
        colRt.pivot = new Vector2(0.5f, 0.5f);
        colRt.sizeDelta = new Vector2(560f, 480f);
        var colLayout = col.AddComponent<VerticalLayoutGroup>();
        var pad = new RectOffset(); pad.left = 24; pad.right = 24; pad.top = 24; pad.bottom = 24;
        colLayout.padding = pad;
        colLayout.spacing = 16f;
        colLayout.childControlWidth = true;
        colLayout.childForceExpandWidth = true;
        colLayout.childAlignment = TextAnchor.UpperCenter;

        var title = BuildLabel(col.transform, "Floor Manager: Copy & Paste", 38f, TextAlignmentOptions.Center, Color.white);
        SetPreferredHeight(title.gameObject, 56f);
        var sub = BuildLabel(col.transform, "Werkzeuge zum Kopieren, Einfügen und Verwalten von Rack-Layouts", 16f, TextAlignmentOptions.Center, new Color(0.78f, 0.86f, 0.96f, 1f));
        SetPreferredHeight(sub.gameObject, 24f);

        BuildBigButton(col.transform, "Floor Plan", "Bestehende Racks ansehen, kopieren oder als Vorlage speichern",
            new Color(0.18f, 0.55f, 0.86f, 1f), () => ShowPage(Page.FloorPlan));
        BuildBigButton(col.transform, "Plan Racks", "Leere Racks bestücken oder neue Racks kaufen",
            new Color(0.86f, 0.55f, 0.18f, 1f), () => ShowPage(Page.PlanRacks));
        BuildBigButton(col.transform, "View Templates", "Gespeicherte Vorlagen mit Vorschau & Preisschätzung",
            new Color(0.45f, 0.32f, 0.78f, 1f), () => ShowPage(Page.ViewTemplates));

        BuildBigButton(col.transform, "Schließen", "Zurück zum Laptop-Hauptscreen",
            new Color(0.32f, 0.34f, 0.40f, 1f), () => Close(FloorManagerCopyPasteMod.MainScreen));

        return page;
    }

    private void BuildBigButton(Transform parent, string title, string subtitle, Color color, Action onClick)
    {
        var btn = CreateUiObject("BigBtn_" + title, parent);
        SetPreferredHeight(btn, 72f);
        var img = btn.AddComponent<Image>();
        img.color = color;
        var ol = btn.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.4f);
        ol.effectDistance = new Vector2(2f, -2f);
        var b = btn.AddComponent<Button>();
        b.targetGraphic = img;
        ConfigureButtonColors(b, color);
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(() =>
        {
            try { onClick(); }
            finally { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
        }));

        var lblTitle = BuildLabel(btn.transform, title, 22f, TextAlignmentOptions.MidlineLeft, Color.white);
        Stretch(lblTitle.rectTransform, new Vector2(20f, 28f), new Vector2(-20f, -8f));
        var lblSub = BuildLabel(btn.transform, subtitle, 13f, TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.78f));
        Stretch(lblSub.rectTransform, new Vector2(22f, 6f), new Vector2(-22f, -36f));
    }

    // ============================================================================
    // FLOOR PLAN PAGE  (full-screen floor map + sidebar with rack contents)
    // ============================================================================

    private GameObject BuildFloorPlanPage(Transform parent)
    {
        var page = CreateUiObject("FloorPlanPage", parent);
        Stretch(page.GetComponent<RectTransform>());

        var (mapArea, sidebar) = BuildPageWithSidebar(page.transform, "Floor Plan", out _fpHeader, out _fpStatus);

        // Map (left): scrollable, both axes
        var floorScroll = BuildScrollRegion(mapArea.transform, out _fpFloorContent, 0f, true, true, false);
        SetFlexibleHeight(floorScroll, 1f);
        SetFlexibleWidth(floorScroll, 1f);

        // Sidebar (right): rack title + scrollable component list + buttons
        var sbInner = BuildContainer(sidebar.transform, -1f);
        SetFlexibleHeight(sbInner, 1f);

        var listScroll = BuildScrollRegion(sbInner.transform, out _fpSidebarContent, 0f, false, true, true);
        SetFlexibleHeight(listScroll, 1f);

        // Bottom buttons – row height matches normal button height (34) so the buttons
        // are NOT visibly taller than other buttons elsewhere on the page.
        var btnRow = CreateUiObject("Buttons", sbInner.transform);
        var rowLayout = btnRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandHeight = false;
        SetPreferredHeight(btnRow, 34f);

        BuildButton(btnRow.transform, "Copy", () =>
        {
            if (_floorPlanSelectedRack == null) { _fpStatus.text = "Kein Rack gewählt."; return; }
            RackPlannerService.Clipboard = RackPlannerService.CaptureRackTemplate(_floorPlanSelectedRack, "clipboard");
            _fpStatus.text = $"Clipboard: {RackPlannerService.Clipboard.Devices.Count} Geräte, {RackPlannerService.Clipboard.Cables.Count} Kabel.";
        }, 0f);

        BuildButton(btnRow.transform, "Create Template", () =>
        {
            try
            {
                if (_floorPlanSelectedRack == null) { _fpStatus.text = "Kein Rack gewählt."; return; }
                var rackToCapture = _floorPlanSelectedRack;
                OpenModal($"Vorlagenname für {rackToCapture.Label}:", $"{rackToCapture.Label}_{DateTime.Now:HHmmss}", name =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(name)) { _fpStatus.text = "Vorlagenname leer."; return; }
                        var t = RackPlannerService.CaptureRackTemplate(rackToCapture, name);
                        _templates.Insert(0, t);
                        RackPlannerService.SaveTemplates(_templates);
                        _fpStatus.text = $"Vorlage gespeichert: {t.Name} ({t.Devices.Count} Geräte, {t.Cables.Count} Kabel).";
                    }
                    catch (Exception ex)
                    {
                        _fpStatus.text = $"Vorlage speichern fehlgeschlagen: {ex.Message}";
                        MelonLoader.MelonLogger.Error($"[RackPlanner] Create Template (save) failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                _fpStatus.text = $"Create Template Fehler: {ex.Message}";
                MelonLoader.MelonLogger.Error($"[RackPlanner] Create Template failed: {ex}");
            }
        }, 0f);

        // Header back-button row
        AddBackButton(page.transform, _fpHeader.transform.parent);
        AddZoomButtons(_fpHeader.transform.parent, () => _fpZoom, v => { _fpZoom = v; RefreshFloorPlan(); });
        AttachMouseWheelZoom(floorScroll, () => _fpZoom, v => { _fpZoom = v; RefreshFloorPlan(); });

        return page;
    }

    private void RefreshFloorPlan()
    {
        RefreshAll();
        _fpHeader.text = "Floor Plan";
        if (_racks.Count > 0 && _floorPlanSelectedRack == null)
            _floorPlanSelectedRack = _racks[0];

        RenderFloorMap(_fpFloorContent, _racks, r => { _floorPlanSelectedRack = r; RefreshFloorPlan(); }, _floorPlanSelectedRack, _fpZoom);
        RenderRackComponentSidebar(_fpSidebarContent, _floorPlanSelectedRack);
        if (string.IsNullOrEmpty(_fpStatus.text))
            _fpStatus.text = _floorPlanSelectedRack == null ? "Keine Racks gefunden." : $"Ausgewählt: {_floorPlanSelectedRack.Label}";
    }

    // ============================================================================
    // PLAN RACKS PAGE  (only empty racks + buy new rack + paste)
    // ============================================================================

    private GameObject BuildPlanRacksPage(Transform parent)
    {
        var page = CreateUiObject("PlanRacksPage", parent);
        Stretch(page.GetComponent<RectTransform>());

        var (mapArea, sidebar) = BuildPageWithSidebar(page.transform, "Plan Racks", out _prHeader, out _prStatus);

        var floorScroll = BuildScrollRegion(mapArea.transform, out _prFloorContent, 0f, true, true, false);
        SetFlexibleHeight(floorScroll, 1f);
        SetFlexibleWidth(floorScroll, 1f);

        var sbInner = BuildContainer(sidebar.transform, -1f);
        SetFlexibleHeight(sbInner, 1f);

        _prTemplateLabel = BuildLabel(sbInner.transform, "Vorlage: –", 14f, TextAlignmentOptions.Left, new Color(0.85f, 0.92f, 1f, 1f));
        SetPreferredHeight(_prTemplateLabel.gameObject, 22f);

        var pickRow = CreateUiObject("PickRow", sbInner.transform);
        var pickLayout = pickRow.AddComponent<HorizontalLayoutGroup>();
        pickLayout.spacing = 6f; pickLayout.childControlWidth = true; pickLayout.childForceExpandWidth = true;
        pickLayout.childControlHeight = true; pickLayout.childForceExpandHeight = true;
        SetPreferredHeight(pickRow, 34f);
        BuildButton(pickRow.transform, "<", () => { CycleTemplate(-1); RefreshPlanRacks(); }, 40f);
        BuildButton(pickRow.transform, ">", () => { CycleTemplate(1); RefreshPlanRacks(); }, 40f);

        var actions = CreateUiObject("Actions", sbInner.transform);
        var actLayout = actions.AddComponent<VerticalLayoutGroup>();
        actLayout.spacing = 4f; actLayout.childControlWidth = true; actLayout.childForceExpandWidth = true;
        actLayout.childControlHeight = true; actLayout.childForceExpandHeight = false;
        SetFlexibleHeight(actions, 1f);

        BuildButton(actions.transform, "Paste Template", () =>
        {
            var t = GetSelectedPlanTemplate();
            if (t == null) { _prStatus.text = "Keine Vorlage gewählt."; return; }
            ApplyToSelectedTarget(t, $"Vorlage {t.Name}");
        }, 0f);
        BuildButton(actions.transform, "Paste from Clipboard", () =>
        {
            if (RackPlannerService.Clipboard == null) { _prStatus.text = "Clipboard ist leer."; return; }
            ApplyToSelectedTarget(RackPlannerService.Clipboard, "Clipboard");
        }, 0f);
        BuildButton(actions.transform, "Buy New Rack (here)", () =>
        {
            var pos = PlayerManager.instance?.playerClass?.transform?.position ?? Vector3.zero;
            if (RackPlannerService.TryBuyAndPlaceRack(pos, out var msg))
                _prStatus.text = msg;
            else
                _prStatus.text = "Kauf fehlgeschlagen: " + msg;
            RefreshPlanRacks();
        }, 0f);
        BuildButton(actions.transform, "Aktualisieren", () => RefreshPlanRacks(), 0f);

        AddBackButton(page.transform, _prHeader.transform.parent);
        AddZoomButtons(_prHeader.transform.parent, () => _prZoom, v => { _prZoom = v; RefreshPlanRacks(); });
        AttachMouseWheelZoom(floorScroll, () => _prZoom, v => { _prZoom = v; RefreshPlanRacks(); });
        return page;
    }

    private void CycleTemplate(int delta)
    {
        if (_templates.Count == 0) return;
        _planTemplateIndex = _planTemplateIndex < 0 ? 0 : (_planTemplateIndex + delta + _templates.Count) % _templates.Count;
    }

    private RackTemplate GetSelectedPlanTemplate() =>
        _planTemplateIndex >= 0 && _planTemplateIndex < _templates.Count ? _templates[_planTemplateIndex] : null;

    private void ApplyToSelectedTarget(RackTemplate template, string description)
    {
        if (_planRacksSelectedRack == null) { _prStatus.text = "Kein Ziel-Rack gewählt."; return; }
        var result = RackPlannerService.ApplyTemplate(template, _planRacksSelectedRack);
        _prStatus.text = $"{description}: {result.SpawnedCount} Geräte, {result.CablesCreated} Kabel, Kosten {result.ChargedAmount}.\n"
                       + string.Join("\n", result.Messages.Take(4));
        RefreshPlanRacks();
    }

    private void RefreshPlanRacks()
    {
        RefreshAll();
        _prHeader.text = "Plan Racks  ·  Belegte Racks rot markiert";

        // Show ALL racks so users can see the full floor – but flag every rack with
        // any installed device as "invalid paste target" so they can't accidentally
        // pick one. Empty racks remain valid drop targets.
        bool IsOccupied(RackRuntimeInfo r) => r != null && r.UsedSlots > 0;

        if (_planRacksSelectedRack == null || IsOccupied(_planRacksSelectedRack))
            _planRacksSelectedRack = _racks.FirstOrDefault(r => !IsOccupied(r));

        RenderFloorMap(
            _prFloorContent,
            _racks,
            r =>
            {
                if (IsOccupied(r))
                {
                    _prStatus.text = $"{r.Label} ist belegt – kein gültiges Paste-Ziel.";
                    return;
                }
                _planRacksSelectedRack = r;
                RefreshPlanRacks();
            },
            _planRacksSelectedRack,
            _prZoom,
            IsOccupied);

        var t = GetSelectedPlanTemplate();
        var clip = RackPlannerService.Clipboard;
        _prTemplateLabel.text = t == null
            ? (clip == null ? "Vorlage: – (Clipboard leer)" : $"Clipboard bereit · {clip.Devices.Count} Geräte")
            : $"Vorlage: {t.Name} · {t.Devices.Count} Geräte / {t.Cables.Count} Kabel";

        // Sidebar shows the *target* rack contents (likely empty) for confirmation
        // Plus, when a template is selected, also a price estimate.
        // Hidden behind a vertical scroll that isn't part of sidebar layout — easier to skip.
    }

    // ============================================================================
    // VIEW TEMPLATES PAGE  (sidebar list of templates + 2D blueprint preview)
    // ============================================================================

    private GameObject BuildViewTemplatesPage(Transform parent)
    {
        var page = CreateUiObject("ViewTemplatesPage", parent);
        Stretch(page.GetComponent<RectTransform>());

        // Layout: header at top, body Horizontal: left list (sidebar) + right preview
        var (mainArea, leftPanel) = BuildPageWithSidebar(page.transform, "View Templates", out _vtHeader, out _vtStatus, sidebarOnLeft: true, sidebarWidth: 220f);

        // Sidebar = list of templates
        var sbInner = BuildContainer(leftPanel.transform, -1f);
        SetFlexibleHeight(sbInner, 1f);
        var listScroll = BuildScrollRegion(sbInner.transform, out _vtListContent, 0f, false, true, true);
        SetFlexibleHeight(listScroll, 1f);
        var rowBtns = CreateUiObject("Row", sbInner.transform);
        var rl = rowBtns.AddComponent<HorizontalLayoutGroup>();
        rl.spacing = 4f; rl.childControlWidth = true; rl.childForceExpandWidth = true; rl.childControlHeight = true; rl.childForceExpandHeight = true;
        SetPreferredHeight(rowBtns, 34f);
        BuildButton(rowBtns.transform, "Aktualisieren", () => RefreshViewTemplates(), 0f);
        BuildButton(rowBtns.transform, "Löschen", () =>
        {
            if (_vtSelectedIndex < 0 || _vtSelectedIndex >= _templates.Count) { _vtStatus.text = "Keine Vorlage gewählt."; return; }
            var name = _templates[_vtSelectedIndex].Name;
            RackPlannerService.DeleteTemplate(_templates, _vtSelectedIndex);
            _vtStatus.text = $"Vorlage gelöscht: {name}";
            _vtSelectedIndex = Math.Min(_vtSelectedIndex, _templates.Count - 1);
            RefreshViewTemplates();
        }, 0f);
        BuildButton(rowBtns.transform, "→ Clipboard", () =>
        {
            if (_vtSelectedIndex < 0 || _vtSelectedIndex >= _templates.Count) return;
            RackPlannerService.Clipboard = _templates[_vtSelectedIndex];
            _vtStatus.text = $"Clipboard: {RackPlannerService.Clipboard.Name}";
        }, 0f);

        // Main = preview area (scrollable)
        var prevScroll = BuildScrollRegion(mainArea.transform, out _vtPreviewContent, 0f, false, true, true);
        SetFlexibleHeight(prevScroll, 1f);

        AddBackButton(page.transform, _vtHeader.transform.parent);
        return page;
    }

    private void RefreshViewTemplates()
    {
        RefreshAll();
        _vtHeader.text = $"View Templates  ·  {_templates.Count} gespeichert";

        ClearChildren(_vtListContent.transform);
        if (_templates.Count == 0)
        {
            BuildLabel(_vtListContent.transform, "Keine Vorlagen vorhanden.", 14f, TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f, 1f));
        }
        else
        {
            for (var i = 0; i < _templates.Count; i++)
            {
                var idx = i;
                var t = _templates[i];
                var row = CreateUiObject($"TplRow_{i}", _vtListContent.transform);
                SetPreferredHeight(row, 56f);
                var img = row.AddComponent<Image>();
                img.color = idx == _vtSelectedIndex ? new Color(0.18f, 0.36f, 0.56f, 1f) : new Color(0.13f, 0.16f, 0.22f, 1f);
                var b = row.AddComponent<Button>();
                b.targetGraphic = img;
                ConfigureButtonColors(b, img.color);
                // Disable focus navigation so Unity does not leave a "selected" outline on
                // the previously clicked row.
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(() =>
                {
                    try { _vtSelectedIndex = idx; RefreshViewTemplates(); }
                    finally { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
                }));

                var n = BuildLabel(row.transform, t.Name, 14f, TextAlignmentOptions.TopLeft, Color.white);
                Stretch(n.rectTransform, new Vector2(8f, 26f), new Vector2(-8f, -4f));
                var sub = BuildLabel(row.transform, $"{t.Devices.Count} Geräte · {t.Cables.Count} Kabel", 11f, TextAlignmentOptions.BottomLeft, new Color(0.78f, 0.86f, 0.98f, 1f));
                Stretch(sub.rectTransform, new Vector2(8f, 4f), new Vector2(-8f, -28f));
            }
        }

        var sel = _vtSelectedIndex >= 0 && _vtSelectedIndex < _templates.Count ? _templates[_vtSelectedIndex] : null;
        RenderTemplatePreview(_vtPreviewContent, sel);
    }

    // ============================================================================
    // SHARED RENDERERS
    // ============================================================================

    /// <summary>Floor map with X-axis flipped horizontally (newest racks on the right).</summary>
    private void RenderFloorMap(GameObject content, IList<RackRuntimeInfo> racks, Action<RackRuntimeInfo> onClick, RackRuntimeInfo selected, float zoom = 1f, Predicate<RackRuntimeInfo> isInvalid = null)
    {
        ClearChildren(content.transform);
        var contentRect = content.GetComponent<RectTransform>();
        zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);

        if (racks == null || racks.Count == 0)
        {
            BuildLabel(content.transform, "Keine Racks gefunden.", 18f, TextAlignmentOptions.Center, Color.white);
            contentRect.sizeDelta = new Vector2(600f, 180f);
            return;
        }

        var tileW = 60f * zoom;
        var tileH = 40f * zoom;
        var pad = 10f;

        // ----------------------------------------------------------------------
        // Grid layout: cluster rack world-positions into discrete columns/rows.
        //
        // Racks in the game live on a regular grid but the corridors between rack
        // rows show up as huge empty space when we simply scale world coordinates
        // to pixels. Instead we:
        //   1. Cluster X (column) and Z (row) positions into discrete cells.
        //   2. Detect "corridors" – clusters where the world-space gap to the
        //      previous cluster is significantly larger than the typical inter-
        //      rack distance – and only add a small fixed extra gap for those,
        //      not the full proportional distance.
        //
        // This keeps rows compact while still visually separating physical aisles.
        // ----------------------------------------------------------------------
        const float clusterTolerance = 0.6f; // world-units: positions closer than this are the same column/row
        const float corridorMultiplier = 1.8f;
        var cellGap = 8f; // pixels between adjacent racks in the same row/column
        var corridorGap = Mathf.Max(16f, tileW * 0.35f); // extra pixels when an aisle separates clusters

        var (xClusters, xDeltas) = ClusterAxis(racks.Select(r => r.Position.x), clusterTolerance);
        var (zClusters, zDeltas) = ClusterAxis(racks.Select(r => r.Position.z), clusterTolerance);

        // Use the smallest positive inter-cluster delta across BOTH axes as the
        // baseline "one rack apart" distance. This is robust even when one axis has
        // only a single delta (e.g. only two rack rows separated by a corridor):
        // the X axis usually has many racks per row, providing a tight baseline that
        // we can apply to the Z axis too.
        var baseline = float.PositiveInfinity;
        foreach (var d in xDeltas) if (d > 0.001f && d < baseline) baseline = d;
        foreach (var d in zDeltas) if (d > 0.001f && d < baseline) baseline = d;
        if (float.IsPositiveInfinity(baseline) || baseline <= 0.001f) baseline = 1f;
        var corridorThreshold = baseline * corridorMultiplier;

        var xCorridor = MarkCorridors(xClusters, corridorThreshold);
        var zCorridor = MarkCorridors(zClusters, corridorThreshold);

        // Build cumulative pixel offsets per cluster index
        var xOffsets = BuildAxisOffsets(xClusters.Count, xCorridor, tileW + cellGap, corridorGap);
        var yOffsets = BuildAxisOffsets(zClusters.Count, zCorridor, tileH + cellGap, corridorGap);

        var width = (xClusters.Count > 0 ? xOffsets[^1] : 0f) + tileW + pad * 2f;
        var height = (zClusters.Count > 0 ? yOffsets[^1] : 0f) + tileH + pad * 2f;
        contentRect.sizeDelta = new Vector2(Mathf.Max(width, tileW + pad * 2f), Mathf.Max(height, tileH + pad * 2f));

        foreach (var rack in racks)
        {
            var tile = CreateUiObject($"Tile_{rack.Label}", content.transform);
            var rt = tile.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(tileW, tileH);

            // Map this rack's world position to its cluster index, then to pixels.
            var col = NearestClusterIndex(xClusters, rack.Position.x);
            var row = NearestClusterIndex(zClusters, rack.Position.z);

            // FLIP horizontally: new racks (higher X in world) appear on the right.
            var maxColOffset = xClusters.Count > 0 ? xOffsets[^1] : 0f;
            var posX = pad + (maxColOffset - xOffsets[col]);
            var posY = -pad - yOffsets[row];
            rt.anchoredPosition = new Vector2(posX, posY);

            var isSelected = rack == selected;
            var invalid = isInvalid != null && isInvalid(rack);
            Color baseColor;
            if (invalid)
            {
                // Warm red/orange tone so occupied racks are visually obvious as
                // "not a valid paste target". Selected-but-invalid (shouldn't really
                // happen) gets a slightly brighter shade so it's still distinguishable.
                baseColor = isSelected
                    ? new Color(0.95f, 0.45f, 0.30f, 1f)
                    : new Color(0.78f, 0.32f, 0.22f, 1f);
            }
            else if (isSelected)
            {
                baseColor = new Color(0.20f, 0.62f, 0.92f, 1f);
            }
            else
            {
                baseColor = Color.Lerp(
                    new Color(0.21f, 0.27f, 0.34f, 1f),
                    new Color(0.24f, 0.58f, 0.44f, 1f),
                    rack.TotalSlots == 0 ? 0f : (float)rack.UsedSlots / rack.TotalSlots);
            }

            var img = tile.AddComponent<Image>();
            img.color = baseColor;

            var btn = tile.AddComponent<Button>();
            btn.targetGraphic = img;
            // Use base color for ALL states so Unity's selected-outline ghost effect
            // doesn't appear over the rack tiles.
            var cb = btn.colors;
            cb.normalColor = baseColor;
            cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.10f);
            cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.18f);
            cb.selectedColor = baseColor;
            cb.disabledColor = baseColor;
            cb.fadeDuration = 0.05f;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var rackRef = rack;
            btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(() =>
            {
                try { onClick(rackRef); }
                finally { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
            }));

            // Smaller, more readable labels – the previous sizes scaled with zoom and ended
            // up overlapping/oversized. Keep them small and let the user zoom for detail.
            var titleSize = Mathf.Clamp(9f * zoom, 8f, 12f);
            var subSize = Mathf.Clamp(8f * zoom, 7f, 11f);
            var ttl = BuildLabel(tile.transform, rack.Label, titleSize, TextAlignmentOptions.Center, Color.white);
            ttl.enableWordWrapping = false;
            ttl.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(ttl.rectTransform, new Vector2(2f, tileH * 0.45f), new Vector2(-2f, -2f));
            var sub = BuildLabel(tile.transform, $"{rack.UsedSlots}/{rack.TotalSlots}U", subSize, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.9f));
            sub.enableWordWrapping = false;
            Stretch(sub.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -tileH * 0.55f));
        }
    }

    /// <summary>
    /// Groups consecutive sorted axis values into clusters where neighbouring values
    /// within <paramref name="tolerance"/> belong to the same cluster. Returns the
    /// list of cluster centres along with the consecutive deltas between them, so the
    /// caller can determine a global "typical rack distance" across both axes.
    /// </summary>
    private static (List<float> centres, List<float> deltas) ClusterAxis(IEnumerable<float> values, float tolerance)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var centres = new List<float>();
        var clusterSums = new List<float>();
        var clusterCounts = new List<int>();

        foreach (var v in sorted)
        {
            if (centres.Count == 0 || v - centres[centres.Count - 1] > tolerance)
            {
                centres.Add(v);
                clusterSums.Add(v);
                clusterCounts.Add(1);
            }
            else
            {
                var last = centres.Count - 1;
                clusterSums[last] += v;
                clusterCounts[last]++;
                centres[last] = clusterSums[last] / clusterCounts[last];
            }
        }

        var deltas = new List<float>();
        for (var i = 1; i < centres.Count; i++) deltas.Add(centres[i] - centres[i - 1]);
        return (centres, deltas);
    }

    /// <summary>
    /// Marks every cluster whose distance to the previous cluster exceeds
    /// <paramref name="threshold"/> as preceded by a corridor.
    /// </summary>
    private static bool[] MarkCorridors(List<float> centres, float threshold)
    {
        var corridor = new bool[centres.Count];
        for (var i = 1; i < centres.Count; i++)
        {
            if (centres[i] - centres[i - 1] > threshold) corridor[i] = true;
        }
        return corridor;
    }

    /// <summary>
    /// Builds cumulative pixel offsets for each cluster index. Adjacent clusters get
    /// <paramref name="cellStep"/> spacing; clusters preceded by a corridor receive an
    /// additional <paramref name="corridorExtra"/> bump.
    /// </summary>
    private static float[] BuildAxisOffsets(int count, bool[] corridorBefore, float cellStep, float corridorExtra)
    {
        var offsets = new float[count];
        var x = 0f;
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                x += cellStep;
                if (i < corridorBefore.Length && corridorBefore[i]) x += corridorExtra;
            }
            offsets[i] = x;
        }
        return offsets;
    }

    private static int NearestClusterIndex(List<float> centres, float value)
    {
        if (centres == null || centres.Count == 0) return 0;
        var bestIdx = 0;
        var bestDist = Mathf.Abs(value - centres[0]);
        for (var i = 1; i < centres.Count; i++)
        {
            var d = Mathf.Abs(value - centres[i]);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        return bestIdx;
    }

    private void RenderRackComponentSidebar(GameObject content, RackRuntimeInfo rack)
    {
        ClearChildren(content.transform);
        if (rack == null)
        {
            BuildLabel(content.transform, "Kein Rack gewählt.", 14f, TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f, 1f));
            return;
        }

        BuildSectionTitle(content.transform, rack.Label);
        BuildLabel(content.transform, $"Belegung: {rack.UsedSlots}/{rack.TotalSlots} U", 13f, TextAlignmentOptions.Left, new Color(0.85f, 0.9f, 1f, 1f));
        BuildLabel(content.transform, $"Geräte: {rack.Devices.Count}", 13f, TextAlignmentOptions.Left, Color.white);
        BuildDivider(content.transform);

        if (rack.TotalSlots <= 0) return;

        var occ = new RackDeviceTemplate[rack.TotalSlots];
        foreach (var d in rack.Devices)
        {
            // Patch panels are visually 2U in this game even when usableObject.sizeInU
            // reports 1, so make sure the rendered occupancy reflects that.
            var size = d.Kind == RackDeviceKind.PatchPanel ? Math.Max(2, d.SizeInU) : Math.Max(1, d.SizeInU);
            for (var s = d.StartIndex; s < Math.Min(rack.TotalSlots, d.StartIndex + size); s++) occ[s] = d;
        }

        for (var slot = rack.TotalSlots - 1; slot >= 0; slot--)
        {
            var d = occ[slot];
            // Compact one-line layout: a single pill containing "U01: SRV · Name"
            string text;
            Color color;
            if (d == null)
            {
                text = $"U{slot + 1:00}  frei";
                color = new Color(0.16f, 0.20f, 0.25f, 0.85f);
            }
            else
            {
                var size = d.Kind == RackDeviceKind.PatchPanel ? Math.Max(2, d.SizeInU) : Math.Max(1, d.SizeInU);
                var top = d.StartIndex + size - 1;
                var name = top == slot
                    ? $"{d.DisplayName}{(string.IsNullOrWhiteSpace(d.Label) ? string.Empty : $" [{d.Label}]")}"
                    : "belegt";
                text = $"U{slot + 1:00}  {ShortDeviceType(d.Kind)} · {name}";
                color = GetDeviceColor(d.Kind);
            }
            BuildPill(content.transform, text, color, -1f);
        }
    }

    private void RenderTemplatePreview(GameObject content, RackTemplate template)
    {
        ClearChildren(content.transform);
        if (template == null)
        {
            BuildLabel(content.transform, "Wähle eine Vorlage links aus, um die Vorschau zu sehen.", 16f, TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f, 1f));
            return;
        }

        BuildSectionTitle(content.transform, template.Name);
        BuildLabel(content.transform, $"Erstellt: {template.CreatedUtc}", 12f, TextAlignmentOptions.Left, new Color(0.7f, 0.78f, 0.92f, 1f));
        BuildLabel(content.transform, $"Quelle: {template.SourceRackLabel}", 13f, TextAlignmentOptions.Left, Color.white);

        // Price estimate block
        var price = RackPlannerService.EstimatePrice(template);
        BuildDivider(content.transform);
        BuildSectionTitle(content.transform, "Preisschätzung");
        BuildLabel(content.transform, $"Geräte (Basis): {price.DeviceBase}", 13f, TextAlignmentOptions.Left, Color.white);
        BuildLabel(content.transform, $"Geräte (1,5x): {price.DeviceAdjusted}", 13f, TextAlignmentOptions.Left, new Color(1f, 0.84f, 0.44f, 1f));
        BuildLabel(content.transform, $"Kabel: {price.CableLength:0.0} m  →  {price.CablePrice}", 13f, TextAlignmentOptions.Left, new Color(0.74f, 0.92f, 0.74f, 1f));
        BuildLabel(content.transform, $"SFP+ Module: {price.SfpCount}  →  {price.SfpPrice}", 13f, TextAlignmentOptions.Left, new Color(0.74f, 0.86f, 1f, 1f));
        BuildLabel(content.transform, $"Gesamt: {price.Total}", 16f, TextAlignmentOptions.Left, new Color(1f, 0.92f, 0.62f, 1f));

        // Blueprint: visual rack column with sized device blocks
        BuildDivider(content.transform);
        BuildSectionTitle(content.transform, "Blueprint");
        BuildBlueprint(content.transform, template);

        // Cable list
        if (template.Cables != null && template.Cables.Count > 0)
        {
            BuildDivider(content.transform);
            BuildSectionTitle(content.transform, "Kabelverbindungen");
            foreach (var c in template.Cables.Take(40))
            {
                var aName = template.Devices.ElementAtOrDefault(c.EndA.DeviceIndex)?.DisplayName ?? "?";
                var bName = template.Devices.ElementAtOrDefault(c.EndB.DeviceIndex)?.DisplayName ?? "?";
                var sfp = c.SfpCount > 0 ? $" · SFP×{c.SfpCount}" : string.Empty;
                BuildLabel(content.transform, $"{aName}:p{c.EndA.PortIndex} ↔ {bName}:p{c.EndB.PortIndex}  ({c.Length:0.0}m{sfp})", 12f, TextAlignmentOptions.Left, new Color(0.86f, 0.92f, 1f, 1f));
            }
            if (template.Cables.Count > 40)
                BuildLabel(content.transform, $"… {template.Cables.Count - 40} weitere", 12f, TextAlignmentOptions.Left, new Color(0.7f, 0.78f, 0.92f, 1f));
        }
    }

    private void BuildBlueprint(Transform parent, RackTemplate template)
    {
        // Determine total slots (max StartIndex + size). Patch panels are 2U.
        int DeviceSize(RackDeviceTemplate d) =>
            d.Kind == RackDeviceKind.PatchPanel ? Math.Max(2, d.SizeInU) : Math.Max(1, d.SizeInU);

        var totalSlots = 0;
        foreach (var d in template.Devices)
            totalSlots = Math.Max(totalSlots, d.StartIndex + DeviceSize(d));
        if (totalSlots <= 0) totalSlots = 24;

        var occ = new RackDeviceTemplate[totalSlots];
        foreach (var d in template.Devices)
            for (var s = Math.Max(0, d.StartIndex); s < Math.Min(totalSlots, d.StartIndex + DeviceSize(d)); s++) occ[s] = d;

        var rack = CreateUiObject("Blueprint", parent);
        var img = rack.AddComponent<Image>();
        img.color = new Color(0.05f, 0.07f, 0.10f, 1f);
        var ol = rack.AddComponent<Outline>();
        ol.effectColor = new Color(0.4f, 0.55f, 0.78f, 0.4f);
        ol.effectDistance = new Vector2(2f, -2f);
        SetPreferredHeight(rack, totalSlots * 18f + 16f);

        for (var slot = totalSlots - 1; slot >= 0; slot--)
        {
            var row = CreateUiObject($"Slot_{slot}", rack.transform);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            var y = -8f - (totalSlots - 1 - slot) * 18f;
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-16f, 17f);

            var d = occ[slot];
            var rowImg = row.AddComponent<Image>();
            rowImg.color = d == null ? new Color(0.10f, 0.12f, 0.15f, 1f) : GetDeviceColor(d.Kind);

            var lbl = BuildLabel(row.transform, d == null ? $"U{slot + 1:00} – frei" : (slot == d.StartIndex + DeviceSize(d) - 1 ? $"U{slot + 1:00}  {ShortDeviceType(d.Kind)} {d.DisplayName}" : $"U{slot + 1:00}  …"), 11f, TextAlignmentOptions.MidlineLeft, Color.white);
            Stretch(lbl.rectTransform, new Vector2(8f, 1f), new Vector2(-4f, -1f));
        }
    }

    // ============================================================================
    // PAGE-WITH-SIDEBAR FRAME
    // ============================================================================

    private (GameObject mainArea, GameObject sidebar) BuildPageWithSidebar(Transform parent, string title, out TextMeshProUGUI header, out TextMeshProUGUI status, bool sidebarOnLeft = false, float sidebarWidth = 220f)
    {
        var page = CreateUiObject("Page", parent);
        Stretch(page.GetComponent<RectTransform>());
        var pImg = page.AddComponent<Image>();
        pImg.color = new Color(0.07f, 0.08f, 0.11f, 0.99f);

        var col = CreateUiObject("Col", page.transform);
        Stretch(col.GetComponent<RectTransform>());
        var colLayout = col.AddComponent<VerticalLayoutGroup>();
        var pad = new RectOffset(); pad.left = 14; pad.right = 14; pad.top = 10; pad.bottom = 12;
        colLayout.padding = pad;
        colLayout.spacing = 8f;
        colLayout.childControlWidth = true; colLayout.childForceExpandWidth = true;
        colLayout.childControlHeight = true; colLayout.childForceExpandHeight = false;

        // Header
        var headerRow = CreateUiObject("Header", col.transform);
        var hl = headerRow.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f; hl.childControlWidth = true; hl.childForceExpandWidth = true; hl.childControlHeight = true;
        SetPreferredHeight(headerRow, 38f);
        header = BuildLabel(headerRow.transform, title, 22f, TextAlignmentOptions.MidlineLeft, Color.white);
        SetFlexibleWidth(header.gameObject, 1f);
        // Back button is added by AddBackButton later

        // Body: horizontal map+sidebar.
        // childForceExpandWidth=true together with flexibleWidth ratios on the children
        // gives us a guaranteed 75% / 25% split (main / sidebar) regardless of screen size.
        var body = CreateUiObject("Body", col.transform);
        var bl = body.AddComponent<HorizontalLayoutGroup>();
        bl.spacing = 10f; bl.childControlWidth = true; bl.childForceExpandWidth = true;
        bl.childControlHeight = true; bl.childForceExpandHeight = true;
        SetFlexibleHeight(body, 1f);
        SetPreferredHeight(body, 600f);

        GameObject mainArea, sidebar;
        if (sidebarOnLeft)
        {
            sidebar = CreateUiObject("Sidebar", body.transform);
            SetPreferredWidth(sidebar, sidebarWidth);
            SetFlexibleWidth(sidebar, 1f);
            mainArea = CreateUiObject("Main", body.transform);
            SetFlexibleWidth(mainArea, 3f); // 3:1 = 75% main / 25% sidebar
        }
        else
        {
            mainArea = CreateUiObject("Main", body.transform);
            SetFlexibleWidth(mainArea, 3f); // 3:1 = 75% main / 25% sidebar
            sidebar = CreateUiObject("Sidebar", body.transform);
            SetPreferredWidth(sidebar, sidebarWidth);
            SetFlexibleWidth(sidebar, 1f);
        }

        var mImg = mainArea.AddComponent<Image>();
        mImg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var mLayout = mainArea.AddComponent<VerticalLayoutGroup>();
        var mPad = new RectOffset(); mPad.left = 8; mPad.right = 8; mPad.top = 8; mPad.bottom = 8;
        mLayout.padding = mPad;
        mLayout.childControlWidth = true; mLayout.childForceExpandWidth = true;
        mLayout.childControlHeight = true; mLayout.childForceExpandHeight = false;

        var sImg = sidebar.AddComponent<Image>();
        sImg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var sLayout = sidebar.AddComponent<VerticalLayoutGroup>();
        var sPad = new RectOffset(); sPad.left = 8; sPad.right = 8; sPad.top = 8; sPad.bottom = 8;
        sLayout.padding = sPad;
        sLayout.childControlWidth = true; sLayout.childForceExpandWidth = true;
        sLayout.childControlHeight = true; sLayout.childForceExpandHeight = false;

        // Status bar at the bottom of the column
        status = BuildLabel(col.transform, string.Empty, 13f, TextAlignmentOptions.TopLeft, new Color(0.92f, 0.95f, 1f, 1f));
        status.enableWordWrapping = true;
        SetPreferredHeight(status.gameObject, 36f);

        return (mainArea, sidebar);
    }

    private void AddBackButton(Transform pageRoot, Transform headerRow)
    {
        // Header was the first child, find it
        // Insert at end of header row
        BuildButton(headerRow, "← Zurück", () => ShowPage(Page.Main), 110f);
    }

    private void AddZoomButtons(Transform headerRow, Func<float> getZoom, Action<float> setZoom)
    {
        // Inserted before the back button. We add them after the back button creation
        // (so they appear to its left because layout group orders by sibling index — back
        // is added first, so place these and reorder).
        var minus = BuildButton(headerRow, "−", () => setZoom(Mathf.Clamp(getZoom() / 1.25f, MinZoom, MaxZoom)), 36f);
        var reset = BuildButton(headerRow, "1×", () => setZoom(1f), 36f);
        var plus = BuildButton(headerRow, "+", () => setZoom(Mathf.Clamp(getZoom() * 1.25f, MinZoom, MaxZoom)), 36f);
        // Make sure these sit just before the back button (which is currently last).
        var backIndex = headerRow.childCount - 4; // back is at last; minus,reset,plus were added before it? no — added after. We added these after AddBackButton was called, so back-button is at index N-4.
        // Reorder: put zoom buttons immediately before the last child (back button).
        var backButton = headerRow.GetChild(headerRow.childCount - 4);
        backButton.SetSiblingIndex(headerRow.childCount - 1);
    }

    /// <summary>
    /// Attaches a mouse-wheel zoom handler to the Viewport child of the given scrollRoot.
    /// The handler is placed on the Viewport (a child of the GameObject that hosts
    /// ScrollRect) so its IScrollHandler intercepts wheel events before ScrollRect on
    /// the parent gets them — i.e. the wheel zooms instead of vertically panning.
    ///
    /// Important: we use a custom <see cref="WheelZoomBehaviour"/> instead of
    /// <see cref="UnityEngine.EventSystems.EventTrigger"/> because EventTrigger
    /// implements every event-handler interface (including drag handlers) and would
    /// prevent click-and-drag panning from reaching the parent ScrollRect.
    /// </summary>
    private void AttachMouseWheelZoom(GameObject scrollRoot, Func<float> getZoom, Action<float> setZoom)
    {
        if (scrollRoot == null) return;
        // The viewport is the first child created by BuildScrollRegion.
        Transform viewport = null;
        for (var i = 0; i < scrollRoot.transform.childCount; i++)
        {
            var ch = scrollRoot.transform.GetChild(i);
            if (ch != null && ch.name == "Viewport") { viewport = ch; break; }
        }
        if (viewport == null) return;

        WheelZoomBehaviour.EnsureRegistered();
        var handler = viewport.gameObject.AddComponent<WheelZoomBehaviour>();
        handler.Target = viewport.GetComponent<RectTransform>();
        handler.OnWheelDelta = dy =>
        {
            try
            {
                if (Mathf.Abs(dy) < 0.001f) return;
                var factor = dy > 0f ? 1.15f : 1f / 1.15f;
                var newZoom = Mathf.Clamp(getZoom() * factor, MinZoom, MaxZoom);
                setZoom(newZoom);
            }
            catch (Exception ex) { MelonLoader.MelonLogger.Warning($"[RackPlanner] Zoom error: {ex.Message}"); }
        };
    }

    // ============================================================================
    // MODAL TEXT INPUT
    // ============================================================================

    private GameObject BuildModal(Transform parent)
    {
        var modal = CreateUiObject("Modal", parent);
        Stretch(modal.GetComponent<RectTransform>());
        var dim = modal.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        var box = CreateUiObject("Box", modal.transform);
        var brt = box.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(520f, 220f);
        var bImg = box.AddComponent<Image>();
        bImg.color = new Color(0.13f, 0.16f, 0.21f, 1f);
        var bOl = box.AddComponent<Outline>();
        bOl.effectColor = new Color(0.4f, 0.55f, 0.78f, 0.6f);
        bOl.effectDistance = new Vector2(2f, -2f);
        var bL = box.AddComponent<VerticalLayoutGroup>();
        var bp = new RectOffset(); bp.left = 18; bp.right = 18; bp.top = 18; bp.bottom = 18;
        bL.padding = bp; bL.spacing = 10f;
        bL.childControlWidth = true; bL.childForceExpandWidth = true;

        _modalPrompt = BuildLabel(box.transform, "Eingabe:", 18f, TextAlignmentOptions.Left, Color.white);
        SetPreferredHeight(_modalPrompt.gameObject, 28f);

        // Input field
        var ifGo = CreateUiObject("Input", box.transform);
        SetPreferredHeight(ifGo, 40f);
        var ifImg = ifGo.AddComponent<Image>();
        ifImg.color = new Color(0.07f, 0.09f, 0.13f, 1f);
        _modalInput = ifGo.AddComponent<TMP_InputField>();
        var textArea = CreateUiObject("TextArea", ifGo.transform);
        Stretch(textArea.GetComponent<RectTransform>(), new Vector2(8f, 4f), new Vector2(-8f, -4f));
        textArea.AddComponent<RectMask2D>();
        var textGo = CreateUiObject("Text", textArea.transform);
        Stretch(textGo.GetComponent<RectTransform>());
        var textTmp = textGo.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize = 16f; textTmp.color = Color.white; textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        var ph = CreateUiObject("Placeholder", textArea.transform);
        Stretch(ph.GetComponent<RectTransform>());
        var phTmp = ph.AddComponent<TextMeshProUGUI>();
        phTmp.fontSize = 16f; phTmp.color = new Color(0.6f, 0.65f, 0.75f, 1f); phTmp.alignment = TextAlignmentOptions.MidlineLeft; phTmp.text = "Name…";
        _modalInput.textViewport = textArea.GetComponent<RectTransform>();
        _modalInput.textComponent = textTmp;
        _modalInput.placeholder = phTmp;

        var row = CreateUiObject("Row", box.transform);
        var rl = row.AddComponent<HorizontalLayoutGroup>();
        rl.spacing = 8f; rl.childControlWidth = true; rl.childForceExpandWidth = true; rl.childControlHeight = true; rl.childForceExpandHeight = true;
        SetPreferredHeight(row, 40f);
        BuildButton(row.transform, "Abbrechen", CloseModal, 0f);
        BuildButton(row.transform, "OK", () =>
        {
            string text = string.Empty;
            try { text = _modalInput != null ? _modalInput.text ?? string.Empty : string.Empty; }
            catch (Exception ex) { MelonLoader.MelonLogger.Warning($"[RackPlanner] Modal read failed: {ex.Message}"); }
            var cb = _modalCallback;
            CloseModal();
            try { cb?.Invoke(text); }
            catch (Exception ex) { MelonLoader.MelonLogger.Error($"[RackPlanner] Modal callback failed: {ex}"); }
        }, 0f);

        return modal;
    }

    private void OpenModal(string prompt, string defaultValue, Action<string> callback)
    {
        if (_modalRoot == null) return;
        try
        {
            // IMPORTANT: activate FIRST. The TMP_InputField was built while the screen
            // root was inactive, so its caret/textArea internals never ran OnEnable.
            // Setting `.text` on an inactive TMP_InputField throws NullReferenceException
            // inside Il2Cpp (silently swallowed by the Button listener), which is why the
            // "Create Template" button used to do nothing.
            _modalRoot.SetActive(true);
            _modalRoot.transform.SetAsLastSibling();
            _modalCallback = callback;
            if (_modalPrompt != null) _modalPrompt.text = prompt;
            if (_modalInput != null)
            {
                try { _modalInput.text = defaultValue ?? string.Empty; }
                catch (Exception inner)
                {
                    MelonLoader.MelonLogger.Warning($"[RackPlanner] Modal input init failed: {inner.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[RackPlanner] OpenModal failed: {ex}");
        }
    }

    private void CloseModal()
    {
        if (_modalRoot != null) _modalRoot.SetActive(false);
        _modalCallback = null;
    }

    // ============================================================================
    // GENERIC HELPERS
    // ============================================================================

    private static float Normalize(float value, float min, float max) =>
        Mathf.Abs(max - min) < 0.001f ? 0.5f : (value - min) / (max - min);

    private static string ShortDeviceType(RackDeviceKind kind) => kind switch
    {
        RackDeviceKind.Server => "SRV",
        RackDeviceKind.NetworkSwitch => "SWT",
        RackDeviceKind.PatchPanel => "PP",
        _ => "DEV"
    };

    private static Color GetDeviceColor(RackDeviceKind kind) => kind switch
    {
        RackDeviceKind.Server => new Color(0.24f, 0.56f, 0.86f, 0.96f),
        RackDeviceKind.NetworkSwitch => new Color(0.23f, 0.74f, 0.45f, 0.96f),
        RackDeviceKind.PatchPanel => new Color(0.58f, 0.42f, 0.86f, 0.96f),
        _ => new Color(0.42f, 0.42f, 0.42f, 0.96f)
    };

    private static GameObject BuildContainer(Transform parent, float preferredHeight)
    {
        var c = CreateUiObject("Container", parent);
        var img = c.AddComponent<Image>();
        img.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        var l = c.AddComponent<VerticalLayoutGroup>();
        var p = new RectOffset(); p.left = 8; p.right = 8; p.top = 8; p.bottom = 8;
        l.padding = p; l.spacing = 6f;
        l.childControlWidth = true; l.childForceExpandWidth = true;
        l.childControlHeight = true; l.childForceExpandHeight = false;
        if (preferredHeight > 0f) SetPreferredHeight(c, preferredHeight); else SetFlexibleHeight(c, 1f);
        return c;
    }

    private static GameObject BuildScrollRegion(Transform parent, out GameObject content, float preferredHeight,
        bool horizontal, bool vertical, bool useLayoutOnContent)
    {
        var scrollRoot = CreateUiObject("ScrollRoot", parent);
        if (preferredHeight > 0f) SetPreferredHeight(scrollRoot, preferredHeight);
        SetFlexibleWidth(scrollRoot, 1f);
        SetFlexibleHeight(scrollRoot, 1f);

        var viewport = CreateUiObject("Viewport", scrollRoot.transform);
        var vrt = viewport.GetComponent<RectTransform>();
        Stretch(vrt);
        var vimg = viewport.AddComponent<Image>();
        vimg.color = new Color(0f, 0f, 0f, 0.005f);
        vimg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        content = CreateUiObject("Content", viewport.transform);
        var crt = content.GetComponent<RectTransform>();
        if (horizontal && vertical)
        {
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
        }
        else if (vertical)
        {
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
        }
        else
        {
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
        }
        crt.sizeDelta = Vector2.zero;
        crt.anchoredPosition = Vector2.zero;

        if (useLayoutOnContent)
        {
            if (vertical && !horizontal)
            {
                var vlg = content.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4f;
                vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
                vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
                var f = content.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            else if (horizontal && !vertical)
            {
                var hlg = content.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6f;
                hlg.childControlWidth = false; hlg.childForceExpandWidth = false;
                hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
                var f = content.AddComponent<ContentSizeFitter>();
                f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                f.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        var sr = scrollRoot.AddComponent<ScrollRect>();
        sr.viewport = vrt;
        sr.content = crt;
        sr.horizontal = horizontal;
        sr.vertical = vertical;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 24f;
        sr.inertia = true;
        sr.decelerationRate = 0.135f;
        return scrollRoot;
    }

    private static Button BuildButton(Transform parent, string text, Action action, float width)
    {
        var bo = CreateUiObject(text.Replace(' ', '_'), parent);
        var le = bo.AddComponent<LayoutElement>();
        if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
        else le.flexibleWidth = 1f;
        le.preferredHeight = 34f;
        var img = bo.AddComponent<Image>();
        img.color = new Color(0.21f, 0.28f, 0.37f, 1f);
        var b = bo.AddComponent<Button>();
        b.targetGraphic = img;
        ConfigureButtonColors(b, img.color);
        // Disable keyboard / focus navigation so Unity does not draw the "selected"
        // outline on the most recently clicked button (the "ghost outline" bug).
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(() =>
        {
            try { action(); }
            finally
            {
                // Drop selection right after the click so OnDeselect fires immediately
                // and the highlighted/selected colour is cleared.
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
        }));
        var lbl = BuildLabel(bo.transform, text, 14f, TextAlignmentOptions.Center, Color.white);
        Stretch(lbl.rectTransform);
        return b;
    }

    private static void ConfigureButtonColors(Button b, Color baseColor)
    {
        var c = b.colors;
        c.normalColor = baseColor;
        c.highlightedColor = Color.Lerp(baseColor, Color.white, 0.14f);
        c.pressedColor = Color.Lerp(baseColor, Color.black, 0.22f);
        // selectedColor == normal so that Unity's "last-clicked" highlight is invisible
        // (eliminates the ghost outline on the previously-clicked button).
        c.selectedColor = baseColor;
        c.disabledColor = baseColor;
        c.fadeDuration = 0.06f;
        b.colors = c;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent, string text, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        var lo = CreateUiObject("Label", parent);
        var l = lo.AddComponent<TextMeshProUGUI>();
        l.text = text;
        l.fontSize = fontSize;
        l.alignment = alignment;
        l.color = color;
        l.enableWordWrapping = true;
        l.raycastTarget = false;
        return l;
    }

    private static TextMeshProUGUI BuildSectionTitle(Transform parent, string text)
    {
        var l = BuildLabel(parent, text, 18f, TextAlignmentOptions.Left, Color.white);
        SetPreferredHeight(l.gameObject, 24f);
        return l;
    }

    private static void BuildDivider(Transform parent)
    {
        var d = CreateUiObject("Divider", parent);
        var i = d.AddComponent<Image>();
        i.color = new Color(1f, 1f, 1f, 0.12f);
        SetPreferredHeight(d, 2f);
    }

    private static GameObject BuildPill(Transform parent, string text, Color color, float preferredWidth)
    {
        var p = CreateUiObject("Pill", parent);
        var i = p.AddComponent<Image>();
        i.color = color;
        if (preferredWidth > 0f) SetPreferredWidth(p, preferredWidth); else SetFlexibleWidth(p, 1f);
        SetPreferredHeight(p, 18f);
        var lbl = BuildLabel(p.transform, text, 12f, TextAlignmentOptions.Center, Color.white);
        Stretch(lbl.rectTransform, new Vector2(6f, 1f), new Vector2(-6f, -1f));
        return p;
    }

    private static void ClearChildren(Transform t)
    {
        for (var i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
    }

    private static GameObject CreateUiObject(string name, Transform parent = null)
    {
        var go = new GameObject(name, Il2CppType.Of<RectTransform>());
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }
    private static void Stretch(RectTransform r, Vector2 min, Vector2 max)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = min; r.offsetMax = max;
    }
    private static void SetPreferredHeight(GameObject g, float v)
    {
        var le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.preferredHeight = v;
    }
    private static void SetPreferredWidth(GameObject g, float v)
    {
        var le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.preferredWidth = v;
    }
    private static void SetFlexibleWidth(GameObject g, float v)
    {
        var le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.flexibleWidth = v;
    }
    private static void SetFlexibleHeight(GameObject g, float v)
    {
        var le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.flexibleHeight = v;
    }
}
