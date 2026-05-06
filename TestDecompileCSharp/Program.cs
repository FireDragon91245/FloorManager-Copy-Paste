using System.Reflection;
using DataCenterLaptopButtonMod.Services;
using MelonLoader;
using HarmonyLib;
using UnityEngine;

[assembly: MelonInfo(typeof(DataCenterLaptopButtonMod.LaptopButtonMod), "Data Center Laptop Button Mod", "0.2.4", "GitHub Copilot")]
[assembly: MelonGame("Waseku", "Data Center")]
[assembly: MelonOptionalDependencies("UnityEngine.CoreModule", "UnityEngine.UIModule", "UnityEngine.UI", "UnityEngine.TextRenderingModule", "Unity.TextMeshPro")]

namespace DataCenterLaptopButtonMod;

public sealed class LaptopButtonMod : MelonMod
{
	internal const string DemoScreenObjectName = "CopilotRackPlannerScreen";
	internal const string DemoButtonObjectName = "CopilotRackPlannerButton";

	internal static GameObject DemoScreen { get; set; }
	internal static GameObject MainScreen { get; set; }

	public override void OnInitializeMelon()
	{
		HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
		RackPlannerService.InitializeSaveDiagnostics();
		LoggerInstance.Msg("Demo-Laptop-Mod initialisiert. Hotkeys: F8 = state dump, F7 = baseline.");
	}

	public override void OnUpdate()
	{
		DiagnosticsService.HandleHotkeys();
	}
}
