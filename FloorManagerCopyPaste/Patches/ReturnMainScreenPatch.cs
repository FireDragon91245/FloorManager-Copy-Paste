using HarmonyLib;
using FloorManagerCopyPaste.UI;
using Il2Cpp;

namespace FloorManagerCopyPaste.Patches;

[HarmonyPatch(typeof(ComputerShop), "ButtonReturnMainScreen")]
public static class ReturnMainScreenPatch
{
    [HarmonyPostfix]
    // ReSharper disable once UnusedMember.Global
    public static void Postfix()
    {
        RackPlannerScreenController.Instance.Close();
    }
}