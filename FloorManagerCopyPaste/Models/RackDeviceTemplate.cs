using System.Collections.Generic;
using Il2Cpp;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackDeviceTemplate
{
    public RackDeviceKind Kind { get; init; }
    public int StartIndex { get; init; }
    public int SizeInU { get; init; }
    public int PrefabId { get; init; }
    public int VariantId { get; init; }
    public int BasePrice { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsPoweredOn { get; init; }

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
    public int ServerType { get; init; }

    /// <summary>
    /// Per-port VLAN exclusions shared by switches, routers, and firewalls.
    /// The game persists these on <see cref="SwitchSaveData"/>.
    /// </summary>
    public List<NetworkPortVlanFilterTemplate> PortVlanFilters { get; init; } = [];

    public int RouterAsn { get; init; }
    public int RouterNextRouteId { get; init; } = 1;
    public List<RouterOwnedSubnetTemplate> RouterOwnedSubnets { get; init; } = [];
    public List<RouterRouteTemplate> RouterRoutes { get; init; } = [];

    public string FirewallClusterIp { get; init; } = string.Empty;
    public List<FirewallRuleTemplate> FirewallRules { get; init; } = [];

    /// <summary>
    /// Device's transform.position expressed in the SOURCE rack's local space.
    /// On apply we restore the spawned device to <c>targetRack.TransformPoint(LocalPos)</c>
    /// which guarantees pixel-perfect alignment regardless of how the prefab's pivot
    /// is authored or how the rack-position anchors are laid out.
    /// </summary>
    public Vec3 LocalPos { get; init; }

    /// <summary>Device's transform.localRotation Euler in source rack's local space.</summary>
    public Vec3 LocalEuler { get; init; }
}
