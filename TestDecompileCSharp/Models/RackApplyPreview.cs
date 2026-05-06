using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackApplyPreview
{
    public List<RackDeviceTemplate> Purchases { get; } = [];
    public List<string> Conflicts { get; } = [];
    public List<RackDeviceTemplate> MatchingDevices { get; } = [];
    public int BaseCost { get; set; }
    public int AdjustedCost { get; set; }
}