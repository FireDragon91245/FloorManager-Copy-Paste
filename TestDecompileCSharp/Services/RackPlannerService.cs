using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DataCenterLaptopButtonMod.Models;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DataCenterLaptopButtonMod.Services;

internal static class RackPlannerService
{
    internal const float PriceMultiplier = 1.5f;
    internal const int PricePerCableMeter = 5;
    internal const int PricePerSfp = 60;

    /// <summary>In-memory clipboard used by the "Copy" button on the Floor Plan page.</summary>
    public static RackTemplate Clipboard { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly bool VerboseRackPlannerDebug = false;

    // Tracks only server GameObjects created by this paste flow in the current runtime.
    // We use this to correct the persisted RackPosition anchor for pasted multi-U
    // servers without touching manual servers, switches, rackMountObjectData, or load state.
    private static readonly HashSet<string> PastedServerIds = new();

    private static string TemplateDirectory => Path.Combine(MelonEnvironment.UserDataDirectory, "DataCenterLaptopButtonMod");
    private static string TemplateFilePath => Path.Combine(TemplateDirectory, "rack-templates.json");

    /// <summary>
    /// Kept as a public no-op so older callers (Program.OnInitializeMelon) compile.
    /// The previous SaveSystem.onSavingData hook + PastedCableSaveRegistry approach
    /// did not work because <c>NetworkSaveData()</c> snapshot-builds its <c>cables</c>
    /// list from <c>WaypointInitializationSystem.Instance.cables</c>; our merged
    /// entries were thrown away. The new approach registers each pasted cable
    /// directly in that live registry via <c>UpdateCableInfo</c>, so no save hook
    /// is needed. See <c>CablePastePlan.md</c>.
    /// </summary>
    public static void InitializeSaveDiagnostics()
    {
        InstallOnSavingDataHook();
    }

    private static bool _onSavingHookInstalled;

    /// <summary>
    /// Subscribe (once) to <see cref="SaveSystem.onSavingData"/>. Empirically the
    /// snapshot constructor of <see cref="NetworkSaveData"/> rebuilds
    /// <c>networkData.servers</c> from a live source that does NOT include our
    /// pasted servers (switches save fine, servers don't). We therefore append
    /// missing entries here so the serializer writes them into the save file.
    /// We also log the BEFORE/AFTER counts so we can tell whether the hook fires
    /// before or after the snapshot ctor.
    /// </summary>
    private static void InstallOnSavingDataHook()
    {
        if (_onSavingHookInstalled) return;
        try
        {
            var del = DelegateSupport.ConvertDelegate<SaveSystem.OnSavingData>(new Action(OnSavingDataInjector));
            // Il2Cpp delegate types do not derive from System.Delegate so we cannot
            // use Delegate.Combine. Fortunately Il2CppInterop maps += to the
            // underlying multicast list, so direct compound assignment works.
            SaveSystem.onSavingData += del;
            _onSavingHookInstalled = true;
            MelonLogger.Msg("[RackPlanner][SAVE-HOOK] subscribed to SaveSystem.onSavingData");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner][SAVE-HOOK] subscribe failed: {ex.Message}");
        }
    }

    private static void OnSavingDataInjector()
    {
        try
        {
            var network = EnsureNetworkSaveData();
            if (network == null)
            {
                MelonLogger.Warning("[RackPlanner][SAVE-HOOK] EnsureNetworkSaveData returned null");
                return;
            }

            int beforeServers = network.servers?.Count ?? -1;
            int beforeSwitches = network.switches?.Count ?? -1;
            int beforePatches = network.patchPanels?.Count ?? -1;

            int addedServers = 0, addedSwitches = 0, addedPatches = 0;

            var servers = Object.FindObjectsOfType<Server>();
            foreach (var s in servers)
            {
                if (s == null) continue;
                RackPosition rp;
                try { rp = s.currentRackPosition; } catch { continue; }
                if (rp == null) continue;
                EnsureValidRackPositionUid(rp);
                var uo = s.GetComponent<UsableObject>();
                if (uo == null) continue;
                var data = BuildServerSaveData(s, uo, rp);
                int sizeBefore = network.servers.Count;
                UpsertServerSaveData(network.servers, data);
                if (network.servers.Count > sizeBefore) addedServers++;
            }

            var switches = Object.FindObjectsOfType<NetworkSwitch>();
            foreach (var sw in switches)
            {
                if (sw == null) continue;
                RackPosition rp;
                try { rp = sw.currentRackPosition; } catch { continue; }
                if (rp == null) continue;
                EnsureValidRackPositionUid(rp);
                var data = BuildSwitchSaveData(sw, rp);
                int sizeBefore = network.switches.Count;
                UpsertSwitchSaveData(network.switches, data);
                if (network.switches.Count > sizeBefore) addedSwitches++;
            }

            var patches = Object.FindObjectsOfType<PatchPanel>();
            foreach (var pp in patches)
            {
                if (pp == null) continue;
                RackPosition rp;
                try { rp = pp.currentRackPosition; } catch { continue; }
                if (rp == null) continue;
                EnsureValidRackPositionUid(rp);
                var data = BuildPatchPanelSaveData(pp, rp);
                int sizeBefore = network.patchPanels.Count;
                UpsertPatchPanelSaveData(network.patchPanels, data);
                if (network.patchPanels.Count > sizeBefore) addedPatches++;
            }

            MelonLogger.Msg($"[RackPlanner][SAVE-HOOK] before={{srv={beforeServers},sw={beforeSwitches},pp={beforePatches}}} after={{srv={network.servers.Count},sw={network.switches.Count},pp={network.patchPanels.Count}}} added={{srv={addedServers},sw={addedSwitches},pp={addedPatches}}} live={{srv={servers.Length},sw={switches.Length},pp={patches.Length}}}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner][SAVE-HOOK] handler failed: {ex.Message}");
        }
    }

    private static void VerboseLog(string message)
    {
        if (VerboseRackPlannerDebug) MelonLogger.Msg(message);
    }

    public static IReadOnlyList<RackRuntimeInfo> GetEmptyOrSparseRacks(int maxUsedSlots = 0)
    {
        return GetRackInfos().Where(r => r.UsedSlots <= maxUsedSlots).ToList();
    }

    public static TemplatePriceEstimate EstimatePrice(RackTemplate template)
    {
        var est = new TemplatePriceEstimate();
        if (template == null) return est;
        foreach (var d in template.Devices)
        {
            est.DeviceBase += d.BasePrice;
            est.DeviceAdjusted += CalculateAdjustedPrice(d.BasePrice);
        }
        if (template.Cables != null)
        {
            foreach (var c in template.Cables)
            {
                est.CableLength += Mathf.Max(0f, c.Length);
                est.SfpCount += c.SfpCount;
            }
        }
        est.CablePrice = Mathf.CeilToInt(est.CableLength * PricePerCableMeter);
        est.SfpPrice = est.SfpCount * PricePerSfp;
        return est;
    }

    public static bool DeleteTemplate(IList<RackTemplate> templates, int index)
    {
        if (templates == null || index < 0 || index >= templates.Count) return false;
        templates.RemoveAt(index);
        SaveTemplates(templates is List<RackTemplate> l ? l : templates.ToList());
        return true;
    }

    /// <summary>
    /// Best-effort attempt to instantiate a new rack at the given world position. The game
    /// does not expose a clean "buy rack" API; we just instantiate the rack prefab. Save/load
    /// integrity is not guaranteed for placed racks.
    /// </summary>
    public static bool TryBuyAndPlaceRack(Vector3 worldPos, out string message)
    {
        message = string.Empty;
        try
        {
            var mgm = MainGameManager.instance;
            if (mgm == null || mgm.rackPrefab == null)
            {
                message = "Rack-Prefab fehlt.";
                return false;
            }
            var inst = Object.Instantiate(mgm.rackPrefab, worldPos, Quaternion.identity);
            if (inst == null) { message = "Instanzierung fehlgeschlagen."; return false; }
            message = $"Neues Rack platziert bei {worldPos}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Fehler: {ex.Message}";
            MelonLogger.Error($"[RackPlanner] BuyRack failed: {ex}");
            return false;
        }
    }

    public static IReadOnlyList<RackRuntimeInfo> GetRackInfos()
    {
        var racks = Object.FindObjectsOfType<Rack>();
        var devicesByRack = BuildDeviceMap();

        return racks
            .Where(rack => rack != null && rack.positions != null)
            .Select((rack, index) =>
            {
                var devices = devicesByRack.TryGetValue(rack, out var rackDevices)
                    ? rackDevices.OrderBy(d => d.Template.StartIndex).Select(d => d.Template).ToList()
                    : new List<RackDeviceTemplate>();

                var totalSlots = rack.positions.Count;
                var usedSlots = devices.Sum(d => Math.Max(1, d.SizeInU));
                return new RackRuntimeInfo
                {
                    Rack = rack,
                    Label = BuildRackLabel(rack, index),
                    Position = rack.transform.position,
                    Devices = devices,
                    TotalSlots = totalSlots,
                    UsedSlots = Math.Min(totalSlots, usedSlots)
                };
            })
            .OrderBy(info => info.Position.z)
            .ThenBy(info => info.Position.x)
            .ToList();
    }

    public static RackTemplate CaptureRackTemplate(RackRuntimeInfo rackInfo, string explicitName = null)
    {
        // Re-scan with full UsableObject linkage so we can capture cables.
        var devicesByRack = BuildDeviceMap();
        var rackDevices = devicesByRack.TryGetValue(rackInfo.Rack, out var list) ? list : new List<DeviceWithRuntime>();
        var ordered = rackDevices.OrderBy(d => d.Template.StartIndex).ToList();

        var template = new RackTemplate
        {
            Name = string.IsNullOrWhiteSpace(explicitName)
                ? $"{SanitizeName(rackInfo.Label)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                : explicitName,
            SourceRackLabel = rackInfo.Label,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Devices = ordered.Select(d => CloneDeviceTemplate(d.Template)).ToList(),
            Cables = CaptureRackCables(rackInfo.Rack, ordered)
        };
        return template;
    }

    public static IReadOnlyList<RackTemplate> LoadTemplates()
    {
        try
        {
            if (!File.Exists(TemplateFilePath))
                return new List<RackTemplate>();

            var json = File.ReadAllText(TemplateFilePath);
            var templates = JsonSerializer.Deserialize<List<RackTemplate>>(json, JsonOptions);
            return templates ?? new List<RackTemplate>();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[RackPlanner] Konnte Templates nicht laden: {ex}");
            return new List<RackTemplate>();
        }
    }

    public static void SaveTemplates(IReadOnlyList<RackTemplate> templates)
    {
        Directory.CreateDirectory(TemplateDirectory);
        var json = JsonSerializer.Serialize(templates, JsonOptions);
        File.WriteAllText(TemplateFilePath, json);
    }

    public static RackApplyPreview BuildPreview(RackTemplate template, RackRuntimeInfo targetRack)
    {
        var preview = new RackApplyPreview
        {
            Template = template,
            TargetRack = targetRack
        };

        var occupancy = BuildOccupancy(targetRack);
        foreach (var device in template.Devices.OrderBy(d => d.StartIndex))
        {
            if (!IsWithinRack(device, targetRack.TotalSlots))
            {
                preview.Conflicts.Add($"{device.DisplayName}: außerhalb des Ziel-Racks.");
                continue;
            }

            var blockingDevice = FindBlockingDevice(device, occupancy);
            if (blockingDevice == null)
            {
                preview.Purchases.Add(CloneDeviceTemplate(device));
                preview.BaseCost += device.BasePrice;
                preview.AdjustedCost += CalculateAdjustedPrice(device.BasePrice);
                StampDevice(device, occupancy);
                continue;
            }

            if (AreEquivalent(device, blockingDevice))
            {
                preview.MatchingDevices.Add(device);
                continue;
            }

            preview.Conflicts.Add($"Slot U{device.StartIndex + 1}: {blockingDevice.DisplayName} blockiert {device.DisplayName}.");
        }

        preview.CableCount = template.Cables?.Count ?? 0;
        return preview;
    }

