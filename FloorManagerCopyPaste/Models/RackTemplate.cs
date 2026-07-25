using System;
using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackTemplate
{
    public const int CurrentSlotEncodingVersion = 1;

    public string Name { get; init; } = string.Empty;
    public string SourceRackLabel { get; init; } = string.Empty;
    public string CreatedUtc { get; init; } = DateTime.UtcNow.ToString("O");
    public int SlotEncodingVersion { get; init; }
    public int TotalSlots { get; init; }
    public List<RackDeviceTemplate> Devices { get; init; } = [];
    public List<RackCableTemplate> Cables { get; init; } = [];
}
