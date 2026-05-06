using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DataCenterLaptopButtonMod.Services;

/// <summary>
/// Read-only diagnostics: captures deep rack/device/save/network state and diffs it
/// between F7 and F8. No SaveData/RackMount/Rack bitmap mutation happens here.
/// </summary>
internal static class DeepDiagnosticsService
{
    private static Snapshot _baseline;
    private static int _hookSeq;
    private static string BaselinePath => Path.Combine(MelonEnvironment.UserDataDirectory, "DataCenterLaptopButtonMod", "deepdiag-baseline.tsv");

    public static void CaptureBaseline(string trigger)
    {
        try
        {
            _baseline = Capture($"BASELINE/{trigger}");
            MelonLogger.Msg($"[DeepDiag][BASELINE][{trigger}] captured lines={_baseline.Lines.Count}");
            SaveBaselineToDisk(_baseline);
            DumpSnapshot(_baseline, "BASELINE");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][BASELINE][{trigger}] failed: {ex}");
        }
    }

    public static void DumpAndDiff(string trigger)
    {
        try
        {
            var current = Capture(trigger);
            MelonLogger.Msg($"[DeepDiag][{trigger}] captured lines={current.Lines.Count}");
            DumpSnapshot(current, trigger);
            if (_baseline == null)
                _baseline = LoadBaselineFromDisk();
            if (_baseline == null)
            {
                MelonLogger.Msg($"[DeepDiag][{trigger}][DIFF] no baseline; press F7 first.");
                return;
            }
            DumpDiff(_baseline, current, trigger);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][{trigger}] failed: {ex}");
        }
    }

    public static void LogRackPositionInsertPrefix(RackPosition rackPosition) => LogHook("RackPosition.InsertItemInRack", "PREFIX", rackPosition, null, null, null, null, null, null);
    public static void LogRackPositionInsertPostfix(RackPosition rackPosition) => LogHook("RackPosition.InsertItemInRack", "POSTFIX_ENUM_CREATED", rackPosition, null, null, null, null, null, null);
    public static void LogServerInsertedPrefix(Server server, ServerSaveData saveData) => LogHook("Server.ServerInsertedInRack", "PREFIX", null, server, null, null, saveData, null, null);
    public static void LogServerInsertedPostfix(Server server, ServerSaveData saveData) => LogHook("Server.ServerInsertedInRack", "POSTFIX", null, server, null, null, saveData, null, null);
    public static void LogSwitchInsertedPrefix(NetworkSwitch sw, SwitchSaveData saveData) => LogHook("NetworkSwitch.SwitchInsertedInRack", "PREFIX", null, null, sw, null, null, saveData, null);
    public static void LogSwitchInsertedPostfix(NetworkSwitch sw, SwitchSaveData saveData) => LogHook("NetworkSwitch.SwitchInsertedInRack", "POSTFIX", null, null, sw, null, null, saveData, null);
    public static void LogPatchPanelInsertedPrefix(PatchPanel pp, PatchPanelSaveData saveData) => LogHook("PatchPanel.InsertedInRack", "PREFIX", null, null, null, pp, null, null, saveData);
    public static void LogPatchPanelInsertedPostfix(PatchPanel pp, PatchPanelSaveData saveData) => LogHook("PatchPanel.InsertedInRack", "POSTFIX", null, null, null, pp, null, null, saveData);
    public static void LogRackPositionSetUsedPrefix(RackPosition rackPosition, bool used) => LogHook("RackPosition.SetUsed", $"PREFIX used={used}", rackPosition, null, null, null, null, null, null);
    public static void LogRackPositionSetUsedPostfix(RackPosition rackPosition, bool used) => LogHook("RackPosition.SetUsed", $"POSTFIX used={used}", rackPosition, null, null, null, null, null, null);
    public static void LogRackMarkPositionAsUsedPrefix(Rack rack, int index, int sizeInU) => LogRackBitmapHook("Rack.MarkPositionAsUsed", "PREFIX", rack, index, sizeInU);
    public static void LogRackMarkPositionAsUsedPostfix(Rack rack, int index, int sizeInU) => LogRackBitmapHook("Rack.MarkPositionAsUsed", "POSTFIX", rack, index, sizeInU);

    private static void LogHook(string method, string phase, RackPosition rp, Server server, NetworkSwitch sw, PatchPanel pp, ServerSaveData ssd, SwitchSaveData swd, PatchPanelSaveData ppd)
    {
        try
        {
            var seq = ++_hookSeq;
            UsableObject uo = null;
            if (server != null) uo = server;
            else if (sw != null) uo = sw;
            else if (pp != null) uo = pp;
            MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][{method}][{phase}] fromRackPlanner={IsCallFromRackPlanner()}");
            if (rp != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][RackPosition] {DescribeRackPosition(rp)}");
            if (uo != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][UsableObject] {DescribeUsableObject(uo)}");
            if (server != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][Server] {DescribeServer(server)}");
            if (sw != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][Switch] id='{Safe(() => sw.GetSwitchId(), "<err>")}' type={Safe(() => sw.switchType, -999)} on={Safe(() => sw.isOn, false)} broken={Safe(() => sw.isBroken, false)}");
            if (pp != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][PatchPanel] id='{Safe(() => pp.patchPanelId, "<err>")}' type={Safe(() => pp.patchPanelType, -999)}");
            if (ssd != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][ServerSaveArg] {DescribeServerSave(ssd)}");
            if (swd != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][SwitchSaveArg] id='{swd.switchID}' type={swd.switchType} rackUID={swd.rackPositionUID} pos={Fmt(swd.position)} rot={Fmt(swd.rotation)}");
            if (ppd != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][PatchSaveArg] id='{ppd.patchPanelID}' type={ppd.patchPanelType} rackUID={ppd.rackPositionUID} pos={Fmt(ppd.position)} rot={Fmt(ppd.rotation)}");
            var rack = rp?.rack ?? uo?.currentRackPosition?.rack;
            if (rack != null) MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][RackBitmap] {DescribeRackBitmap(rack)}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][HOOK][{method}][{phase}] failed: {ex.Message}");
        }
    }

    private static void LogRackBitmapHook(string method, string phase, Rack rack, int index, int sizeInU)
    {
        try
        {
            var seq = ++_hookSeq;
            MelonLogger.Msg($"[DeepDiag][HOOK#{seq}][{method}][{phase}] index={index} sizeInU={sizeInU} fromRackPlanner={IsCallFromRackPlanner()} {DescribeRackBitmap(rack)}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][HOOK][{method}][{phase}] failed: {ex.Message}");
        }
    }

    private static Snapshot Capture(string label)
    {
        var s = new Snapshot(label);
        CaptureSummaryAndSave(s);
        CaptureRacks(s);
        CaptureDevices(s);
        CaptureSaveEntries(s);
        CaptureCrossChecks(s);
        return s;
    }

    private static void CaptureSummaryAndSave(Snapshot s)
    {
        var save = Safe(() => SaveData.instance, null);
        var network = Safe(() => save?.networkData, null);
        var nm = Safe(() => NetworkMap.instance, null);
        var nums = Safe(() => nm?.GetNumberOfDevices(), null);
        var numsText = nums != null ? string.Join(",", nums.Select(n => n.ToString(CultureInfo.InvariantCulture))) : "<null>";
        s.Add("summary/counts",
            $"racks={Safe(() => Object.FindObjectsOfType<Rack>()?.Count ?? -1, -2)} " +
            $"liveServers={Safe(() => Object.FindObjectsOfType<Server>()?.Count ?? -1, -2)} saveServers={Safe(() => network?.servers?.Count ?? -3, -2)} " +
            $"liveSwitches={Safe(() => Object.FindObjectsOfType<NetworkSwitch>()?.Count ?? -1, -2)} saveSwitches={Safe(() => network?.switches?.Count ?? -3, -2)} " +
            $"livePatches={Safe(() => Object.FindObjectsOfType<PatchPanel>()?.Count ?? -1, -2)} savePatches={Safe(() => network?.patchPanels?.Count ?? -3, -2)} " +
            $"liveCableLinks={Safe(() => Object.FindObjectsOfType<CableLink>()?.Count(l => l != null && l.isStartOrEnd) ?? -1, -2)} wisCables={Safe(() => WaypointInitializationSystem.Instance?.GetAllCables()?.Count ?? -3, -2)} saveCables={Safe(() => network?.cables?.Count ?? -3, -2)}");
        s.Add("summary/saveData", save == null ? "SaveData=<null>" : $"name='{Safe(() => save.nameOfSave, "<err>")}' version={Safe(() => save.version, -999)} lastRackUID={Safe(() => save.lastUsedRackPositionGlobalUID, -999)} saveComplete={Safe(() => save.saveComplete, false)} rackMountObjectDataCount={Safe(() => save.rackMountObjectData?.Count ?? -1, -2)}");
        s.Add("summary/networkMap", nm == null ? "NetworkMap=<null>" : $"GetNumberOfDevices=[{numsText}] cableConnections={Safe(() => nm.cableConnections?.Count ?? -1, -2)} brokenServers={Safe(() => nm.brokenServers?.Count ?? -1, -2)} brokenSwitches={Safe(() => nm.brokenSwitches?.Count ?? -1, -2)}");
    }

    private static void CaptureRacks(Snapshot s)
    {
        var racks = Safe(() => Object.FindObjectsOfType<Rack>(), null);
        if (racks == null) return;
        var ordinal = 0;
        foreach (var rack in racks)
        {
            if (rack == null) continue;
            var rackKey = RackKey(rack, ordinal);
            s.Add($"rack/{rackKey}/bitmap", DescribeRackBitmap(rack));
            var ordered = GetPhysicalOrderPositions(rack);
            var slotCount = Safe(() => rack.positions?.Count ?? 0, 0);
            for (var arrayIndex = 0; arrayIndex < slotCount; arrayIndex++)
            {
                var pos = Safe(() => rack.positions[arrayIndex], null);
                var physSlot = -1;
                if (pos != null)
                {
                    for (var i = 0; i < ordered.Count; i++) if (ordered[i] == pos) { physSlot = i; break; }
                }
                var used = Safe(() => rack.isPositionUsed != null && arrayIndex < rack.isPositionUsed.Count ? rack.isPositionUsed[arrayIndex] : -9, -9);
                s.Add($"rack/{rackKey}/pos/arr={arrayIndex:00}", pos == null ? "<null>" : $"arr={arrayIndex} phys={physSlot} posIdx={Safe(() => pos.positionIndex, -999)} uid={Safe(() => pos.rackPosGlobalUID, -999)} used={used} local={FmtLocal(rack.transform, pos.transform.position)} world={Fmt(pos.transform.position)}");
            }
            ordinal++;
        }
    }

    private static void CaptureDevices(Snapshot s)
    {
        var usableObjects = Safe(() => Object.FindObjectsOfType<UsableObject>(), null);
        if (usableObjects == null) return;
        foreach (var uo in usableObjects)
        {
            if (uo == null) continue;
            var kind = DeviceKind(uo);
            if (kind == "Other" && Safe(() => uo.currentRackPosition, null) == null) continue;
            var id = DeviceId(uo, kind);
            var key = !string.IsNullOrEmpty(id) ? $"{kind}:{id}" : $"{kind}:iid={Safe(() => uo.GetInstanceID(), 0)}:{Safe(() => uo.name, "<unnamed>")}";
            s.Add($"device/{key}/state", DescribeUsableObject(uo));
            var server = Safe(() => uo.GetComponent<Server>(), null);
            if (server != null) s.Add($"device/{key}/server", DescribeServer(server));
            var nearest = FindNearestRackPosition(uo.transform.position);
            if (nearest.Position != null) s.Add($"device/{key}/nearestRackPosition", $"dist={nearest.Distance.ToString("0.###", CultureInfo.InvariantCulture)} {DescribeRackPosition(nearest.Position)}");
        }
    }

    private static void CaptureSaveEntries(Snapshot s)
    {
        var network = Safe(() => SaveData.instance?.networkData, null);
        if (network == null) return;
        var servers = Safe(() => network.servers, null);
        if (servers != null)
        {
            for (var i = 0; i < servers.Count; i++)
            {
                var data = servers[i];
                if (data != null) s.Add($"save/server/{data.serverID}/idx={i}", DescribeServerSave(data));
            }
        }
        var switches = Safe(() => network.switches, null);
        if (switches != null)
        {
            for (var i = 0; i < switches.Count; i++)
            {
                var data = switches[i];
                if (data != null) s.Add($"save/switch/{data.switchID}/idx={i}", $"id='{data.switchID}' type={data.switchType} rackUID={data.rackPositionUID} resolved={DescribeRackPosition(Safe(() => RackPosition.GetByUID(data.rackPositionUID), null))} pos={Fmt(data.position)} rot={Fmt(data.rotation)} on={data.isOn} broken={data.isBroken} label='{data.label}'");
            }
        }
        var patches = Safe(() => network.patchPanels, null);
        if (patches != null)
        {
            for (var i = 0; i < patches.Count; i++)
            {
                var data = patches[i];
                if (data != null) s.Add($"save/patch/{data.patchPanelID}/idx={i}", $"id='{data.patchPanelID}' type={data.patchPanelType} rackUID={data.rackPositionUID} resolved={DescribeRackPosition(Safe(() => RackPosition.GetByUID(data.rackPositionUID), null))} pos={Fmt(data.position)} rot={Fmt(data.rotation)}");
            }
        }
    }

    private static void CaptureCrossChecks(Snapshot s)
    {
        var servers = Safe(() => Object.FindObjectsOfType<Server>(), null);
        if (servers != null)
        {
            foreach (var server in servers)
            {
                if (server == null) continue;
                var id = Safe(() => server.ServerID ?? string.Empty, string.Empty);
                var uo = (UsableObject)server;
                var networkHas = !string.IsNullOrEmpty(id) && Safe(() => NetworkMap.instance != null && NetworkMap.instance.GetServer(id) != null, false);
                var save = FindServerSave(id);
                var rp = Safe(() => uo.currentRackPosition, null);
                var finding = $"id='{id}' networkMapHas={networkHas} liveCurrentUID={(rp != null ? Safe(() => rp.rackPosGlobalUID, -999).ToString(CultureInfo.InvariantCulture) : "<null>")} uoUID={Safe(() => uo.rackPositionUID, -999)} saveUID={(save != null ? save.rackPositionUID.ToString(CultureInfo.InvariantCulture) : "<missing>")} saveResolved={(save != null ? DescribeRackPosition(Safe(() => RackPosition.GetByUID(save.rackPositionUID), null)) : "<missing>")} nearest={DescribeNearest(uo.transform.position)}";
                s.Add($"check/server/{(!string.IsNullOrEmpty(id) ? id : Safe(() => server.GetInstanceID(), 0).ToString(CultureInfo.InvariantCulture))}", finding);
            }
        }
    }

    private static void DumpSnapshot(Snapshot s, string trigger)
    {
        foreach (var key in s.Order)
            MelonLogger.Msg($"[DeepDiag][{trigger}][{key}] {s.Lines[key]}");
    }

    private static void DumpDiff(Snapshot before, Snapshot after, string trigger)
    {
        MelonLogger.Msg($"[DeepDiag][{trigger}][DIFF] baseline='{before.Label}' {before.CapturedAt:HH:mm:ss.fff} -> current='{after.Label}' {after.CapturedAt:HH:mm:ss.fff}");
        foreach (var key in before.Lines.Keys.Except(after.Lines.Keys).OrderBy(k => k))
            MelonLogger.Warning($"[DeepDiag][{trigger}][DIFF][REMOVED][{key}] {before.Lines[key]}");
        foreach (var key in after.Lines.Keys.Except(before.Lines.Keys).OrderBy(k => k))
            MelonLogger.Warning($"[DeepDiag][{trigger}][DIFF][ADDED][{key}] {after.Lines[key]}");
        foreach (var key in before.Lines.Keys.Intersect(after.Lines.Keys).OrderBy(k => k))
        {
            if (before.Lines[key] == after.Lines[key]) continue;
            MelonLogger.Warning($"[DeepDiag][{trigger}][DIFF][CHANGED][{key}]");
            MelonLogger.Warning($"[DeepDiag][{trigger}][DIFF][BEFORE][{key}] {before.Lines[key]}");
            MelonLogger.Warning($"[DeepDiag][{trigger}][DIFF][AFTER ][{key}] {after.Lines[key]}");
        }
    }

    private static void SaveBaselineToDisk(Snapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            var lines = new List<string>
            {
                "#DataCenterLaptopButtonMod.DeepDiagBaseline.v1",
                $"#label\t{Encode(snapshot.Label)}",
                $"#captured\t{snapshot.CapturedAt:o}"
            };
            foreach (var key in snapshot.Order)
                lines.Add($"{Encode(key)}\t{Encode(snapshot.Lines[key])}");
            File.WriteAllLines(BaselinePath, lines);
            MelonLogger.Msg($"[DeepDiag][BASELINE] persisted path='{BaselinePath}' lines={snapshot.Lines.Count}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][BASELINE] persist failed: {ex.Message}");
        }
    }

    private static Snapshot LoadBaselineFromDisk()
    {
        try
        {
            if (!File.Exists(BaselinePath)) return null;
            var snapshot = new Snapshot("BASELINE/LOADED_FROM_DISK");
            foreach (var raw in File.ReadAllLines(BaselinePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.StartsWith("#label\t", StringComparison.Ordinal))
                {
                    snapshot.Label = Decode(raw.Substring("#label\t".Length));
                    continue;
                }
                if (raw.StartsWith("#", StringComparison.Ordinal)) continue;
                var tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                snapshot.Add(Decode(raw.Substring(0, tab)), Decode(raw.Substring(tab + 1)));
            }
            MelonLogger.Msg($"[DeepDiag][BASELINE] loaded persisted baseline path='{BaselinePath}' lines={snapshot.Lines.Count}");
            return snapshot.Lines.Count > 0 ? snapshot : null;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DeepDiag][BASELINE] load failed: {ex.Message}");
            return null;
        }
    }

    private static string Encode(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty));

    private static string Decode(string text)
        => Encoding.UTF8.GetString(Convert.FromBase64String(text ?? string.Empty));

    private static ServerSaveData FindServerSave(string serverId)
    {
        if (string.IsNullOrEmpty(serverId)) return null;
        var servers = Safe(() => SaveData.instance?.networkData?.servers, null);
        if (servers == null) return null;
        for (var i = 0; i < servers.Count; i++)
        {
            var data = servers[i];
            if (data != null && data.serverID == serverId) return data;
        }
        return null;
    }

    private static string DeviceKind(UsableObject uo)
    {
        if (Safe(() => uo.GetComponent<Server>() != null, false)) return "Server";
        if (Safe(() => uo.GetComponent<NetworkSwitch>() != null, false)) return "Switch";
        if (Safe(() => uo.GetComponent<PatchPanel>() != null, false)) return "PatchPanel";
        return "Other";
    }

    private static string DeviceId(UsableObject uo, string kind)
    {
        if (kind == "Server") return Safe(() => uo.GetComponent<Server>().ServerID ?? string.Empty, string.Empty);
        if (kind == "Switch") return Safe(() => uo.GetComponent<NetworkSwitch>().GetSwitchId() ?? string.Empty, string.Empty);
        if (kind == "PatchPanel") return Safe(() => uo.GetComponent<PatchPanel>().patchPanelId ?? string.Empty, string.Empty);
        return string.Empty;
    }

    private static string DescribeRackBitmap(Rack rack)
    {
        if (rack == null) return "rack=<null>";
        var bits = new List<string>();
        var used = 0;
        if (rack.isPositionUsed != null)
        {
            for (var i = 0; i < rack.isPositionUsed.Count; i++)
            {
                var v = Safe(() => rack.isPositionUsed[i], -9);
                if (v != 0) used++;
                bits.Add(v.ToString(CultureInfo.InvariantCulture));
            }
        }
        return $"rack='{Safe(() => rack.name, "<err>")}' iid={Safe(() => rack.GetInstanceID(), 0)} pos={Safe(() => Fmt(rack.transform.position), "<err>")} slots={Safe(() => rack.positions?.Count ?? -1, -2)} flagsUsed={used} bitmap={string.Join("", bits)}";
    }

    private static string DescribeRackPosition(RackPosition rp)
        => rp == null ? "<null>" : $"uid={Safe(() => rp.rackPosGlobalUID, -999)} posIdx={Safe(() => rp.positionIndex, -999)} physSlot={ResolvePhysicalSlot(rp.rack, rp)} arrSlot={ResolveArraySlot(rp.rack, rp)} rack='{Safe(() => rp.rack?.name ?? "<null>", "<err>")}' rackIid={Safe(() => rp.rack != null ? rp.rack.GetInstanceID() : 0, 0)} world={Safe(() => Fmt(rp.transform.position), "<err>")}";

    private static string DescribeUsableObject(UsableObject uo)
    {
        var rp = Safe(() => uo.currentRackPosition, null);
        return $"kind={DeviceKind(uo)} name='{Safe(() => uo.name, "<err>")}' iid={Safe(() => uo.GetInstanceID(), 0)} active={Safe(() => uo.gameObject.activeInHierarchy, false)} parent='{Safe(() => uo.transform.parent != null ? uo.transform.parent.name : "<null>", "<err>")}' prefabID={Safe(() => uo.prefabID, -999)} sizeU={Safe(() => uo.sizeInU, -999)} label='{Safe(() => uo.labelText ?? string.Empty, "<err>")}' uo.rackPositionUID={Safe(() => uo.rackPositionUID, -999)} currentRP={DescribeRackPosition(rp)} world={Safe(() => Fmt(uo.transform.position), "<err>")} rot={Safe(() => Fmt(uo.transform.rotation), "<err>")} localInRack={(rp?.rack != null ? FmtLocal(rp.rack.transform, uo.transform.position) : "<no-current-rack>")}";
    }

    private static string DescribeServer(Server server)
        => server == null ? "<null>" : $"id='{Safe(() => server.ServerID ?? string.Empty, "<err>")}' ip='{Safe(() => server.IP ?? string.Empty, "<err>")}' type={Safe(() => server.serverType, -999)} customer={Safe(() => server.GetCustomerID(), -999)} appID={Safe(() => server.appID, -999)} on={Safe(() => server.isOn, false)} broken={Safe(() => server.isBroken, false)} ttb={Safe(() => server.timeToBrake, -999)} eol={Safe(() => server.eolTime, -999)} warnCleared={Safe(() => server.isWarningCleared, false)} networkMapHas={Safe(() => NetworkMap.instance != null && NetworkMap.instance.GetServer(server.ServerID) != null, false)}";

    private static string DescribeServerSave(ServerSaveData data)
        => data == null ? "<null>" : $"id='{data.serverID}' customer={data.customerID} ip='{data.ip}' type={data.serverType} rackUID={data.rackPositionUID} resolved={DescribeRackPosition(Safe(() => RackPosition.GetByUID(data.rackPositionUID), null))} prefabID={data.prefabID} pos={Fmt(data.position)} rot={Fmt(data.rotation)} on={data.isOn} broken={data.isBroken} ttb={data.timeToBrake} eol={data.eolTime} warnCleared={data.isWarningCleared} label='{data.label}'";

    private static string DescribeNearest(Vector3 worldPos)
    {
        var nearest = FindNearestRackPosition(worldPos);
        return nearest.Position == null ? "<none>" : $"dist={nearest.Distance.ToString("0.###", CultureInfo.InvariantCulture)} {DescribeRackPosition(nearest.Position)}";
    }

    private static List<RackPosition> GetPhysicalOrderPositions(Rack rack)
    {
        var list = new List<RackPosition>();
        if (rack == null || rack.positions == null) return list;
        for (var i = 0; i < rack.positions.Count; i++)
        {
            var p = Safe(() => rack.positions[i], null);
            if (p != null) list.Add(p);
        }
        var rackTr = rack.transform;
        list.Sort((a, b) => Safe(() => rackTr.InverseTransformPoint(a.transform.position).y, 0f).CompareTo(Safe(() => rackTr.InverseTransformPoint(b.transform.position).y, 0f)));
        return list;
    }

    private static int ResolvePhysicalSlot(Rack rack, RackPosition rp)
    {
        if (rack == null || rp == null) return -1;
        var ordered = GetPhysicalOrderPositions(rack);
        for (var i = 0; i < ordered.Count; i++) if (ordered[i] == rp) return i;
        return -1;
    }

    private static int ResolveArraySlot(Rack rack, RackPosition rp)
    {
        if (rack == null || rack.positions == null || rp == null) return -1;
        for (var i = 0; i < rack.positions.Count; i++) if (rack.positions[i] == rp) return i;
        return -1;
    }

    private static NearestRackPosition FindNearestRackPosition(Vector3 worldPos)
    {
        RackPosition best = null;
        var bestDist = float.MaxValue;
        var racks = Safe(() => Object.FindObjectsOfType<Rack>(), null);
        if (racks != null)
        {
            foreach (var rack in racks)
            {
                if (rack?.positions == null) continue;
                for (var i = 0; i < rack.positions.Count; i++)
                {
                    var pos = Safe(() => rack.positions[i], null);
                    if (pos == null) continue;
                    var dist = (pos.transform.position - worldPos).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = pos;
                    }
                }
            }
        }
        return new NearestRackPosition { Position = best, Distance = best != null ? Mathf.Sqrt(bestDist) : -1f };
    }

    private static string RackKey(Rack rack, int ordinal)
        => $"rack#{ordinal}:{Safe(() => rack.name, "<unnamed>")}:iid={Safe(() => rack.GetInstanceID(), 0)}:pos={Safe(() => Fmt(rack.transform.position), "<err>")}";

    private static bool IsCallFromRackPlanner()
        => Safe(() => Environment.StackTrace.IndexOf("RackPlannerService", StringComparison.OrdinalIgnoreCase) >= 0, false);

    private static string Fmt(Vector3 v)
        => $"({v.x.ToString("0.###", CultureInfo.InvariantCulture)},{v.y.ToString("0.###", CultureInfo.InvariantCulture)},{v.z.ToString("0.###", CultureInfo.InvariantCulture)})";

    private static string Fmt(Quaternion q)
        => $"({q.x.ToString("0.###", CultureInfo.InvariantCulture)},{q.y.ToString("0.###", CultureInfo.InvariantCulture)},{q.z.ToString("0.###", CultureInfo.InvariantCulture)},{q.w.ToString("0.###", CultureInfo.InvariantCulture)})";

    private static string FmtLocal(Transform origin, Vector3 world)
        => origin == null ? "<null-origin>" : Fmt(origin.InverseTransformPoint(world));

    private static T Safe<T>(Func<T> action, T fallback)
    {
        try { return action(); }
        catch { return fallback; }
    }

    private sealed class Snapshot
    {
        public string Label;
        public readonly DateTime CapturedAt = DateTime.Now;
        public readonly Dictionary<string, string> Lines = new(StringComparer.Ordinal);
        public readonly List<string> Order = new();

        public Snapshot(string label) { Label = label; }

        public void Add(string key, string value)
        {
            if (!Lines.ContainsKey(key)) Order.Add(key);
            Lines[key] = value ?? "<null>";
        }
    }

    private struct NearestRackPosition
    {
        public RackPosition Position;
        public float Distance;
    }
}



