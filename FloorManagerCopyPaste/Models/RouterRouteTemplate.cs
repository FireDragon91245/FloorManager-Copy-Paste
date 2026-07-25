namespace FloorManagerCopyPaste.Models;

internal sealed class RouterRouteTemplate
{
    public int RouteId { get; init; }
    public int SourceVlanId { get; init; }
    public string SourceIp { get; init; } = string.Empty;
    public int TargetVlanId { get; init; }
    public string TargetIp { get; init; } = string.Empty;
}
