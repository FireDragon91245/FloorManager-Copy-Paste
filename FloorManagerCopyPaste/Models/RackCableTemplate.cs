using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackCableTemplate
{
    public RackCableEndpoint EndA { get; init; } = new();
    public RackCableEndpoint EndB { get; init; } = new();
    public float Speed { get; init; }
    public float Length { get; init; }

    /// <summary>
    /// Source-rack-LOCAL positions of every link/hook the original cable was routed
    /// through – including the two endpoint attach points. Used to recreate the
    /// cable on a target rack with the same prefab. Position [0] is end A's attach
    /// point, the last position is end Bs attach point, and everything in between
    /// is a hook/cable-management Transform local to the rack.
    /// </summary>
    public List<Vec3> LocalRoute { get; init; } = [];
    /// <summary>
    /// True when the entire cable (every endpoint and every hook in <see cref="LocalRoute"/>)
    /// stays inside the source rack's local AABB. Cables that leave the rack are
    /// not safe to clone and should be skipped.
    /// </summary>
    public bool FullyInsideSourceRack { get; init; }
    public int TypeA { get; init; }
    public int TypeB { get; init; }
    public int SfpTypeA { get; init; } = -1;
    public int SfpTypeB { get; init; } = -1;
    public float ColorR { get; init; } = 1f;
    public float ColorG { get; init; } = 1f;
    public float ColorB { get; init; } = 1f;
    public float ColorA { get; init; } = 1f;

    public int SfpCount => (SfpTypeA >= 0 ? 1 : 0) + (SfpTypeB >= 0 ? 1 : 0);
}