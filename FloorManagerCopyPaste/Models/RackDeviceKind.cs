namespace FloorManagerCopyPaste.Models;

internal enum RackDeviceKind
{
    // Values are persisted as numbers in rack-templates.json. Keep the original
    // three values stable so templates created before Router/Firewall support
    // continue to deserialize correctly.
    Server = 0,
    NetworkSwitch = 1,
    PatchPanel = 2,
    Firewall = 3,
    Router = 4
}
