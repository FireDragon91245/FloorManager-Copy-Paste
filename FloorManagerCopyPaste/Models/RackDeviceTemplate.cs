using System.Collections.Generic;
using Il2Cpp;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackDeviceTemplate
{
    public RackDeviceKind Kind { get; init; }
    public int StartIndex { get; init; }
    public int SizeInU { get; init; }
    public int PrefabId { get; init; }
    /// <summary>
    /// Exact index of the source prefab in its MainGameManager device array.
    /// prefabID and serverType are not interchangeable in the updated game.
    /// </summary>
    public int PrefabArrayIndex { get; init; } = -1;
    public int VariantId { get; init; }
    public int BasePrice { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsPoweredOn { get; init; }

    /// <summary>
    /// Server-only operational type captured from the source. This is NOT the index
    /// in MainGameManager.serverPrefabs: the current 5U server is hardware prefab 1
    /// while its operational serverType is 0. The route evaluator uses this value
    /// when assigning the customer's application.
    ///
    /// Note: we intentionally DO NOT capture/replay IP, customerID or appID — a pasted
    /// server should behave like a freshly-bought one (its own IP, its own customer).
    /// </summary>
    public int ServerType { get; init; }

    /// <summary>
    /// Distinguishes newly captured templates from legacy JSON where ServerType was
    /// incorrectly populated with PrefabArrayIndex. For legacy templates the spawned
    /// prefab's serialized serverType is authoritative.
    /// </summary>
    public bool HasOperationalServerType { get; init; }

    /// <summary>
    /// Normalized runtime processing capacity serialized on the source prefab (0.12
    /// is displayed by the game as 12,000 for the current 5U server). ServerSaveData
    /// does not persist this field: vanilla gets it back by reconstructing the prefab
    /// on load. Preserve it explicitly so a freshly pasted live instance is usable
    /// before the first reload.
    /// </summary>
    public float ServerMaxProcessingSpeed { get; init; }

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

    /// <summary>
    /// Explicit validity bit because a zero position/rotation can be a legitimate
    /// rack-local pose and must not be mistaken for missing legacy data.
    /// </summary>
    public bool HasLocalPose { get; init; }

    /// <summary>Exact rack-relative quaternion; avoids Euler ambiguity at a 180° rear mount.</summary>
    public float LocalRotationX { get; init; }
    public float LocalRotationY { get; init; }
    public float LocalRotationZ { get; init; }
    public float LocalRotationW { get; init; } = 1f;
}