    public static RackApplyResult ApplyTemplate(RackTemplate template, RackRuntimeInfo targetRack)
    {
        InitializeSaveDiagnostics();
        var preview = BuildPreview(template, targetRack);
        var result = new RackApplyResult();

        if (preview.Conflicts.Count > 0)
        {
            result.Messages.AddRange(preview.Conflicts);
            return result;
        }

        var player = PlayerManager.instance?.playerClass;
        if (player == null && preview.Purchases.Count > 0)
        {
            result.Messages.Add("Spieler konnte nicht gefunden werden.");
            return result;
        }

        if (player != null)
        {
            var requiredFunds = preview.AdjustedCost;
            if (player.money < requiredFunds)
            {
                result.Messages.Add($"Zu wenig Geld: benötigt {requiredFunds:0}, vorhanden {player.money:0}.");
                return result;
            }
        }

        var successfulCosts = 0;
        foreach (var purchase in preview.Purchases)
        {
            if (TrySpawnIntoRack(targetRack.Rack, purchase, out var message))
            {
                result.SpawnedCount++;
                successfulCosts += CalculateAdjustedPrice(purchase.BasePrice);
            }

            result.Messages.Add(message);
        }

        if (successfulCosts > 0 && player != null)
        {
            var updatedMoney = Mathf.Max(0f, player.money - successfulCosts);
            player.money = updatedMoney;

            if (SaveData.instance?.playerData != null)
                SaveData.instance.playerData.coins = updatedMoney;

            var staticUi = StaticUIElements.instance;
            if (staticUi != null && staticUi.topLeft_coinTXT != null)
                staticUi.topLeft_coinTXT.text = Mathf.RoundToInt(updatedMoney).ToString();

            result.ChargedAmount = successfulCosts;
        }

        // Cable copy step (best effort, after devices are in)
        if (template.Cables != null && template.Cables.Count > 0)
        {
            try
            {
                ApplyCables(template, targetRack, result);
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Kabel-Clone abgebrochen: {ex.Message}");
                MelonLogger.Error($"[RackPlanner] Cable apply failed: {ex}");
            }
        }

        if (preview.Purchases.Count == 0 && result.CablesCreated == 0)
            result.Messages.Add("Nichts zu kaufen oder einzufügen – Ziel-Rack passt bereits.");

        return result;
    }

    // ------------------------------------------------------------------ device map -----

    private sealed class DeviceWithRuntime
    {
        public RackDeviceTemplate Template { get; set; }
        public UsableObject UsableObject { get; set; }
        public Server Server { get; set; }
        public NetworkSwitch Switch { get; set; }
        public PatchPanel Patch { get; set; }
    }

    private static Dictionary<Rack, List<DeviceWithRuntime>> BuildDeviceMap()
    {
        var map = new Dictionary<Rack, List<DeviceWithRuntime>>();
        var usableObjects = Object.FindObjectsOfType<UsableObject>();

        foreach (var usableObject in usableObjects)
        {
            if (usableObject == null || usableObject.currentRackPosition == null || usableObject.currentRackPosition.rack == null)
                continue;

            if (!TryBuildDeviceTemplate(usableObject, out var deviceTemplate, out var server, out var sw, out var pp))
                continue;

            var rack = usableObject.currentRackPosition.rack;
            if (!map.TryGetValue(rack, out var list))
            {
                list = new List<DeviceWithRuntime>();
                map[rack] = list;
            }

            list.Add(new DeviceWithRuntime
            {
                Template = deviceTemplate,
                UsableObject = usableObject,
                Server = server,
                Switch = sw,
                Patch = pp
            });
        }

        return map;
    }

    /// <summary>
    /// Returns the rack's <c>positions</c> sorted ascending by rack-LOCAL Y, so
    /// index 0 is the physically lowest slot (U01) and index <c>N-1</c> is the topmost.
    /// We use <see cref="Transform.InverseTransformPoint"/> instead of world Y so the
    /// ordering is correct even when the rack is rotated or its prefab places all
    /// slot transforms at the same world Y.
    /// </summary>
    private static List<RackPosition> GetPhysicalOrderPositions(Rack rack)
    {
        var list = new List<RackPosition>();
        if (rack == null || rack.positions == null) return list;
        for (var i = 0; i < rack.positions.Count; i++)
        {
            var p = rack.positions[i];
            if (p != null) list.Add(p);
        }
        var rackTr = rack.transform;
        list.Sort((a, b) =>
        {
            float ya = 0f, yb = 0f;
            try { ya = rackTr.InverseTransformPoint(a.transform.position).y; } catch { /* fallback */ }
            try { yb = rackTr.InverseTransformPoint(b.transform.position).y; } catch { /* fallback */ }
            return ya.CompareTo(yb);
        });
        return list;
    }

    /// <summary>
    /// Resolves the physical slot (0-based, bottom-to-top) of <paramref name="rackPos"/>.
    /// The game's <c>RackPosition.positionIndex</c> is a logical id that does NOT match
    /// the rendered U-number; sorting by Y does.
    /// </summary>
    private static int ResolveSlotIndex(Rack rack, RackPosition rackPos)
    {
        if (rack == null || rackPos == null) return -1;
        var ordered = GetPhysicalOrderPositions(rack);
        for (var i = 0; i < ordered.Count; i++)
            if (ordered[i] == rackPos) return i;
        return -1;
    }

    /// <summary>Returns the <see cref="RackPosition"/> at a physical slot.</summary>
    private static RackPosition GetPositionByPhysicalSlot(Rack rack, int physicalSlot)
    {
        var ordered = GetPhysicalOrderPositions(rack);
        if (physicalSlot < 0 || physicalSlot >= ordered.Count) return null;
        return ordered[physicalSlot];
    }

    /// <summary>
    /// Translates a physical slot index back to the array index inside
    /// <c>rack.positions</c> (= the index expected by <c>Rack.IsPositionAvailable</c>
    /// and <c>Rack.MarkPositionAsUsed</c>).
    /// </summary>
    private static int PhysicalToArrayIndex(Rack rack, int physicalSlot)
    {
        var pos = GetPositionByPhysicalSlot(rack, physicalSlot);
        if (pos == null || rack?.positions == null) return -1;
        for (var i = 0; i < rack.positions.Count; i++)
            if (rack.positions[i] == pos) return i;
        return -1;
    }

    /// <summary>
    /// Returns the lowest array index covered by a multi-U device whose physical slot
    /// range is [<paramref name="physicalSlot"/>, <paramref name="physicalSlot"/>+
    /// <paramref name="sizeInU"/>-1]. Required because the game's array order may be
    /// inverted relative to the physical Y order.
    /// </summary>
    private static int PhysicalRangeToArrayStart(Rack rack, int physicalSlot, int sizeInU)
    {
        var min = int.MaxValue;
        for (var s = physicalSlot; s < physicalSlot + Math.Max(1, sizeInU); s++)
        {
            var idx = PhysicalToArrayIndex(rack, s);
            if (idx >= 0 && idx < min) min = idx;
        }
        return min == int.MaxValue ? -1 : min;
    }

    /// <summary>
    /// Vanilla uses the physically upper slot of a multi-U device as the device's
    /// <c>currentRackPosition</c> / persisted <c>rackPositionUID</c> anchor. The UI
    /// template stores the physically lower occupied slot so stamping can cover
    /// [bottom..top]. Convert bottom+size to that vanilla anchor slot.
    /// </summary>
    private static int ResolveAnchorPhysicalSlot(int bottomPhysicalSlot, int sizeInU)
        => bottomPhysicalSlot + Math.Max(1, sizeInU) - 1;

    private static int ResolveAnchorArrayIndex(Rack rack, int bottomPhysicalSlot, int sizeInU)
        => PhysicalToArrayIndex(rack, ResolveAnchorPhysicalSlot(bottomPhysicalSlot, sizeInU));

