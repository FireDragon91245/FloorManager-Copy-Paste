namespace FloorManagerCopyPaste.Models;

internal sealed class FirewallRuleTemplate
{
    public int PortIndex { get; init; }
    public string SourceIpCidr { get; init; } = string.Empty;
    public string DestinationIpCidr { get; init; } = string.Empty;
    public int NetworkPort { get; init; }
    public int Protocol { get; init; }
    public bool Bidirectional { get; init; }
    public bool Allow { get; init; }
}
