using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class NetworkPortVlanFilterTemplate
{
    public int PortIndex { get; init; }
    public List<int> DisallowedVlanIds { get; init; } = [];
}