    private static bool TryBuildDeviceTemplate(UsableObject usableObject, out RackDeviceTemplate template, out Server server, out NetworkSwitch sw, out PatchPanel patch)
    {
        template = null;
        server = null;
        sw = null;
        patch = null;

        var rackPos = usableObject.currentRackPosition;
        var anchorIndex = ResolveSlotIndex(rackPos?.rack, rackPos);
        if (anchorIndex < 0) return false;
        var sizeInU = Math.Max(1, usableObject.sizeInU);
        var startIndex = Math.Max(0, anchorIndex - sizeInU + 1);
        var label = usableObject.labelText ?? string.Empty;
        var shopItem = usableObject.shopItemSO;
        var displayName = shopItem?.itemName ?? usableObject.gameObject.name;
        var basePrice = shopItem?.price ?? 0;
        var prefabId = usableObject.prefabID;

        // Capture the EXACT placement in source rack-local space so we can replay it
        // pixel-perfect on a target rack (same prefab → same local-to-world mapping).
        // This sidesteps any guesswork about prefab pivot / rack-position anchor offsets.
        var rackTr = rackPos?.rack?.transform;
        var localPos = rackTr != null
            ? Vec3.From(rackTr.InverseTransformPoint(usableObject.transform.position))
            : default;
        var localEuler = rackTr != null
            ? Vec3.From((Quaternion.Inverse(rackTr.rotation) * usableObject.transform.rotation).eulerAngles)
            : default;
        VerboseLog($"[RackPlanner][CAPTURE] dev kind={(usableObject.GetComponent<Server>()!=null?"Server":usableObject.GetComponent<NetworkSwitch>()!=null?"NetworkSwitch":"PatchPanel")} sizeU={sizeInU} bottomPhysSlot={startIndex} anchorPhysSlot={anchorIndex} localPos={localPos.ToUnity()} localEuler={localEuler.ToUnity()} slotLocal={(rackTr!=null?rackTr.InverseTransformPoint(rackPos.transform.position):Vector3.zero)}");

        server = usableObject.GetComponent<Server>();
        if (server != null)
        {
            template = new RackDeviceTemplate
            {
                Kind = RackDeviceKind.Server,
                StartIndex = startIndex,
                SizeInU = sizeInU,
                PrefabId = prefabId,
                VariantId = server.serverType,
                BasePrice = basePrice,
                DisplayName = displayName,
                Label = label,
                IsPoweredOn = server.isOn,
                LocalPos = localPos,
                LocalEuler = localEuler,
                // serverType is the only operational field we need — it identifies
                // which prefab variant this is. IP / customer / appID are
                // intentionally NOT captured: a pasted server should be a fresh one.
                ServerType = server.serverType
            };
            MelonLogger.Msg($"[RackPlanner][CAPTURE-SRV] name='{usableObject.name}' serverType={server.serverType} (IP/customer/appID not copied — paste will get a fresh assignment)");
            return true;
        }

        sw = usableObject.GetComponent<NetworkSwitch>();
        if (sw != null)
        {
            template = new RackDeviceTemplate
            {
                Kind = RackDeviceKind.NetworkSwitch,
                StartIndex = startIndex,
                SizeInU = sizeInU,
                PrefabId = prefabId,
                VariantId = sw.switchType,
                BasePrice = basePrice,
                DisplayName = displayName,
                Label = label,
                IsPoweredOn = sw.isOn,
                LocalPos = localPos,
                LocalEuler = localEuler
            };
            return true;
        }

        patch = usableObject.GetComponent<PatchPanel>();
        if (patch != null)
        {
            // Patch panels are visually 2U in this game; usableObject.sizeInU is sometimes
            // reported as 1, so we clamp to a minimum of 2 to keep blueprints/sidebar
            // accurate – but only when there is actually room in the rack, otherwise
            // we'd push the device past the top edge and BuildPreview would reject it.
            var totalSlots = rackPos?.rack?.positions?.Count ?? int.MaxValue;
            var ppSize = Math.Max(2, sizeInU);
            if (startIndex + ppSize > totalSlots) ppSize = Math.Max(1, totalSlots - startIndex);
            template = new RackDeviceTemplate
            {
                Kind = RackDeviceKind.PatchPanel,
                StartIndex = startIndex,
                SizeInU = ppSize,
                PrefabId = prefabId,
                VariantId = patch.patchPanelType,
                BasePrice = basePrice,
                DisplayName = displayName,
                Label = label,
                IsPoweredOn = false,
                LocalPos = localPos,
                LocalEuler = localEuler
            };
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- cable capture -----

    /// <summary>
    /// Computes the rack-local AABB that encloses every <see cref="RackPosition"/> of the rack,
    /// padded by <paramref name="margin"/> on each side. Used to decide whether a cable's
    /// hooks/holders are still inside the rack we're capturing – cables that leave the rack
    /// (e.g. patch panel down-link to a switch in a different rack) must NOT be cloned.
    /// </summary>
    private static (Vector3 min, Vector3 max) GetRackLocalBounds(Rack rack, float margin = 0.35f)
    {
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var ordered = GetPhysicalOrderPositions(rack);
        var rackTr = rack.transform;
        foreach (var pos in ordered)
        {
            Vector3 lp;
            try { lp = rackTr.InverseTransformPoint(pos.transform.position); } catch { continue; }
            min = Vector3.Min(min, lp);
            max = Vector3.Max(max, lp);
        }
        if (float.IsPositiveInfinity(min.x))
        {
            min = new Vector3(-1f, -0.5f, -1f);
            max = new Vector3(1f, 2f, 1f);
        }
        var pad = new Vector3(margin, margin, margin);
        return (min - pad, max + pad);
    }

    private static bool IsInsideLocalBounds(Vector3 localPoint, Vector3 min, Vector3 max)
    {
        return localPoint.x >= min.x && localPoint.x <= max.x
            && localPoint.y >= min.y && localPoint.y <= max.y
            && localPoint.z >= min.z && localPoint.z <= max.z;
    }

    private static List<RackCableTemplate> CaptureRackCables(Rack sourceRack, List<DeviceWithRuntime> rackDevices)
    {
        var cables = new List<RackCableTemplate>();
        VerboseLog($"[RackPlanner][CABLE-DUMP] === CaptureRackCables START === rack={(sourceRack==null?"<null>":sourceRack.name)} devices={rackDevices?.Count ?? 0}");
        if (rackDevices == null || rackDevices.Count == 0 || sourceRack == null)
        {
            VerboseLog("[RackPlanner][CABLE-DUMP] EARLY EXIT: no devices or no rack");
            return cables;
        }

        // Map every CableLink in the rack to its (deviceIndex, portIndex)
        var linkLookup = new Dictionary<CableLink, (int deviceIndex, int portIndex, RackDeviceKind kind)>();
        for (var i = 0; i < rackDevices.Count; i++)
        {
            var d = rackDevices[i];
            var ports = GetCableLinkPorts(d);
            var portCount = ports?.Count ?? -1;
            VerboseLog($"[RackPlanner][CABLE-DUMP] Device[{i}] kind={d.Template.Kind} name='{d.Template.DisplayName}' slot={d.Template.StartIndex} sizeU={d.Template.SizeInU} portsArray={(ports==null?"NULL":portCount.ToString())}");
            if (ports == null) continue;
            for (var p = 0; p < ports.Count; p++)
            {
                var link = ports[p];
                if (link == null)
                {
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   Port[{p}] = NULL link");
                    continue;
                }
                int cableId = -999; bool isStartOrEnd = false, isEndPoint = false; int typeOfLink = -999; float speed = -1f; int sfp = -999;
                try { cableId = link.cableIDsOnLink; } catch (Exception ex) { VerboseLog($"[RackPlanner][CABLE-DUMP]   Port[{p}] cableIDsOnLink threw: {ex.Message}"); }
                try { isStartOrEnd = link.isStartOrEnd; } catch { }
                try { isEndPoint = link.isEndPoint; } catch { }
                try { typeOfLink = (int)link.typeOfLink; } catch { }
                try { speed = link.connectionSpeed; } catch { }
                try { sfp = link.sfpTypeInserted; } catch { }
                VerboseLog($"[RackPlanner][CABLE-DUMP]   Port[{p}] link={link.GetInstanceID()} cableId={cableId} startOrEnd={isStartOrEnd} endPoint={isEndPoint} type={typeOfLink} speed={speed} sfp={sfp}");

                if (!linkLookup.ContainsKey(link))
                    linkLookup[link] = (i, p, d.Template.Kind);
                else
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   Port[{p}] DUPLICATE link reference (already mapped)");
            }
        }

        VerboseLog($"[RackPlanner][CABLE-DUMP] Total unique CableLinks discovered: {linkLookup.Count}");

        // Group by cable id
        var byCable = new Dictionary<int, List<(CableLink link, int devIdx, int port, RackDeviceKind kind)>>();
        int skippedNoId = 0, skippedThrew = 0;
        foreach (var kv in linkLookup)
        {
            var link = kv.Key;
            int cableId = 0;
            try { cableId = link.cableIDsOnLink; } catch { skippedThrew++; continue; }
            if (cableId <= 0) { skippedNoId++; continue; }
            if (!byCable.TryGetValue(cableId, out var lst))
            {
                lst = new List<(CableLink, int, int, RackDeviceKind)>();
                byCable[cableId] = lst;
            }
            lst.Add((link, kv.Value.deviceIndex, kv.Value.portIndex, kv.Value.kind));
        }

        VerboseLog($"[RackPlanner][CABLE-DUMP] Grouping: distinctCableIds={byCable.Count} skippedNoId={skippedNoId} skippedThrew={skippedThrew}");
        foreach (var pair in byCable)
            VerboseLog($"[RackPlanner][CABLE-DUMP]   cableId={pair.Key} endpointsFound={pair.Value.Count} (devIdx,port,kind)=[{string.Join(",", pair.Value.Select(v => $"({v.devIdx},{v.port},{v.kind})"))}]");

        var positions = CablePositions.instance;
        VerboseLog($"[RackPlanner][CABLE-DUMP] CablePositions.instance={(positions==null?"NULL":"OK")}");
        var (boundsMin, boundsMax) = GetRackLocalBounds(sourceRack);
        VerboseLog($"[RackPlanner][CABLE-DUMP] Rack local bounds: min={boundsMin} max={boundsMax}");
        var sourceTr = sourceRack.transform;

        foreach (var pair in byCable)
        {
            VerboseLog($"[RackPlanner][CABLE-DUMP] --- Processing cableId={pair.Key} endpoints={pair.Value.Count} ---");
            // Both endpoints must terminate on a device inside this rack.
            if (pair.Value.Count < 2)
            {
                VerboseLog($"[RackPlanner][CABLE-DUMP]   SKIP cableId={pair.Key}: only {pair.Value.Count} endpoint(s) inside this rack (other end is in a different rack or not on a captured device)");
                continue;
            }
            var a = pair.Value[0];
            var b = pair.Value[1];

            float speed = 0f;
            Color color = Color.white;
            float length = 0f;
            var worldWaypoints = new List<Vec3>();
            var localRoute = new List<Vec3>();
            bool fullyInside = true;
            int sfpA = -1, sfpB = -1;

            try { speed = a.link.connectionSpeed; } catch (Exception ex) { VerboseLog($"[RackPlanner][CABLE-DUMP]   speed read threw: {ex.Message}"); }
            try { sfpA = a.link.sfpTypeInserted; } catch { }
            try { sfpB = b.link.sfpTypeInserted; } catch { }
            VerboseLog($"[RackPlanner][CABLE-DUMP]   endpoints: A=(dev{a.devIdx},port{a.port},{a.kind}) B=(dev{b.devIdx},port{b.port},{b.kind}) speed={speed} sfpA={sfpA} sfpB={sfpB}");

            try
            {
                if (positions != null)
                {
                    var mat = positions.GetCableMaterial(pair.Key);
                    if (mat != null) color = mat.color;
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   material={(mat==null?"NULL":"OK")} color=({color.r:0.##},{color.g:0.##},{color.b:0.##},{color.a:0.##})");

                    // Raw link transforms = ordered list of every Transform the cable
                    // routes through (endpoint A → hooks/holders → endpoint B). This is
                    // what the game itself stores per cable and what we need to recreate
                    // the same path on a target rack with the same prefab.
                    var rawTransforms = positions.GetRawLinkTransforms(pair.Key);
                    var rawCount = rawTransforms?.Count ?? 0;
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   rawTransforms={(rawTransforms==null?"NULL":rawCount.ToString())}");

                    // Path 1: hook transforms (preferred, gives the exact routing path).
                    // We do NOT AABB-test these – cable physics often route along the back
                    // of the rack frame (~1cm outside the slot-anchor bounds). Endpoint
                    // containment (both ports on captured devices in this rack) is what
                    // determines whether the cable belongs to this rack.
                    if (rawTransforms != null && rawCount >= 2)
                    {
                        for (var i = 0; i < rawCount; i++)
                        {
                            var tr = rawTransforms[i];
                            if (tr == null)
                            {
                                VerboseLog($"[RackPlanner][CABLE-DUMP]     raw[{i}] = NULL transform (skipped)");
                                continue;
                            }
                            var local = sourceTr.InverseTransformPoint(tr.position);
                            VerboseLog($"[RackPlanner][CABLE-DUMP]     raw[{i}] world={tr.position} local={local} name='{tr.name}'");
                            localRoute.Add(Vec3.From(local));
                        }
                    }

                    // Always also read the rendered (line-renderer) positions: they give
                    // us the cable length and serve as fallback when GetRawLinkTransforms
                    // returns nothing (which happens for runtime-built cables that haven't
                    // been re-saved yet).
                    var pts = positions.GetCablePositions(pair.Key);
                    var renderedCount = pts?.Count ?? 0;
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   renderedPositions={(pts==null?"NULL":renderedCount.ToString())}");
                    if (pts != null)
                    {
                        Vector3? prev = null;
                        for (var i = 0; i < renderedCount; i++)
                        {
                            var p = pts[i];
                            worldWaypoints.Add(Vec3.From(p));
                            if (prev.HasValue) length += Vector3.Distance(prev.Value, p);
                            prev = p;
                        }
                    }

                    // Path 2: fallback to rendered positions if no hooks were available.
                    // Same rationale: trust endpoint containment, no AABB rejection.
                    if (localRoute.Count < 2 && pts != null && renderedCount >= 2)
                    {
                        VerboseLog($"[RackPlanner][CABLE-DUMP]   FALLBACK: building LocalRoute from {renderedCount} rendered positions");
                        // Downsample to keep the route compact (every Nth point + first/last).
                        var step = Math.Max(1, renderedCount / 20);
                        for (var i = 0; i < renderedCount; i++)
                        {
                            // Always keep first, last, and every Nth point.
                            if (i != 0 && i != renderedCount - 1 && (i % step) != 0) continue;
                            var local = sourceTr.InverseTransformPoint(pts[i]);
                            localRoute.Add(Vec3.From(local));
                        }
                    }

                    // We accept the cable as long as we have at least the two endpoints.
                    fullyInside = localRoute.Count >= 2;
                    VerboseLog($"[RackPlanner][CABLE-DUMP]   computed length={length:0.###}m localRoute.Count={localRoute.Count} accepted={fullyInside}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackPlanner][CABLE-DUMP]   route capture EXCEPTION cableId={pair.Key}: {ex}");
                fullyInside = false;
            }

            // Reject cables whose route leaves the rack – the user explicitly does not
            // want partial cables that depend on hooks outside the rack.
            if (!fullyInside || localRoute.Count < 2)
            {
                VerboseLog($"[RackPlanner][CABLE-DUMP]   REJECT cableId={pair.Key}: fullyInside={fullyInside} localRoute.Count={localRoute.Count}");
                continue;
            }

            int typeA = 0, typeB = 0;
            try { typeA = (int)a.link.typeOfLink; } catch { /* ignore */ }
            try { typeB = (int)b.link.typeOfLink; } catch { /* ignore */ }

            VerboseLog($"[RackPlanner][CABLE-DUMP]   ACCEPT cableId={pair.Key}: A=(dev{a.devIdx},port{a.port}) B=(dev{b.devIdx},port{b.port}) len={length:0.##}m hooks={localRoute.Count}");
            MelonLogger.Msg($"[RackPlanner][CABLE-CAPTURE] MANUAL id={pair.Key} A=dev{a.devIdx}.port{a.port} type={(CableLink.TypeOfLink)typeA} pos={a.link.transform.position} cableId={a.link.cableIDsOnLink} startOrEnd={a.link.isStartOrEnd} end={a.link.isEndPoint} sw='{a.link.switchID ?? string.Empty}' cust={a.link.CustomerID} sfp={sfpA} B=dev{b.devIdx}.port{b.port} type={(CableLink.TypeOfLink)typeB} pos={b.link.transform.position} cableId={b.link.cableIDsOnLink} startOrEnd={b.link.isStartOrEnd} end={b.link.isEndPoint} sw='{b.link.switchID ?? string.Empty}' cust={b.link.CustomerID} sfp={sfpB} route={localRoute.Count} rendered={worldWaypoints.Count} speed={speed:0.###}");
            cables.Add(new RackCableTemplate
            {
                EndA = new RackCableEndpoint { DeviceIndex = a.devIdx, PortIndex = a.port, Kind = a.kind },
                EndB = new RackCableEndpoint { DeviceIndex = b.devIdx, PortIndex = b.port, Kind = b.kind },
                Speed = speed,
                Length = length,
                Waypoints = worldWaypoints,
                LocalRoute = localRoute,
                FullyInsideSourceRack = fullyInside,
                TypeA = typeA,
                TypeB = typeB,
                SfpTypeA = sfpA,
                SfpTypeB = sfpB,
                ColorR = color.r,
                ColorG = color.g,
                ColorB = color.b,
                ColorA = color.a
            });
        }

        VerboseLog($"[RackPlanner][CABLE-DUMP] === CaptureRackCables END === captured {cables.Count} cable(s) for rack '{sourceRack.name}'");
        return cables;
    }

    private static Il2CppReferenceArray<CableLink> GetCableLinkPorts(DeviceWithRuntime d)
    {
        if (d.Server != null) return d.Server.cablelinks;
        if (d.Switch != null) return d.Switch.cableLinkSwitchPorts;
        if (d.Patch != null) return d.Patch.cableLinkPorts;
        return null;
    }

    // ---------------------------------------------------------------- cable apply -----

    /// <summary>
    /// Builds a flat list of every Transform under <paramref name="rackRoot"/> together
    /// with its rack-local position. Used to find the same hook/holder transforms in a
    /// target rack that the original cable was routed through.
    /// </summary>
    private static List<(Transform tr, Vector3 local)> CollectRackTransforms(Transform rackRoot)
    {
        var result = new List<(Transform, Vector3)>();
        if (rackRoot == null) return result;
        var stack = new Stack<Transform>();
        stack.Push(rackRoot);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == null) continue;
            Vector3 local;
            try { local = rackRoot.InverseTransformPoint(cur.position); } catch { local = Vector3.zero; }
            result.Add((cur, local));
            for (var i = 0; i < cur.childCount; i++) stack.Push(cur.GetChild(i));
        }
        return result;
    }

    private static Transform FindClosestTransform(List<(Transform tr, Vector3 local)> all, Vector3 targetLocal, float maxDist = 0.15f)
    {
        Transform best = null;
        var bestSq = maxDist * maxDist;
        foreach (var (tr, local) in all)
        {
            var d = (local - targetLocal).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = tr; }
        }
        return best;
    }

    private static void ApplyCables(RackTemplate template, RackRuntimeInfo targetRack, RackApplyResult result)
    {
        // Re-resolve devices currently in the target rack (the freshly-spawned ones
        // already have valid ServerID / switchId because we used the null/fresh-insert
        // overload in TrySpawnIntoRack).
        var devicesByRack = BuildDeviceMap();
        if (!devicesByRack.TryGetValue(targetRack.Rack, out var targetDevices))
        {
            result.Messages.Add("Kabel-Clone: keine Ziel-Geräte gefunden.");
            return;
        }

        var byStart = targetDevices.ToDictionary(d => d.Template.StartIndex);

        DeviceWithRuntime FindForTemplateIndex(int templateIdx)
        {
            if (templateIdx < 0 || templateIdx >= template.Devices.Count) return null;
            var dev = template.Devices[templateIdx];
            return byStart.TryGetValue(dev.StartIndex, out var d) ? d : null;
        }

        var positions = CablePositions.instance;
        if (positions == null)
        {
            result.Messages.Add("Kabel-Clone: CablePositions.instance fehlt.");
            return;
        }

        var rackTr = targetRack.Rack.transform;
        var targetRackTransforms = CollectRackTransforms(rackTr);

        foreach (var cable in template.Cables)
        {
            try
            {
                if (!cable.FullyInsideSourceRack || cable.LocalRoute == null || cable.LocalRoute.Count < 2)
                {
                    result.CablesFailed++;
                    continue;
                }

                var devA = FindForTemplateIndex(cable.EndA.DeviceIndex);
                var devB = FindForTemplateIndex(cable.EndB.DeviceIndex);
                if (devA == null || devB == null) { result.CablesFailed++; continue; }

                var portsA = GetCableLinkPorts(devA);
                var portsB = GetCableLinkPorts(devB);
                if (portsA == null || portsB == null
                    || cable.EndA.PortIndex < 0 || cable.EndA.PortIndex >= portsA.Count
                    || cable.EndB.PortIndex < 0 || cable.EndB.PortIndex >= portsB.Count)
                {
                    result.CablesFailed++; continue;
                }

                var linkA = portsA[cable.EndA.PortIndex];
                var linkB = portsB[cable.EndB.PortIndex];
                if (linkA == null || linkB == null) { result.CablesFailed++; continue; }
                if (linkA.cableIDsOnLink > 0 || linkB.cableIDsOnLink > 0) { result.CablesFailed++; continue; }

                var attachA = linkA.GetRopeAttachPoint();
                var attachB = linkB.GetRopeAttachPoint();
                if (attachA == null || attachB == null) { result.CablesFailed++; continue; }

                var typeA = cable.TypeA != 0 ? (CableLink.TypeOfLink)cable.TypeA : linkA.typeOfLink;
                var typeB = cable.TypeB != 0 ? (CableLink.TypeOfLink)cable.TypeB : linkB.typeOfLink;

                EnsureSfpInserted(linkA, cable.SfpTypeA, cable.Speed);
                EnsureSfpInserted(linkB, cable.SfpTypeB, cable.Speed);

                // The captured LocalRoute may be ordered B → A instead of A → B
                // (LineRenderer points are in render order, which doesn't always match
                // the device-A → device-B mental model). If we don't fix this, the
                // cable jumps src → far end → snake back → near end. Detect by
                // comparing distances of the first/last local point (transformed to
                // world space of the TARGET rack) against attachA / attachB and
                // flipping the route if needed.
                var routeFirstWorld = rackTr.TransformPoint(cable.LocalRoute[0].ToUnity());
                var routeLastWorld = rackTr.TransformPoint(cable.LocalRoute[cable.LocalRoute.Count - 1].ToUnity());
                var dFirstA = (routeFirstWorld - attachA.position).sqrMagnitude;
                var dLastA = (routeLastWorld - attachA.position).sqrMagnitude;
                var routeReversed = dFirstA > dLastA;
                MelonLogger.Msg($"[RackPlanner][CABLE-APPLY] dev{cable.EndA.DeviceIndex}.{cable.EndA.PortIndex}->dev{cable.EndB.DeviceIndex}.{cable.EndB.PortIndex} routeLen={cable.LocalRoute.Count} dFirstA={dFirstA:0.###} dLastA={dLastA:0.###} reverse={routeReversed}");

                // Translate every captured rack-local hook position into the target
                // rack's world space and replace the two endpoints with the actual
                // rope-attach points of the freshly-spawned devices.
                var worldRoute = new Il2CppSystem.Collections.Generic.List<Vector3>();
                var rawLinkRoute = new Il2CppSystem.Collections.Generic.List<Transform>();
                worldRoute.Add(attachA.position);
                rawLinkRoute.Add(linkA.transform);
                if (routeReversed)
                {
                    for (var i = cable.LocalRoute.Count - 2; i >= 1; i--)
                    {
                        worldRoute.Add(rackTr.TransformPoint(cable.LocalRoute[i].ToUnity()));
                        var hook = FindClosestTransform(targetRackTransforms, cable.LocalRoute[i].ToUnity());
                        if (hook != null && hook != linkA.transform && hook != linkB.transform) rawLinkRoute.Add(hook);
                    }
                }
                else
                {
                    for (var i = 1; i < cable.LocalRoute.Count - 1; i++)
                    {
                        worldRoute.Add(rackTr.TransformPoint(cable.LocalRoute[i].ToUnity()));
                        var hook = FindClosestTransform(targetRackTransforms, cable.LocalRoute[i].ToUnity());
                        if (hook != null && hook != linkA.transform && hook != linkB.transform) rawLinkRoute.Add(hook);
                    }
                }
                worldRoute.Add(attachB.position);
                rawLinkRoute.Add(linkB.transform);

                var startEp = BuildCableEndpointSaveData(devA, linkA, typeA);
                var endEp = BuildCableEndpointSaveData(devB, linkB, typeB);

                // Reserve a fresh cable id via the game's own path so CablePositions
                // initialises its runtime dictionaries consistently.
                int newId;
                try
                {
                    newId = positions.CreateNewCable();
                    if (newId <= 0) throw new InvalidOperationException("CreateNewCable returned invalid id");
                }
                catch
                {
                    newId = positions.nextCableId <= 0 ? 1 : positions.nextCableId;
                    positions.nextCableId = newId + 1;
                }

                var saveData = new CableSaveData
                {
                    cableID = newId,
                    startPoint = startEp,
                    endPoint = endEp,
                    waypoints = worldRoute,
                    midPointPositions = new Il2CppSystem.Collections.Generic.List<Vector3>(),
                    maxSpeed = cable.Speed,
                    cableColor = new Color(cable.ColorR, cable.ColorG, cable.ColorB, cable.ColorA)
                };

                // (1) Visual rope + CablePositions runtime dictionaries.
                positions.LoadCable(saveData);

                // (2) THE KEY STEP — register the cable in the live runtime registry
                // that the snapshot ctor of NetworkSaveData iterates at save time.
                // Without this, save->load loses the pasted cable. See CablePastePlan.md.
                var registered = RegisterCableWithVanillaRouting(newId, saveData, worldRoute);

                // (3) Endpoint metadata on each CableLink (cableId / parent device /
                // type / speed / SFP). Manual placement does this inside InteractOnClick
                // and SecondActionOnClick.
                FinalizeCableLink(linkA, devA, newId, false, typeA, cable.Speed, cable.SfpTypeA);
                FinalizeCableLink(linkB, devB, newId, true, typeB, cable.Speed, cable.SfpTypeB);

                try { devA.Server?.RegisterLink(linkA); } catch { /* non-fatal */ }
                try { devB.Server?.RegisterLink(linkB); } catch { /* non-fatal */ }

                MelonLogger.Msg($"[RackPlanner][CABLE-VANILLA] cableId={newId} vanillaRegistered={registered} A=({typeA},server='{startEp.serverID}',switch='{startEp.switchID}',sfp={cable.SfpTypeA}) B=({typeB},server='{endEp.serverID}',switch='{endEp.switchID}',sfp={cable.SfpTypeB}) points={worldRoute.Count}");
                result.CablesCreated++;
            }
            catch (Exception ex)
            {
                result.CablesFailed++;
                MelonLogger.Warning($"[RackPlanner] Kabel-Clone Fehler: {ex.Message}");
            }
        }

        if (template.Cables.Count > 0)
            result.Messages.Add($"Kabel: {result.CablesCreated} erstellt, {result.CablesFailed} fehlgeschlagen.");
    }

    private static CableEndpointSaveData BuildCableEndpointSaveData(DeviceWithRuntime dev, CableLink link, CableLink.TypeOfLink type)
    {
        return new CableEndpointSaveData
        {
            type = type,
            // Save/load matches endpoints against the actual port/link position, not
            // the rope attach transform (which is offset from the port). Using the
            // attach point here creates a visually present but non-persistent cable.
            position = link != null ? link.transform.position : Vector3.zero,
            customerID = dev?.Server != null ? dev.Server.GetCustomerID() : -1,
            switchID = dev?.Switch != null ? GetSwitchId(dev.Switch) : string.Empty,
            serverID = dev?.Server != null ? (dev.Server.ServerID ?? string.Empty) : string.Empty
        };
    }

    private static string GetSwitchId(NetworkSwitch sw)
    {
        if (sw == null) return string.Empty;
        try
        {
            var id = sw.GetSwitchId();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        catch { /* fallback */ }
        return sw.switchId ?? string.Empty;
    }

    /// <summary>
    /// Plug the freshly-pasted cable into the same live runtime structures the
    /// manual cable-tool would touch, so:
    /// (a) <see cref="NetworkSaveData"/>'s snapshot ctor finds it at save time
    ///     (it iterates <see cref="WaypointInitializationSystem.Instance"/>.cables);
    /// (b) routing / packet spawners get scheduled;
    /// (c) <see cref="NetworkMap"/> knows about the connection for path evaluation.
    /// Returns true if at least the primary registration (UpdateCableInfo) succeeded.
    /// </summary>
    private static bool RegisterCableWithVanillaRouting(int cableId, CableSaveData saveData, Il2CppSystem.Collections.Generic.List<Vector3> worldRoute)
    {
        if (cableId <= 0 || saveData == null) return false;

        var primaryOk = false;

        // (a) WaypointInitializationSystem.UpdateCableInfo — adds to the dict the
        // save serializer iterates. This is THE registration that was missing.
        try
        {
            var wis = WaypointInitializationSystem.Instance;
            if (wis == null)
            {
                MelonLogger.Warning("[RackPlanner][CABLE-VANILLA] WaypointInitializationSystem.Instance is null; cable will NOT be saved");
            }
            else
            {
                var info = BuildCableInfo(cableId, saveData, worldRoute);
                // UpdateCableInfo() in vanilla only mutates EXISTING dictionary
                // entries (it has no public Add path — manual placement adds the
                // entry inside the private CreateCableWithSpawners called by
                // GenerateFinalPath). Insert ourselves into the dict first so the
                // subsequent UpdateCableInfo + save snapshot both see the cable.
                try
                {
                    var dict = wis.cables;
                    if (dict != null) dict[cableId] = info;
                    else MelonLogger.Warning($"[RackPlanner][CABLE-VANILLA] wis.cables is null for {cableId}");
                }
                catch (Exception exDict)
                {
                    MelonLogger.Warning($"[RackPlanner][CABLE-VANILLA] direct dict insert failed for {cableId}: {exDict.Message}");
                }
                wis.UpdateCableInfo(cableId, info);
                primaryOk = true;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner][CABLE-VANILLA] UpdateCableInfo failed for {cableId}: {ex.Message}");
        }

        // (b) NetworkMap.RegisterCableConnection — fills cableConnections so route
        // evaluation can find the topology endpoints. Idempotent.
        try
        {
            var nm = NetworkMap.instance;
            if (nm != null)
            {
                nm.RegisterCableConnection(
                    cableId,
                    saveData.startPoint?.position ?? Vector3.zero,
                    saveData.endPoint?.position ?? Vector3.zero,
                    saveData.startPoint != null ? saveData.startPoint.type : CableLink.TypeOfLink.None,
                    saveData.endPoint != null ? saveData.endPoint.type : CableLink.TypeOfLink.None,
                    saveData.startPoint?.switchID ?? string.Empty,
                    saveData.endPoint?.switchID ?? string.Empty,
                    saveData.startPoint?.customerID ?? -1,
                    saveData.endPoint?.customerID ?? -1,
                    saveData.startPoint?.serverID ?? string.Empty,
                    saveData.endPoint?.serverID ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner][CABLE-VANILLA] NetworkMap.RegisterCableConnection failed for {cableId}: {ex.Message}");
        }

        // (c) Trigger route recomputation so the new cable starts carrying packets.
        try { WaypointInitializationSystem.Instance?.RequestRouteEvaluation(); }
        catch (Exception ex) { MelonLogger.Warning($"[RackPlanner][CABLE-VANILLA] RequestRouteEvaluation failed: {ex.Message}"); }

        return primaryOk;
    }

    private static WaypointInitializationSystem.CableInfo BuildCableInfo(int cableId, CableSaveData saveData, Il2CppSystem.Collections.Generic.List<Vector3> worldRoute)
    {
        var info = new WaypointInitializationSystem.CableInfo
        {
            CableID = cableId,
            StartPoint = BuildCableEndpoint(saveData.startPoint),
            EndPoint = BuildCableEndpoint(saveData.endPoint),
            Waypoints = worldRoute ?? new Il2CppSystem.Collections.Generic.List<Vector3>(),
            MaxSpeed = saveData.maxSpeed
        };
        return info;
    }

    private static WaypointInitializationSystem.CableEndpoint BuildCableEndpoint(CableEndpointSaveData ep)
    {
        if (ep == null) return new WaypointInitializationSystem.CableEndpoint();
        return new WaypointInitializationSystem.CableEndpoint
        {
            Type = ep.type,
            Position = ep.position,
            CustomerID = ep.customerID,
            SwitchID = ep.switchID ?? string.Empty,
            ServerID = ep.serverID ?? string.Empty
        };
    }

    private static void FinalizeCableLink(CableLink link, DeviceWithRuntime dev, int cableId, bool isEndPoint, CableLink.TypeOfLink type, float speed, int sfpType)
    {
        if (link == null) return;

        link.cableIDsOnLink = cableId;
        link.isStartOrEnd = true;
        link.isEndPoint = isEndPoint;
        link.typeOfLink = type;
        if (dev?.Server != null)
        {
            link.parentServer = dev.Server;
            link.CustomerID = dev.Server.GetCustomerID();
        }
        if (dev?.Switch != null)
        {
            link.parentSwitch = dev.Switch;
            link.switchID = GetSwitchId(dev.Switch);
        }
        if (dev?.Patch != null)
            link.parentPatchPanel = dev.Patch;

        if (speed > 0f)
        {
            try { link.SetConnectionSpeed(speed); } catch { link.connectionSpeed = speed; }
        }

        if (sfpType >= 0)
            EnsureSfpInserted(link, sfpType, speed);
    }

    private static void EnsureSfpInserted(CableLink link, int sfpType, float speed)
    {
        if (link == null || sfpType < 0) return;

        SFPModule module = null;
        try { module = link.insertedSFP; } catch { /* fallback */ }

        if (module == null)
        {
            try
            {
                var prefabs = MainGameManager.instance?.sfpPrefabs;
                if (prefabs != null && sfpType >= 0 && sfpType < prefabs.Count)
                {
                    var prefab = prefabs[sfpType];
                    if (prefab != null)
                    {
                        var inst = Object.Instantiate(prefab, link.transform.position, link.transform.rotation);
                        module = inst != null ? inst.GetComponent<SFPModule>() : null;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackPlanner] SFP instantiate failed type={sfpType}: {ex.Message}");
            }
        }

        if (module != null)
        {
            try { module.prefabID = sfpType; } catch { /* best effort: many game prefabs use the same id as sfpType */ }
            try { module.sfpType = sfpType; } catch { /* non-fatal */ }
            if (speed > 0f) { try { module.speed = speed; } catch { /* non-fatal */ } }
            try { module.InsertDirectlyIntoPort(link); }
            catch
            {
                try { module.InsertedInSFPPort(link, true); } catch { /* direct state fallback below */ }
            }
            try { link.insertedSFP = module; } catch { /* non-fatal */ }
        }

        try { link.InsertSFP(speed > 0f ? speed : link.connectionSpeed, sfpType, module); }
        catch
        {
            link.sfpTypeInserted = sfpType;
            if (speed > 0f) link.connectionSpeed = speed;
        }

        link.sfpTypeInserted = sfpType;
        AddSfpToCurrentSave(module, link, sfpType);
    }

    private static SaveData GetCurrentSaveData()
    {
        try { if (SaveData.instance != null) return SaveData.instance; } catch { /* fallback */ }
        try { return SaveData._current; } catch { return null; }
    }

    private static NetworkSaveData EnsureNetworkSaveData()
    {
        var save = GetCurrentSaveData();
        if (save == null) return null;

        if (save.networkData == null)
            save.networkData = new NetworkSaveData();
        if (save.networkData.servers == null)
            save.networkData.servers = new Il2CppSystem.Collections.Generic.List<ServerSaveData>();
        if (save.networkData.switches == null)
            save.networkData.switches = new Il2CppSystem.Collections.Generic.List<SwitchSaveData>();
        if (save.networkData.patchPanels == null)
            save.networkData.patchPanels = new Il2CppSystem.Collections.Generic.List<PatchPanelSaveData>();
        if (save.networkData.cables == null)
            save.networkData.cables = new Il2CppSystem.Collections.Generic.List<CableSaveData>();
        if (save.networkData.sfpModules == null)
            save.networkData.sfpModules = new Il2CppSystem.Collections.Generic.List<SFPSaveData>();
        return save.networkData;
    }

    // ---- Diagnostics helpers (used by DiagnosticsService and remaining log calls) ----

    internal static int SafeCount<T>(Il2CppSystem.Collections.Generic.List<T> list)
    {
        try { return list?.Count ?? -1; } catch { return -2; }
    }

    internal static int SafeCount<T>(Il2CppSystem.Collections.Generic.Dictionary<int, T> dict)
    {
        try { return dict?.Count ?? -1; } catch { return -2; }
    }

    internal static int SafeValue(Func<int> getter)
    {
        try { return getter(); } catch { return -2; }
    }

    /// <summary>
    /// Belt-and-suspenders: keep an SFPSaveData entry in the current save snapshot for
    /// every SFP we inserted via paste. Strictly only useful if the vanilla snapshot
    /// ctor for some reason missed the live SFPModule (it shouldn't - but harmless).
    /// </summary>
    private static void AddSfpToCurrentSave(SFPModule module, CableLink link, int sfpType)
    {
        if (link == null || sfpType < 0) return;
        try
        {
            var network = EnsureNetworkSaveData();
            if (network?.sfpModules == null) return;

            var portPosition = link.transform.position;
            for (var i = 0; i < network.sfpModules.Count; i++)
            {
                var existing = network.sfpModules[i];
                if (existing != null && existing.isInserted && (existing.portPosition - portPosition).sqrMagnitude < 0.0001f)
                    return;
            }

            var tr = module != null ? module.transform : link.transform;
            var prefabId = sfpType;
            try { if (module != null) prefabId = module.prefabID; } catch { /* fallback */ }
            network.sfpModules.Add(new SFPSaveData
            {
                prefabID = prefabId,
                position = tr.position,
                rotation = tr.rotation,
                isInserted = true,
                portPosition = portPosition
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner] AddSfpToCurrentSave failed: {ex.Message}");
        }
    }

    private static string GetEndpointId(DeviceWithRuntime d)
    {
        if (d.Server != null) return d.Server.ServerID ?? string.Empty;
        if (d.Switch != null) return d.Switch.switchId ?? string.Empty;
        if (d.Patch != null) return d.Patch.patchPanelId ?? string.Empty;
        return string.Empty;
    }

    private static int _runtimeUidFloor = -1;

    /// <summary>
    /// Ensures <paramref name="pos"/> has a strictly-positive
    /// <see cref="RackPosition.rackPosGlobalUID"/>. Freshly bought racks (and any
    /// that haven't run their Awake/Start yet) carry the uninitialized sentinel
    /// (typically -1). Pasting devices into such positions persists
    /// <c>rackPositionUID = -1</c> on the device, and after reload
    /// <see cref="RackPosition.GetByUID"/> returns null → the device's
    /// <c>currentRackPosition</c> stays null → <c>BuildDeviceMap</c> drops it →
    /// rack shows 0/47U on the floor map and clicking the device triggers
    /// "Device needs to be in Rack". We assign a fresh UID using the same global
    /// counter the save serializer uses (<c>SaveData.lastUsedRackPositionGlobalUID</c>).
    /// </summary>
    private static void EnsureValidRackPositionUid(RackPosition pos)
    {
        if (pos == null) return;
        try { if (pos.rackPosGlobalUID > 0) return; } catch { return; }

        int newUid;
        try
        {
            var save = SaveData.instance;
            if (save != null)
            {
                save.lastUsedRackPositionGlobalUID += 1;
                newUid = save.lastUsedRackPositionGlobalUID;
            }
            else
            {
                if (_runtimeUidFloor < 100000) _runtimeUidFloor = 100000;
                _runtimeUidFloor += 1;
                newUid = _runtimeUidFloor;
            }
        }
        catch
        {
            if (_runtimeUidFloor < 100000) _runtimeUidFloor = 100000;
            _runtimeUidFloor += 1;
            newUid = _runtimeUidFloor;
        }

        try { pos.SetUID(newUid); }
        catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] SetUID({newUid}) failed: {ex.Message}"); }
    }

    /// <summary>
    /// One-shot repair pass for saves that contain pasted devices left orphaned
    /// by the pre-fix mod (rack-position UID was -1 → load couldn't relink them).
    /// Strategy:
    ///  (1) Assign UIDs to every <see cref="RackPosition"/> that still has an
    ///      uninitialized one.
    ///  (2) For every <see cref="UsableObject"/> with a null
    ///      <c>currentRackPosition</c>, find the closest RackPosition by world
    ///      distance (within 0.6 m) and re-link it.
    ///  (3) Re-write the SaveData entry so the next save persists the new link.
    /// Triggered manually via the F6 hotkey from
    /// <see cref="DiagnosticsService.HandleHotkeys"/>.
    /// </summary>
    public static int RepairOrphanedPastedDevices()
    {
        var repaired = 0;
        try
        {
            var allPositions = Object.FindObjectsOfType<RackPosition>();
            foreach (var p in allPositions) EnsureValidRackPositionUid(p);

            // Re-register every Server / NetworkSwitch with NetworkMap so the
            // NetworkSaveData snapshot ctor (which iterates NetworkMap.servers /
            // .switches) picks them up at save time. This is idempotent — vanilla
            // entries get overwritten in place because the dict is keyed by the
            // device's stable ID.
            try
            {
                var nm = NetworkMap.instance;
                if (nm != null)
                {
                    foreach (var srv in Object.FindObjectsOfType<Server>())
                    {
                        if (srv == null) continue;
                        try { nm.RegisterServer(srv); } catch { /* non-fatal */ }
                    }
                    foreach (var sw in Object.FindObjectsOfType<NetworkSwitch>())
                    {
                        if (sw == null) continue;
                        try { nm.RegisterSwitch(sw); } catch { /* non-fatal */ }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackPlanner][REPAIR] NetworkMap re-register pass failed: {ex.Message}");
            }

            var allUsable = Object.FindObjectsOfType<UsableObject>();
            foreach (var uo in allUsable)
            {
                if (uo == null) continue;
                bool needsRelink;
                try { needsRelink = uo.currentRackPosition == null; } catch { needsRelink = false; }
                if (!needsRelink) continue;

                // Only repair components that are server/switch/patchPanel.
                var hasServer = uo.GetComponent<Server>() != null;
                var hasSwitch = uo.GetComponent<NetworkSwitch>() != null;
                var hasPatch  = uo.GetComponent<PatchPanel>() != null;
                if (!hasServer && !hasSwitch && !hasPatch) continue;

                var uoPos = uo.transform.position;
                RackPosition best = null;
                var bestDist = 0.6f * 0.6f;
                foreach (var p in allPositions)
                {
                    if (p == null || p.rack == null) continue;
                    var d = (p.transform.position - uoPos).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = p;
                    }
                }

                if (best == null) continue;
                try
                {
                    uo.currentRackPosition = best;
                    uo.rackPositionUID = best.rackPosGlobalUID;
                    try { uo.RemoveRigidbody(); } catch { /* non-fatal */ }
                    UpsertMountedDeviceSaveData(uo, best);
                    repaired++;
                    MelonLogger.Msg($"[RackPlanner][REPAIR] relinked '{uo.name}' → rack='{best.rack?.name}' uid={best.rackPosGlobalUID}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[RackPlanner][REPAIR] relink failed for '{uo.name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner][REPAIR] aborted: {ex.Message}");
        }
        MelonLogger.Msg($"[RackPlanner][REPAIR] done; relinked {repaired} orphaned device(s).");
        return repaired;
    }

    private static void SyncMountedDeviceWithVanillaState(Rack rack, RackPosition rackPosition, RackDeviceTemplate template, UsableObject usableObject)
    {
        if (rack == null || rackPosition == null || usableObject == null || template == null) return;

        try
        {
            var sizeU = Math.Max(1, template.SizeInU);
            var arrayStart = ResolveAnchorArrayIndex(rack, template.StartIndex, sizeU);

            // Some racks (especially freshly bought ones) may have RackPositions
            // whose rackPosGlobalUID is still the uninitialized sentinel (-1 / 0).
            // The save serializer stores rackPositionUID by value, so without a
            // real UID, after save+reload RackPosition.GetByUID(uid) returns null,
            // currentRackPosition stays null forever and BuildDeviceMap drops the
            // device → rack shows 0/47U on the floor map and clicking the device
            // triggers "Device needs to be in Rack". Assign a fresh UID via the
            // same global counter SaveData uses (lastUsedRackPositionGlobalUID).
            EnsureValidRackPositionUid(rackPosition);

            // Keep the runtime object in the same state as a vanilla rack insertion.
            // This prevents ValidateRackPosition-style checks from treating pasted
            // devices as loose shop/world objects ("Device needs to be in Rack").
            usableObject.currentRackPosition = rackPosition;
            usableObject.rackPositionUID = rackPosition.rackPosGlobalUID;
            usableObject.prefabID = template.PrefabId;
            usableObject.sizeInU = sizeU;
            usableObject.labelText = template.Label ?? string.Empty;

            if (arrayStart >= 0)
            {
                try { rack.MarkPositionAsUsed(arrayStart, sizeU); } catch { /* non-fatal: already marked in the spawn path */ }
            }

            for (var s = template.StartIndex; s < template.StartIndex + sizeU; s++)
            {
                var pos = GetPositionByPhysicalSlot(rack, s);
                if (pos != null) { try { pos.SetUsed(true); } catch { /* non-fatal */ } }
            }

            try { usableObject.RemoveRigidbody(); } catch { /* non-fatal */ }
            UpsertMountedDeviceSaveData(usableObject, rackPosition);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[RackPlanner] Vanilla device-state sync failed for '{template.DisplayName}': {ex.Message}");
        }
    }

    private static void UpsertMountedDeviceSaveData(UsableObject usableObject, RackPosition rackPosition)
    {
        var network = EnsureNetworkSaveData();
        if (network == null || usableObject == null || rackPosition == null) return;

        var server = usableObject.GetComponent<Server>();
        if (server != null)
        {
            var data = BuildServerSaveData(server, usableObject, rackPosition);
            UpsertServerSaveData(network.servers, data);
            MelonLogger.Msg($"[RackPlanner][DEVICE-SAVE] upsert server='{data.serverID}' rackPos={data.rackPositionUID} saveServers={network.servers.Count}");
            return;
        }

        var sw = usableObject.GetComponent<NetworkSwitch>();
        if (sw != null)
        {
            var data = BuildSwitchSaveData(sw, rackPosition);
            UpsertSwitchSaveData(network.switches, data);
            MelonLogger.Msg($"[RackPlanner][DEVICE-SAVE] upsert switch='{data.switchID}' rackPos={data.rackPositionUID} saveSwitches={network.switches.Count}");
            return;
        }

        var patch = usableObject.GetComponent<PatchPanel>();
        if (patch != null)
        {
            var data = BuildPatchPanelSaveData(patch, rackPosition);
            UpsertPatchPanelSaveData(network.patchPanels, data);
            MelonLogger.Msg($"[RackPlanner][DEVICE-SAVE] upsert patch='{data.patchPanelID}' rackPos={data.rackPositionUID} savePatchPanels={network.patchPanels.Count}");
        }
    }

    private static ServerSaveData BuildServerSaveData(Server server, UsableObject usableObject, RackPosition rackPosition)
    {
        var tr = server.transform;
        var saveRackPosition = ResolveServerSaveRackPosition(server, usableObject, rackPosition);
        return new ServerSaveData
        {
            serverID = server.ServerID ?? string.Empty,
            customerID = SafeValue(() => server.GetCustomerID()),
            ip = server.IP ?? string.Empty,
            serverType = server.serverType,
            position = tr.position,
            rotation = tr.rotation,
            rackPositionUID = saveRackPosition.rackPosGlobalUID,
            prefabID = usableObject.prefabID,
            isOn = server.isOn,
            isBroken = server.isBroken,
            timeToBrake = server.timeToBrake,
            eolTime = server.eolTime,
            isWarningCleared = server.isWarningCleared,
            label = server.labelText ?? string.Empty
        };
    }

    private static RackPosition ResolveServerSaveRackPosition(Server server, UsableObject usableObject, RackPosition currentRackPosition)
    {
        if (server == null || usableObject == null || currentRackPosition == null || currentRackPosition.rack == null)
            return currentRackPosition;

        var serverId = server.ServerID ?? string.Empty;
        if (string.IsNullOrEmpty(serverId) || !PastedServerIds.Contains(serverId))
            return currentRackPosition;

        var sizeU = Math.Max(1, usableObject.sizeInU);
        if (sizeU <= 1)
            return currentRackPosition;

        // Vanilla's insert/load code persists the RackPosition that matches the
        // device transform anchor/pivot. Our template StartIndex is the physical
        // bottom slot, which is correct for occupancy stamping but wrong for some
        // multi-U server prefabs on reload. Use the closest RackPosition to the
        // final pasted transform as the save anchor, matching what reseating does.
        var closest = FindClosestRackPosition(currentRackPosition.rack, server.transform.position);
        if (closest == null)
            return currentRackPosition;

        EnsureValidRackPositionUid(closest);
        if (closest != currentRackPosition)
        {
            MelonLogger.Msg($"[RackPlanner][SERVER-SAVE-ANCHOR] server='{serverId}' sizeU={sizeU} rackPosUID {currentRackPosition.rackPosGlobalUID}->{closest.rackPosGlobalUID} physSlot {ResolveSlotIndex(currentRackPosition.rack, currentRackPosition)}->{ResolveSlotIndex(currentRackPosition.rack, closest)}");
        }

        return closest;
    }

    private static RackPosition FindClosestRackPosition(Rack rack, Vector3 worldPosition)
    {
        if (rack == null || rack.positions == null) return null;

        RackPosition best = null;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < rack.positions.Count; i++)
        {
            var pos = rack.positions[i];
            if (pos == null) continue;

            var distance = (pos.transform.position - worldPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pos;
            }
        }

        return best;
    }

    private static SwitchSaveData BuildSwitchSaveData(NetworkSwitch sw, RackPosition rackPosition)
    {
        var tr = sw.transform;
        return new SwitchSaveData
        {
            switchID = GetSwitchId(sw),
            switchType = sw.switchType,
            position = tr.position,
            rotation = tr.rotation,
            rackPositionUID = rackPosition.rackPosGlobalUID,
            isOn = sw.isOn,
            label = sw.labelText ?? string.Empty,
            isBroken = sw.isBroken,
            timeToBrake = sw.timeToBrake,
            eolTime = sw.eolTime,
            isWarningCleared = sw.isWarningCleared,
            portVlanFilters = new Il2CppSystem.Collections.Generic.List<PortVlanFilterData>()
        };
    }

    private static PatchPanelSaveData BuildPatchPanelSaveData(PatchPanel patch, RackPosition rackPosition)
    {
        var tr = patch.transform;
        return new PatchPanelSaveData
        {
            patchPanelID = patch.patchPanelId ?? string.Empty,
            position = tr.position,
            rotation = tr.rotation,
            rackPositionUID = rackPosition.rackPosGlobalUID,
            patchPanelType = patch.patchPanelType
        };
    }

    private static void UpsertServerSaveData(Il2CppSystem.Collections.Generic.List<ServerSaveData> servers, ServerSaveData data)
    {
        if (servers == null || data == null) return;
        for (var i = 0; i < servers.Count; i++)
        {
            var existing = servers[i];
            if (existing == null) continue;
            if ((!string.IsNullOrEmpty(data.serverID) && existing.serverID == data.serverID)
                || existing.rackPositionUID == data.rackPositionUID)
            {
                servers[i] = data;
                return;
            }
        }
        servers.Add(data);
    }

    private static void UpsertSwitchSaveData(Il2CppSystem.Collections.Generic.List<SwitchSaveData> switches, SwitchSaveData data)
    {
        if (switches == null || data == null) return;
        for (var i = 0; i < switches.Count; i++)
        {
            var existing = switches[i];
            if (existing == null) continue;
            if ((!string.IsNullOrEmpty(data.switchID) && existing.switchID == data.switchID)
                || existing.rackPositionUID == data.rackPositionUID)
            {
                switches[i] = data;
                return;
            }
        }
        switches.Add(data);
    }

    private static void UpsertPatchPanelSaveData(Il2CppSystem.Collections.Generic.List<PatchPanelSaveData> patchPanels, PatchPanelSaveData data)
    {
        if (patchPanels == null || data == null) return;
        for (var i = 0; i < patchPanels.Count; i++)
        {
            var existing = patchPanels[i];
            if (existing == null) continue;
            if ((!string.IsNullOrEmpty(data.patchPanelID) && existing.patchPanelID == data.patchPanelID)
                || existing.rackPositionUID == data.rackPositionUID)
            {
                patchPanels[i] = data;
                return;
            }
        }
        patchPanels.Add(data);
    }

    // ---------------------------------------------------------------- spawning -----

    private static GameObject ResolvePrefab(RackDeviceTemplate template)
    {
        var mainGameManager = MainGameManager.instance;
        if (mainGameManager == null)
            return null;

        return template.Kind switch
        {
            RackDeviceKind.Server => ResolveFromArray(mainGameManager.serverPrefabs, template.PrefabId),
            RackDeviceKind.NetworkSwitch => ResolveFromArray(mainGameManager.switchesPrefabs, template.VariantId),
            RackDeviceKind.PatchPanel => ResolveFromArray(mainGameManager.patchPanelsPrefabs, template.VariantId),
            _ => null
        };
    }

    private static GameObject ResolveFromArray(Il2CppReferenceArray<GameObject> prefabs, int index)
    {
        if (prefabs == null || index < 0 || index >= prefabs.Count)
            return null;

        return prefabs[index];
    }

    private static bool TrySpawnIntoRack(Rack rack, RackDeviceTemplate template, out string message)
    {
        message = string.Empty;

        try
        {
            var sizeU = Math.Max(1, template.SizeInU);
            var anchorPhysicalSlot = ResolveAnchorPhysicalSlot(template.StartIndex, sizeU);
            if (rack.positions == null || template.StartIndex < 0 || anchorPhysicalSlot >= rack.positions.Count)
            {
                message = $"{template.DisplayName}: ungültige Zielposition.";
                return false;
            }

            // template.StartIndex is the physically lower occupied slot (0 = bottom)
            // for UI/occupancy planning. Vanilla mounting uses the physically upper
            // slot as the RackPosition anchor and passes that anchor's array index to
            // IsPositionAvailable / MarkPositionAsUsed. If we initialize UID on the
            // lower slot but mark from the upper array index, Rack.MarkPositionAsUsed
            // writes -1 owners into isPositionUsed[] and reload loses server occupancy.
            var arrayStart = ResolveAnchorArrayIndex(rack, template.StartIndex, sizeU);
            if (arrayStart < 0)
            {
                message = $"{template.DisplayName}: ungültige Zielposition.";
                return false;
            }

            if (!rack.IsPositionAvailable(arrayStart, sizeU))
            {
                message = $"{template.DisplayName}: Ziel-Slots sind nicht frei.";
                return false;
            }

            var rackPosition = GetPositionByPhysicalSlot(rack, anchorPhysicalSlot);
            if (rackPosition == null)
            {
                message = $"{template.DisplayName}: RackPosition fehlt.";
                return false;
            }

            var prefab = ResolvePrefab(template);
            if (prefab == null)
            {
                message = $"{template.DisplayName}: kein passendes Prefab gefunden.";
                return false;
            }

            // CRITICAL: instantiate the prefab in DEACTIVATED state so the device's
            // Awake()/Start() do not run before we have wired up currentRackPosition,
            // rackPositionUID, prefabID, sizeInU, label, parent etc. If Awake fired
            // first with currentRackPosition==null, ValidateRackPosition() reports
            // the device as "lost" and the game respawns it at
            // MainGameManager.placeToRespawnLostUsableObjects -> the device falls
            // out of the rack and currentRackPosition stays null forever, which
            // makes BuildDeviceMap drop it -> mod's floor-map shows the rack as
            // empty even though the GameObject exists somewhere in the world.
            var prefabWasActive = false;
            try { prefabWasActive = prefab.activeSelf; } catch { /* defensive */ }
            try { if (prefabWasActive) prefab.SetActive(false); } catch { /* non-fatal */ }

            GameObject instance;
            try
            {
                // Parent under MainGameManager.parentUsableObjects when available -
                // that is where the vanilla insert-flow places mounted devices. As a
                // fallback we use the rack-position transform.
                Transform parentTr = null;
                try { parentTr = MainGameManager.instance?.parentUsableObjects; } catch { /* fallback */ }
                if (parentTr == null) parentTr = rackPosition.transform;
                instance = Object.Instantiate(prefab, parentTr);
                if (instance == null)
                {
                    message = $"{template.DisplayName}: Prefab-Instanzierung fehlgeschlagen.";
                    return false;
                }
                // Ensure instance is inactive even if prefab.SetActive() above failed
                // (Object.Instantiate copies activeSelf from the prefab).
                try { if (instance.activeSelf) instance.SetActive(false); } catch { /* non-fatal */ }
            }
            finally
            {
                try { if (prefabWasActive) prefab.SetActive(true); } catch { /* non-fatal */ }
            }

            var usableObject = instance.GetComponent<UsableObject>();
            if (usableObject == null)
            {
                Object.Destroy(instance);
                message = $"{template.DisplayName}: UsableObject-Komponente fehlt.";
                return false;
            }

            // Wire up everything the *InsertedInRack methods will look at while we
            // are still INACTIVE (Awake has not run yet). After SetActive(true),
            // Awake sees a fully populated UsableObject.
            EnsureValidRackPositionUid(rackPosition);
            usableObject.currentRackPosition = rackPosition;
            usableObject.rackPositionUID = rackPosition.rackPosGlobalUID;
            usableObject.prefabID = template.PrefabId;
            usableObject.sizeInU = Math.Max(1, template.SizeInU);
            usableObject.labelText = template.Label;

            // Initial pose: identity in rack-position local space; we reapply the
            // captured pose AFTER InsertedInRack because the game centers the device
            // on the slot's anchor inside its own insert routine.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // NOTE: Rack.MarkPositionAsUsed / RackPosition.SetUsed are called LATER
            // (after *InsertedInRack), mirroring the vanilla RackPosition.InsertItemInRack
            // coroutine which marks the bitmap as its FINAL step. Calling it pre-activate
            // gets clobbered by something in the activation / Awake / *InsertedInRack
            // sequence — that is why "pickup + reseat" used to be the only way to fix
            // occupancy: reseating runs the real coroutine, which marks the bitmap last.

            var targetRackTr = rack.transform;
            var capturedLocalPos = template.LocalPos.ToUnity();
            var capturedLocalEuler = template.LocalEuler.ToUnity();
            var hasCapturedPose = capturedLocalPos.sqrMagnitude > 0.000001f || capturedLocalEuler.sqrMagnitude > 0.000001f;
            VerboseLog($"[RackPlanner][SPAWN] {template.DisplayName} kind={template.Kind} sizeU={template.SizeInU} bottomPhysSlot={template.StartIndex} anchorPhysSlot={anchorPhysicalSlot} anchorArray={arrayStart} parentWorld={rackPosition.transform.position} capturedLocalPos={capturedLocalPos} capturedLocalEuler={capturedLocalEuler} hasCapturedPose={hasCapturedPose}");

            // Activate now -> Awake() runs with all fields set.
            try { instance.SetActive(true); } catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] SetActive failed: {ex.Message}"); }

            // Mounted devices must NOT have an active rigidbody; otherwise they'd fall
            // out of the rack and the player couldn't pick them up cleanly. The game
            // calls RemoveRigidbody() during its own RackPosition.InsertItemInRack flow.
            try { usableObject.RemoveRigidbody(); } catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] RemoveRigidbody failed: {ex.Message}"); }

            switch (template.Kind)
            {
                case RackDeviceKind.Server:
                {
                    var server = instance.GetComponent<Server>();
                    if (server == null)
                        throw new InvalidOperationException("Server-Komponente fehlt.");

                    // Capture the state Awake left the server in BEFORE we call
                    // ServerInsertedInRack(null). The vanilla shop flow always feeds
                    // ServerInsertedInRack a server that has a unique ServerID,
                    // a generated IP, an assigned customer/app and a freshly rolled
                    // timeToBrake/eolTime. If any of those are missing, the LOAD
                    // path on the next save->reload cycle silently drops the entry.
                    string preServerId = null;
                    string preIp = null;
                    int preServerType = -1, preAppId = -1, preTimeToBrake = -1, preEolTime = -1;
                    try { preServerId = server.ServerID; } catch { /* defensive */ }
                    try { preIp = server.IP; } catch { /* defensive */ }
                    try { preServerType = server.serverType; } catch { /* defensive */ }
                    try { preAppId = server.appID; } catch { /* defensive */ }
                    try { preTimeToBrake = server.timeToBrake; } catch { /* defensive */ }
                    try { preEolTime = server.eolTime; } catch { /* defensive */ }
                    MelonLogger.Msg($"[RackPlanner][SPAWN-SRV][PRE-INSERT] name={instance.name} ServerID='{preServerId ?? "<null>"}' IP='{preIp ?? "<null>"}' serverType={preServerType} appID={preAppId} ttb={preTimeToBrake} eol={preEolTime} curRP={(server.currentRackPosition!=null?"set":"null")} rpUID={server.rackPositionUID}");

                    // Pass null = fresh-insert path. The game initialises a unique
                    // ServerID, valid IP, customer assignment, eolTime/timeToBrake
                    // and the broken/warning state for us. Passing a stub
                    // ServerSaveData would take the LOAD path and leave us with
                    // serverID="", customerID=-1 ("error customer"), eolTime=0.
                    server.ServerInsertedInRack(null);

                    // Defensive: if the fresh-insert path did not generate a unique
                    // ServerID for some reason, do it ourselves. NetworkMap.servers
                    // is keyed by ServerID; a duplicate / empty ID causes the
                    // snapshot ctor to drop entries on save.
                    if (string.IsNullOrEmpty(server.ServerID))
                    {
                        try
                        {
                            var newId = server.GenerateUniqueServerId();
                            if (!string.IsNullOrEmpty(newId)) server.ServerID = newId;
                            MelonLogger.Msg($"[RackPlanner][SPAWN-SRV][FALLBACK-ID] generated ServerID='{server.ServerID}' for '{instance.name}'");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[RackPlanner][SPAWN-SRV] GenerateUniqueServerId failed: {ex.Message}");
                        }
                    }

                    // ROOT-CAUSE FIX: ServerInsertedInRack(null) generates a ServerID
                    // and picks a customer but does NOT set serverType / IP — those
                    // are set by ComputerShop in the manual flow. Without them the
                    // LOAD path treats the saved entry as "uninitialised" and the
                    // server vanishes on save→reload.
                    //
                    // We deliberately DO NOT copy the source's IP, customer or appID:
                    // a pasted server should behave like a freshly-bought one (own IP,
                    // own customer assignment). Only `serverType` is replayed because
                    // it identifies which prefab variant this is — the shop sets it
                    // implicitly by picking the right prefab from MainGameManager
                    // .serverPrefabs[serverType], so the source's serverType is the
                    // correct value for the same prefab.
                    try
                    {
                        if (template.ServerType > 0 && server.serverType != template.ServerType)
                            server.serverType = template.ServerType;
                    }
                    catch (Exception ex) { MelonLogger.Warning($"[RackPlanner][SPAWN-SRV] set serverType failed: {ex.Message}"); }

                    try
                    {
                        var ipBlank = string.IsNullOrEmpty(server.IP) || server.IP == "0.0.0.0";
                        if (ipBlank)
                        {
                            // ButtonClickChangeIP cycles to the next free auto-generated
                            // IP — same path the shop uses to give a fresh server its
                            // first valid address.
                            server.ButtonClickChangeIP();
                        }
                    }
                    catch (Exception ex) { MelonLogger.Warning($"[RackPlanner][SPAWN-SRV] ButtonClickChangeIP failed: {ex.Message}"); }

                    // Capture POST state to compare with the manual flow.
                    string postServerId = null;
                    string postIp = null;
                    int postServerType = -1, postAppId = -1, postTimeToBrake = -1, postEolTime = -1, postCustomer = -1;
                    try { postServerId = server.ServerID; } catch { /* defensive */ }
                    try { postIp = server.IP; } catch { /* defensive */ }
                    try { postServerType = server.serverType; } catch { /* defensive */ }
                    try { postAppId = server.appID; } catch { /* defensive */ }
                    try { postTimeToBrake = server.timeToBrake; } catch { /* defensive */ }
                    try { postEolTime = server.eolTime; } catch { /* defensive */ }
                    try { postCustomer = server.GetCustomerID(); } catch { /* defensive */ }
                    MelonLogger.Msg($"[RackPlanner][SPAWN-SRV][POST-INSERT] name={instance.name} ServerID='{postServerId ?? "<null>"}' IP='{postIp ?? "<null>"}' serverType={postServerType} appID={postAppId} customer={postCustomer} ttb={postTimeToBrake} eol={postEolTime} curRP={(server.currentRackPosition!=null?"set":"null")} rpUID={server.rackPositionUID} parent='{(instance.transform.parent!=null?instance.transform.parent.name:"<null>")}'");

                    // CRITICAL: NetworkSaveData()'s snapshot ctor iterates
                    // NetworkMap.servers (Dictionary<string, Server>) at save time.
                    // Vanilla manual-insert registers the server only AFTER a customer
                    // claims it (UpdateCustomer path). For our paste flow there is no
                    // customer assignment, so the server is never registered → snapshot
                    // ctor doesn't see it → server vanishes from save. Force-register.
                    try { NetworkMap.instance?.RegisterServer(server); }
                    catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] RegisterServer failed: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(server.ServerID))
                    {
                        PastedServerIds.Add(server.ServerID);
                        MelonLogger.Msg($"[RackPlanner][SPAWN-SRV][TRACK-PASTED] ServerID='{server.ServerID}' sizeU={Math.Max(1, usableObject.sizeInU)} bottomPhysSlot={template.StartIndex} anchorPhysSlot={anchorPhysicalSlot} anchorUID={rackPosition.rackPosGlobalUID}");
                    }
                    if (!string.IsNullOrEmpty(template.Label)) server.labelText = template.Label;
                    if (server.isOn != template.IsPoweredOn)
                        server.PowerButton(template.IsPoweredOn);
                    break;
                }
                case RackDeviceKind.NetworkSwitch:
                {
                    var networkSwitch = instance.GetComponent<NetworkSwitch>();
                    if (networkSwitch == null)
                        throw new InvalidOperationException("Switch-Komponente fehlt.");

                    networkSwitch.SwitchInsertedInRack(null); // fresh-insert path
                    // Idempotent insurance: if SwitchInsertedInRack already registered
                    // the switch this is a no-op (Dictionary indexer overwrites).
                    try { NetworkMap.instance?.RegisterSwitch(networkSwitch); }
                    catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] RegisterSwitch failed: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(template.Label)) networkSwitch.labelText = template.Label;
                    if (networkSwitch.isOn != template.IsPoweredOn)
                        networkSwitch.PowerButton(template.IsPoweredOn);
                    break;
                }
                case RackDeviceKind.PatchPanel:
                {
                    var patchPanel = instance.GetComponent<PatchPanel>();
                    if (patchPanel == null)
                        throw new InvalidOperationException("PatchPanel-Komponente fehlt.");

                    patchPanel.InsertedInRack(null); // fresh-insert path
                    if (!string.IsNullOrEmpty(template.Label)) patchPanel.labelText = template.Label;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Belt-and-suspenders: re-assert currentRackPosition / UID / prefabID
            // / sizeInU after *InsertedInRack in case the vanilla method overwrote
            // them. This is what makes the mod's floor-map count the slot as used.
            EnsureValidRackPositionUid(rackPosition);
            usableObject.currentRackPosition = rackPosition;
            usableObject.rackPositionUID = rackPosition.rackPosGlobalUID;
            usableObject.prefabID = template.PrefabId;
            usableObject.sizeInU = Math.Max(1, template.SizeInU);

            // ROOT-CAUSE FIX for "pasted rack shows 2/47U occupancy until I reseat".
            // The vanilla RackPosition.InsertItemInRack coroutine marks the slot
            // bitmap (Rack.isPositionUsed[]) as its FINAL step, AFTER the device's
            // *InsertedInRack method has run. Doing it earlier (pre-SetActive or
            // pre-*InsertedInRack) gets clobbered — which is exactly what we
            // observed. Mark NOW, post-insert, exactly like the coroutine does.
            try
            {
                int beforeUsed = 0;
                try { if (rack.isPositionUsed != null) for (var i = 0; i < rack.isPositionUsed.Count; i++) if (rack.isPositionUsed[i] != 0) beforeUsed++; } catch { /* defensive */ }
                rack.MarkPositionAsUsed(arrayStart, sizeU);
                rackPosition.SetUsed(true);
                int afterUsed = 0;
                try { if (rack.isPositionUsed != null) for (var i = 0; i < rack.isPositionUsed.Count; i++) if (rack.isPositionUsed[i] != 0) afterUsed++; } catch { /* defensive */ }
                VerboseLog($"[RackPlanner][SPAWN-MARK] {template.DisplayName} anchorArray={arrayStart} sizeU={sizeU} anchorPhysSlot={anchorPhysicalSlot} rackPosIdx={rackPosition.positionIndex} anchorUID={rackPosition.rackPosGlobalUID} usedBefore={beforeUsed} usedAfter={afterUsed}");
            }
            catch (Exception ex) { MelonLogger.Warning($"[RackPlanner] post-insert MarkPositionAsUsed failed: {ex.Message}"); }

            var postInsertLocal = targetRackTr.InverseTransformPoint(instance.transform.position);
            VerboseLog($"[RackPlanner][SPAWN-AFTER-INSERT] {template.DisplayName} kind={template.Kind} sizeU={template.SizeInU} bottomPhysSlot={template.StartIndex} anchorPhysSlot={anchorPhysicalSlot} localPos={postInsertLocal} world={instance.transform.position} rot={instance.transform.eulerAngles}");

            // The InsertedInRack methods are required for IDs/state, but their visual
            // placement is based on the newly selected rackPosition and can interpret a
            // multi-U device's top-pivot as the anchor. For copied templates we already
            // captured the exact final rack-local device pose from the source rack.
            // Apply it AFTER the game initialisation so there is no second offset and
            // the pasted device visually lands exactly where the copied one was.
            if (hasCapturedPose)
            {
                instance.transform.position = targetRackTr.TransformPoint(capturedLocalPos);
                instance.transform.rotation = targetRackTr.rotation * Quaternion.Euler(capturedLocalEuler);
                VerboseLog($"[RackPlanner][SPAWN-POSE-CORRECTED] {template.DisplayName} kind={template.Kind} sizeU={template.SizeInU} physSlot={template.StartIndex} localPos={targetRackTr.InverseTransformPoint(instance.transform.position)} world={instance.transform.position} rot={instance.transform.eulerAngles}");
            }
            else if (sizeU > 1)
            {
                var pivotSlot = template.StartIndex + sizeU - 1;
                var pivotRackPosition = GetPositionByPhysicalSlot(rack, pivotSlot);
                if (pivotRackPosition != null)
                {
                    instance.transform.position = pivotRackPosition.transform.position;
                    VerboseLog($"[RackPlanner][SPAWN-PIVOT-FALLBACK] {template.DisplayName} kind={template.Kind} sizeU={template.SizeInU} bottomPhysSlot={template.StartIndex} pivotPhysSlot={pivotSlot} localPos={targetRackTr.InverseTransformPoint(instance.transform.position)} world={instance.transform.position} rot={instance.transform.eulerAngles}");
                }
            }

            SyncMountedDeviceWithVanillaState(rack, rackPosition, template, usableObject);

            message = $"{template.DisplayName} eingefügt (+{CalculateAdjustedPrice(template.BasePrice)}).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"{template.DisplayName}: Fehler beim Einfügen – {ex.Message}";
            return false;
        }
    }

    // ---------------------------------------------------------------- helpers -----

    private static RackDeviceTemplate FindBlockingDevice(RackDeviceTemplate template, RackDeviceTemplate[] occupancy)
    {
        RackDeviceTemplate blocking = null;
        for (var i = template.StartIndex; i < template.StartIndex + template.SizeInU; i++)
        {
            var slot = occupancy[i];
            if (slot == null)
                continue;

            if (blocking == null)
                blocking = slot;
            else if (!ReferenceEquals(blocking, slot) && !AreEquivalent(blocking, slot))
                return slot;
        }

        return blocking;
    }

    private static RackDeviceTemplate[] BuildOccupancy(RackRuntimeInfo rackInfo)
    {
        var occupancy = new RackDeviceTemplate[rackInfo.TotalSlots];
        foreach (var device in rackInfo.Devices)
            StampDevice(device, occupancy);

        return occupancy;
    }

    private static void StampDevice(RackDeviceTemplate device, RackDeviceTemplate[] occupancy)
    {
        var upperBound = Math.Min(occupancy.Length, device.StartIndex + Math.Max(1, device.SizeInU));
        for (var i = Math.Max(0, device.StartIndex); i < upperBound; i++)
            occupancy[i] = device;
    }

    private static bool IsWithinRack(RackDeviceTemplate template, int totalSlots)
    {
        return template.StartIndex >= 0 && template.SizeInU > 0 && template.StartIndex + template.SizeInU <= totalSlots;
    }

    private static bool AreEquivalent(RackDeviceTemplate left, RackDeviceTemplate right)
    {
        return left.Kind == right.Kind
            && left.StartIndex == right.StartIndex
            && left.SizeInU == right.SizeInU
            && left.VariantId == right.VariantId;
    }

    private static RackDeviceTemplate CloneDeviceTemplate(RackDeviceTemplate source)
    {
        return new RackDeviceTemplate
        {
            Kind = source.Kind,
            StartIndex = source.StartIndex,
            SizeInU = source.SizeInU,
            PrefabId = source.PrefabId,
            VariantId = source.VariantId,
            BasePrice = source.BasePrice,
            DisplayName = source.DisplayName,
            Label = source.Label,
            IsPoweredOn = source.IsPoweredOn,
            LocalPos = source.LocalPos,
            LocalEuler = source.LocalEuler
        };
    }

    private static string BuildRackLabel(Rack rack, int index)
    {
        var pos = rack.transform.position;
        return $"Rack {index + 1} ({pos.x:0.#}/{pos.z:0.#})";
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    public static int CalculateAdjustedPrice(int basePrice)
    {
        return Mathf.CeilToInt(basePrice * PriceMultiplier);
    }
}

