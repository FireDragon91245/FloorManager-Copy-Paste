using System;
using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackRuntimeInfo
{
    public string Label { get; init; } = string.Empty;
    public UnityEngine.Vector3 Position { get; init; }
    public Il2Cpp.Rack Rack { get; init; }
    public IReadOnlyList<RackDeviceTemplate> Devices { get; init; } = Array.Empty<RackDeviceTemplate>();
    public int TotalSlots { get; init; }
    public int UsedSlots { get; init; }
}