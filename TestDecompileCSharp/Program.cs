using System.Reflection;
using FloorManagerCopyPaste.Services;
using MelonLoader;
using HarmonyLib;
using UnityEngine;

[assembly: MelonInfo(typeof(FloorManagerCopyPaste.FloorManagerCopyPasteMod), "Floor Manager: Copy & Paste", "0.2.5", "FireDragon91245")]
[assembly: MelonGame("Waseku", "Data Center")]
[assembly: MelonOptionalDependencies("UnityEngine.CoreModule", "UnityEngine.UIModule", "UnityEngine.UI", "UnityEngine.TextRenderingModule", "Unity.TextMeshPro")]

namespace FloorManagerCopyPaste;

public sealed class FloorManagerCopyPasteMod : MelonMod
{
	internal const string FloorManagerScreenObjectName = "FloorManagerScreen";
	internal const string FloorManagerButtonObjectName = "FloorManagerButton";

	internal static GameObject FloorManagerScreen { get; set; }
	internal static GameObject MainScreen { get; set; }

	public override void OnInitializeMelon()
	{
		HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
		RackPlannerService.InitializeSaveDiagnostics();
		LoggerInstance.Msg("Floor Manager: Copy & Paste initialisiert. F6 repariert verwaiste Rack-Geräte.");
	}

	public override void OnUpdate()
	{
		DiagnosticsService.HandleHotkeys();
	}
}
