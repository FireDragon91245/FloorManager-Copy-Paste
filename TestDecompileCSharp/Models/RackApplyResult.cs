using System.Collections.Generic;

namespace FloorManagerCopyPaste.Models;

internal sealed class RackApplyResult
{
    public int SpawnedCount { get; set; }
    public int ChargedAmount { get; set; }
    public int CablesCreated { get; set; }
    public int CablesFailed { get; set; }
    public List<string> Messages { get; } = [];
}