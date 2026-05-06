using HarmonyLib;
using DataCenterLaptopButtonMod.UI;
using Il2Cpp;

namespace DataCenterLaptopButtonMod.Patches;

[HarmonyPatch(typeof(ComputerShop), "ButtonReturnMainScreen")]
public static class ReturnMainScreenPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        RackPlannerScreenController.Instance.Close();
    }
}


