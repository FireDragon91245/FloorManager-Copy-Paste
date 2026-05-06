using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using DataCenterLaptopButtonMod.UI;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DataCenterLaptopButtonMod.Patches;

[HarmonyPatch(typeof(ComputerShop), "Awake")]
public static class ComputerShopAwakePatch
{
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedMember.Global
    public static void Postfix(ComputerShop __instance)
    {
        try
        {
            var canvasComputerShop = __instance.canvasComputerShop;
            var mainScreen = __instance.mainScreen;
            LaptopButtonMod.MainScreen = mainScreen;

            if (canvasComputerShop == null || mainScreen == null)
            {
                MelonLogger.Warning("[LaptopDemo] canvasComputerShop oder mainScreen war null; Button-Injektion übersprungen.");
                return;
            }

            var layoutGroup = mainScreen.GetComponentInChildren<LayoutGroup>();
            if (layoutGroup == null)
            {
                MelonLogger.Warning("[LaptopDemo] Kein LayoutGroup-Knoten im Laptop-Hauptscreen gefunden.");
                return;
            }

            if (layoutGroup.transform.Find(LaptopButtonMod.DemoButtonObjectName) != null)
                return;

            var button = BuildAppButton(layoutGroup.transform, "RACK");
            button.gameObject.name = LaptopButtonMod.DemoButtonObjectName;
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(() => OpenDemoScreen(__instance, mainScreen)));
            MelonLogger.Msg("[LaptopDemo] Rack-Planner-Button im Laptop eingefügt.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[LaptopDemo] Fehler in ComputerShop.Awake-Postfix: {ex}");
        }
    }

    private static void OpenDemoScreen(ComputerShop computerShop, GameObject mainScreen)
    {
        RackPlannerScreenController.Instance.Open(computerShop, mainScreen);
    }

    private static Button BuildAppButton(Transform parent, string label)
    {
        var buttonObject = new GameObject(LaptopButtonMod.DemoButtonObjectName);
        buttonObject.transform.SetParent(parent, false);

        var buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(100f, 100f);

        var background = buttonObject.AddComponent<Image>();
        background.color = new Color(0.72f, 0.72f, 0.72f, 1f);

        var outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var icon = new GameObject("Icon");
        icon.transform.SetParent(buttonObject.transform, false);
        var iconRect = icon.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.15f, 0.25f);
        iconRect.anchorMax = new Vector2(0.85f, 0.9f);
        iconRect.sizeDelta = Vector2.zero;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        var iconImage = icon.AddComponent<Image>();
        iconImage.color = new Color(0.15f, 0.15f, 0.18f, 1f);
        BuildGridLine(icon.transform, 0.5f, true);
        BuildGridLine(icon.transform, 0.5f, false);

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0.22f);
        labelRect.sizeDelta = Vector2.zero;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var text = labelObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 11f;
        text.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        text.alignment = TextAlignmentOptions.Center;

        return button;
    }

    private static void BuildGridLine(Transform parent, float anchor, bool vertical)
    {
        var lineObject = new GameObject("GridLine");
        lineObject.transform.SetParent(parent, false);

        var rectTransform = lineObject.AddComponent<RectTransform>();
        if (vertical)
        {
            rectTransform.anchorMin = new Vector2(anchor, 0f);
            rectTransform.anchorMax = new Vector2(anchor, 1f);
            rectTransform.sizeDelta = new Vector2(2f, 0f);
        }
        else
        {
            rectTransform.anchorMin = new Vector2(0f, anchor);
            rectTransform.anchorMax = new Vector2(1f, anchor);
            rectTransform.sizeDelta = new Vector2(0f, 2f);
        }

        rectTransform.anchoredPosition = Vector2.zero;
        var image = lineObject.AddComponent<Image>();
        image.color = new Color(0.45f, 0.45f, 0.45f, 0.6f);
    }
}


