using System;
using MelonLoader;
using UnityEngine.InputSystem;

namespace DataCenterLaptopButtonMod.Services;

/// <summary>
/// Hotkey entrypoint for read-only deep diagnostics.
/// F7 captures a baseline snapshot, F8 dumps current state and diffs against F7.
/// F6 remains the existing manual repair hotkey and is not used by the diagnostics.
/// </summary>
internal static class DiagnosticsService
{
    public static void HandleHotkeys()
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f8Key.wasPressedThisFrame)
            {
                DeepDiagnosticsService.DumpAndDiff("F8");
            }
            else if (kb.f7Key.wasPressedThisFrame)
            {
                DeepDiagnosticsService.CaptureBaseline("F7");
            }
            else if (kb.f6Key.wasPressedThisFrame)
            {
                MelonLogger.Msg("[Diagnostics][F6] Running RepairOrphanedPastedDevices…");
                var n = RackPlannerService.RepairOrphanedPastedDevices();
                MelonLogger.Msg($"[Diagnostics][F6] Repair finished. Relinked {n} device(s). Save now to persist.");
            }
        }
        catch (Exception ex)
        {
            try { MelonLogger.Warning($"[Diagnostics] HandleHotkeys failed: {ex.Message}"); } catch { /* swallow */ }
        }
    }
}
