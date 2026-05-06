using System;
using MelonLoader;
using UnityEngine.InputSystem;

namespace FloorManagerCopyPaste.Services;

/// <summary>
/// Hotkey entrypoint for lightweight maintenance actions.
/// </summary>
internal static class DiagnosticsService
{
    public static void HandleHotkeys()
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f6Key.wasPressedThisFrame)
            {
                RackPlannerService.RepairOrphanedPastedDevices();
            }
        }
        catch (Exception ex)
        {
            try { MelonLogger.Warning($"[RackPlanner] Reparatur-Hotkey fehlgeschlagen: {ex.Message}"); } catch { /* swallow */ }
        }
    }
}
