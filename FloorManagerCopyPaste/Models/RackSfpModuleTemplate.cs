namespace FloorManagerCopyPaste.Models;

/// <summary>
/// An installed SFP/QSFP module is switch inventory, independent of whether a
/// cable is currently connected to the port.
/// </summary>
internal sealed class RackSfpModuleTemplate
{
    public int PortIndex { get; init; } = -1;
    public int SfpType { get; init; } = -1;
    public float Speed { get; init; }
}
