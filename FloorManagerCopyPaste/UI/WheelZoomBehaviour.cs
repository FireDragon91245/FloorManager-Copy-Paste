using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FloorManagerCopyPaste.UI;

/// <summary>
/// Minimal MonoBehaviour that watches the mouse wheel in <see cref="Update"/>
/// and forwards the vertical scroll delta to a managed callback whenever the
/// cursor is over a target <see cref="RectTransform"/>.
///
/// We deliberately do NOT implement <c>IScrollHandler</c> nor use
/// <c>UnityEngine.EventSystems.EventTrigger</c>:
///   * EventTrigger implements every event-handler interface (including drag
///     handlers) and would block click-and-drag panning on the parent ScrollRect.
///   * Implementing <c>IScrollHandler</c> on an Il2Cpp-injected managed class
///     is fragile – Il2Cpp's <c>ExecuteEvents</c> pipeline does not always
///     route to the injected interface, so wheel events silently never fire.
///
/// Polling <see cref="Input.mouseScrollDelta"/> sidesteps both problems and
/// keeps wheel-zoom + drag-pan working in tandem.
/// </summary>
internal sealed class WheelZoomBehaviour : MonoBehaviour
{
    private static bool _registered;

    /// <summary>Wheel events are only forwarded when the mouse is inside this rect.</summary>
    public RectTransform Target;

    /// <summary>Managed callback invoked with the vertical scroll delta.</summary>
    public Action<float> OnWheelDelta;

    // Required ctor for Il2Cpp-injected MonoBehaviours.
    public WheelZoomBehaviour(IntPtr ptr) : base(ptr)
    {
    }

    public static void EnsureRegistered()
    {
        if (_registered) return;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<WheelZoomBehaviour>();
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"WheelZoomBehaviour register failed: {ex.Message}");
        }

        _registered = true;
    }

    public void Update()
    {
        try
        {
            if (!Target || OnWheelDelta is null) return;

            // Use the new Input System (legacy UnityEngine.Input throws because the
            // game's Player Settings has switched to "Input System Package").
            var mouse = Mouse.current;
            if (mouse is null) return;

            var dy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(dy) < 0.001f) return;

            // Only react when the cursor is actually over our target rect.
            // Passing null camera works for ScreenSpaceOverlay canvases (which
            // is what the laptop UI uses).
            var mousePos = mouse.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(Target, mousePos, null))
                return;

            // Mouse-wheel scroll deltas in the new Input System are typically
            // ±120 per notch (Windows raw HID values). Normalise to ±1 so the
            // zoom factor stays reasonable.
            var normalised = Mathf.Clamp(dy / 120f, -3f, 3f);
            if (Mathf.Abs(normalised) < 0.001f) normalised = Mathf.Sign(dy);
            OnWheelDelta(normalised);
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"WheelZoom Update error: {ex.Message}");
        }
    }
}