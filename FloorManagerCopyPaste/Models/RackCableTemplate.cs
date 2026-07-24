using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackCableTemplate
{
    public RackCableEndpoint EndA { get; init; } = new();
    public RackCableEndpoint EndB { get; init; } = new();
    public float Speed { get; init; }
    public float Length { get; init; }

    /// <summary>
    /// Source-rack-local positions of the cable's complete generated centreline,
    /// including its two endpoint attachment points. The updated game derives this
    /// path from its raw link/control transforms and inserts the points that form
    /// corner bends. Keeping every generated point preserves the visible curve when
    /// the cable is recreated; retaining only the raw link transforms can collapse a
    /// routed cable to a straight endpoint-to-endpoint segment.
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
