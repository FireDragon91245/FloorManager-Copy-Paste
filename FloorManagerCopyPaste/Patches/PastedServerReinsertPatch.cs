using System;
using FloorManagerCopyPaste.Services;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace FloorManagerCopyPaste.Patches;

[HarmonyPatch(typeof(Server), nameof(Server.ServerInsertedInRack))]
internal static class PastedServerReinsertPatch
{
    [HarmonyPostfix]
    private static void Postfix(Server __instance)
    {
        try
        {
            RackPlannerService.RefreshPastedServerRuntimeState(__instance);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Pasted server rack-reinsertion repair failed: {ex.Message}");
        }
    }
}
