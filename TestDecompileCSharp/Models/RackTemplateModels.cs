using System;
using System.Collections.Generic;

namespace DataCenterLaptopButtonMod.Models;

internal enum RackDeviceKind
{
    Server,
    NetworkSwitch,
    PatchPanel
}

/// <summary>JSON-friendly Vector3 replacement.</summary>
internal struct Vec3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public static Vec3 From(UnityEngine.Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
    public UnityEngine.Vector3 ToUnity() => new(X, Y, Z);
}

internal sealed class RackTemplate
{
    public string Name { get; set; } = string.Empty;
    public string SourceRackLabel { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public List<RackDeviceTemplate> Devices { get; set; } = new();
    public List<RackCableTemplate> Cables { get; set; } = new();
}

internal sealed class RackDeviceTemplate
{
    public RackDeviceKind Kind { get; set; }
    public int StartIndex { get; set; }
    public int SizeInU { get; set; }
    public int PrefabId { get; set; }
    public int VariantId { get; set; }
    public int BasePrice { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsPoweredOn { get; set; }

    /// <summary>
    /// Server-only state captured from the source so we can replay it onto a freshly
    /// instantiated server. <see cref="Server.ServerInsertedInRack"/> with null saveData
    /// generates a ServerID and assigns a customer but DOES NOT initialise serverType —
    /// that is normally set by ComputerShop when picking the right prefab. Without
    /// it the LOAD path on save→reload treats the entry as uninitialised and discards it.
    ///
    /// Note: we intentionally DO NOT capture/replay IP, customerID or appID — a pasted
    /// server should behave like a freshly-bought one (its own IP, its own customer).
    /// </summary>
    public int ServerType { get; set; }

    /// <summary>
    /// Device's transform.position expressed in the SOURCE rack's local space.
    /// On apply we restore the spawned device to <c>targetRack.TransformPoint(LocalPos)</c>
    /// which guarantees pixel-perfect alignment regardless of how the prefab's pivot
    /// is authored or how the rack-position anchors are laid out.
    /// </summary>
    public Vec3 LocalPos { get; set; }

    /// <summary>Device's transform.localRotation Euler in source rack's local space.</summary>
    public Vec3 LocalEuler { get; set; }
}

internal sealed class RackCableTemplate
{
    public RackCableEndpoint EndA { get; set; } = new();
    public RackCableEndpoint EndB { get; set; } = new();
    public float Speed { get; set; }
    public float Length { get; set; }
    /// <summary>World-space waypoints (legacy, kept for stats/UI).</summary>
    public List<Vec3> Waypoints { get; set; } = new();
    /// <summary>
    /// Source-rack-LOCAL positions of every link/hook the original cable was routed
    /// through – including the two endpoint attach points. Used to recreate the
    /// cable on a target rack with the same prefab. Position [0] is end A's attach
    /// point, the last position is end B's attach point, and everything in between
    /// is a hook/cable-management Transform local to the rack.
    /// </summary>
    public List<Vec3> LocalRoute { get; set; } = new();
    /// <summary>
    /// True when the entire cable (every endpoint and every hook in <see cref="LocalRoute"/>)
    /// stays inside the source rack's local AABB. Cables that leave the rack are
    /// not safe to clone and should be skipped.
    /// </summary>
    public bool FullyInsideSourceRack { get; set; }
    public int TypeA { get; set; }
    public int TypeB { get; set; }
    public int SfpTypeA { get; set; } = -1;
    public int SfpTypeB { get; set; } = -1;
    public float ColorR { get; set; } = 1f;
    public float ColorG { get; set; } = 1f;
    public float ColorB { get; set; } = 1f;
    public float ColorA { get; set; } = 1f;

    public int SfpCount => (SfpTypeA >= 0 ? 1 : 0) + (SfpTypeB >= 0 ? 1 : 0);
}

internal sealed class RackCableEndpoint
{
    public int DeviceIndex { get; set; } = -1;
    public int PortIndex { get; set; }
    public RackDeviceKind Kind { get; set; }
}

internal sealed class RackRuntimeInfo
{
    public string Label { get; init; } = string.Empty;
    public UnityEngine.Vector3 Position { get; init; }
    public Il2Cpp.Rack Rack { get; init; }
    public IReadOnlyList<RackDeviceTemplate> Devices { get; init; } = Array.Empty<RackDeviceTemplate>();
    public int TotalSlots { get; init; }
    public int UsedSlots { get; init; }
}

internal sealed class RackApplyPreview
{
    public RackTemplate Template { get; init; } = new();
    public RackRuntimeInfo TargetRack { get; init; } = new();
    public List<RackDeviceTemplate> Purchases { get; } = new();
    public List<string> Conflicts { get; } = new();
    public List<RackDeviceTemplate> MatchingDevices { get; } = new();
    public int BaseCost { get; set; }
    public int AdjustedCost { get; set; }
    public int CableCount { get; set; }
    public bool CanApply => Conflicts.Count == 0 && Purchases.Count > 0;
}

internal sealed class RackApplyResult
{
    public int SpawnedCount { get; set; }
    public int ChargedAmount { get; set; }
    public int CablesCreated { get; set; }
    public int CablesFailed { get; set; }
    public List<string> Messages { get; } = new();
}

internal sealed class TemplatePriceEstimate
{
    public int DeviceBase { get; set; }
    public int DeviceAdjusted { get; set; }
    public float CableLength { get; set; }
    public int CablePrice { get; set; }
    public int SfpCount { get; set; }
    public int SfpPrice { get; set; }
    public int Total => DeviceAdjusted + CablePrice + SfpPrice;
}
