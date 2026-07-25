namespace FloorManagerCopyPaste.Models;

internal sealed class RouterOwnedSubnetTemplate
{
    public int VlanId { get; init; }
    public string SubnetCidr { get; init; } = string.Empty;
}
