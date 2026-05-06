using System;
using DataCenterLaptopButtonMod.Services;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace DataCenterLaptopButtonMod.Patches;

/// <summary>
/// Read-only trace hooks around vanilla rack mounting methods. These patches only log
/// state before/after the original methods; they never change arguments or return values.
/// </summary>
[HarmonyPatch(typeof(RackPosition), "InsertItemInRack")]
public static class RackPositionInsertItemInRackDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(RackPosition __instance)
    {
        try { DeepDiagnosticsService.LogRackPositionInsertPrefix(__instance); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][RackPosition.InsertItemInRack][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(RackPosition __instance)
    {
        try { DeepDiagnosticsService.LogRackPositionInsertPostfix(__instance); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][RackPosition.InsertItemInRack][Postfix] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Server), "ServerInsertedInRack")]
public static class ServerInsertedInRackDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Server __instance, ServerSaveData serverSaveData)
    {
        try { DeepDiagnosticsService.LogServerInsertedPrefix(__instance, serverSaveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][Server.ServerInsertedInRack][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(Server __instance, ServerSaveData serverSaveData)
    {
        try { DeepDiagnosticsService.LogServerInsertedPostfix(__instance, serverSaveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][Server.ServerInsertedInRack][Postfix] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(NetworkSwitch), "SwitchInsertedInRack")]
public static class SwitchInsertedInRackDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(NetworkSwitch __instance, SwitchSaveData switchSaveData)
    {
        try { DeepDiagnosticsService.LogSwitchInsertedPrefix(__instance, switchSaveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][NetworkSwitch.SwitchInsertedInRack][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(NetworkSwitch __instance, SwitchSaveData switchSaveData)
    {
        try { DeepDiagnosticsService.LogSwitchInsertedPostfix(__instance, switchSaveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][NetworkSwitch.SwitchInsertedInRack][Postfix] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(PatchPanel), "InsertedInRack")]
public static class PatchPanelInsertedInRackDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(PatchPanel __instance, PatchPanelSaveData saveData)
    {
        try { DeepDiagnosticsService.LogPatchPanelInsertedPrefix(__instance, saveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][PatchPanel.InsertedInRack][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(PatchPanel __instance, PatchPanelSaveData saveData)
    {
        try { DeepDiagnosticsService.LogPatchPanelInsertedPostfix(__instance, saveData); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][PatchPanel.InsertedInRack][Postfix] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(Rack), "MarkPositionAsUsed")]
public static class RackMarkPositionAsUsedDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Rack __instance, int index, int sizeInU)
    {
        try { DeepDiagnosticsService.LogRackMarkPositionAsUsedPrefix(__instance, index, sizeInU); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][Rack.MarkPositionAsUsed][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(Rack __instance, int index, int sizeInU)
    {
        try { DeepDiagnosticsService.LogRackMarkPositionAsUsedPostfix(__instance, index, sizeInU); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][Rack.MarkPositionAsUsed][Postfix] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(RackPosition), "SetUsed")]
public static class RackPositionSetUsedDiagnosticsPatch
{
    [HarmonyPrefix]
    public static void Prefix(RackPosition __instance, bool used)
    {
        try { DeepDiagnosticsService.LogRackPositionSetUsedPrefix(__instance, used); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][RackPosition.SetUsed][Prefix] {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void Postfix(RackPosition __instance, bool used)
    {
        try { DeepDiagnosticsService.LogRackPositionSetUsedPostfix(__instance, used); }
        catch (Exception ex) { MelonLogger.Warning($"[DeepDiag][Patch][RackPosition.SetUsed][Postfix] {ex.Message}"); }
    }
}

