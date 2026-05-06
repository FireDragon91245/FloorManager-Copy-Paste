using HarmonyLib;
using DataCenterLaptopButtonMod.UI;
using Il2Cpp;

namespace DataCenterLaptopButtonMod.Patches;

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


