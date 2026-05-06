# Cable / Server Paste — Root Cause & Fix Plan

> Recherche-Stand: 2026-05-06. Dekompilat unter `decomp_full/` (ILSpy 10.0.1.8346 auf
> `D:\SteamLibrary\steamapps\common\Data Center\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out\Assembly-CSharp.dll`).
> Cpp2IL liefert nur Signaturen + Field-Offsets, keine Method-Bodies — die Architektur
> lässt sich aber aus Namen, Signaturen und Reihenfolge eindeutig rekonstruieren.

## 1. So funktioniert das Save-Modell wirklich

Save-Datei (BinaryFormatter, siehe [SaveSystem.cs:101](decomp_full/SaveSystem.cs)) enthält
ein Wurzel-Objekt [`SaveData`](decomp_full/SaveData.cs) mit Feld
`networkData : NetworkSaveData` ([SaveData.cs:36](decomp_full/SaveData.cs)).

[`NetworkSaveData`](decomp_full/NetworkSaveData.cs) ist die einzige Liste-Sammlung für
Kabel:
```csharp
public List<ServerSaveData>     servers;       // 0x10
public List<SwitchSaveData>     switches;      // 0x18
public List<PatchPanelSaveData> patchPanels;   // 0x20
public List<CableSaveData>      cables;        // 0x28
public List<SFPSaveData>        sfpModules;    // 0x38
public List<CableLinkLabelData> cableLinkLabels; // 0x48
```
Der Parameterless-Konstruktor `NetworkSaveData()` hat eine RVA-Länge von **0x1C21**
(~7200 Byte nativem Code). Das ist viel zu groß für eine simple Initialisierung —
**er sammelt die Live-Welt** (Pattern: „snapshot constructor"). Er iteriert
also intern über **die echte Laufzeit-Registrierung** der Kabel und befüllt
`cables`. Wer ist diese Laufzeit-Registrierung?

Antwort: [`WaypointInitializationSystem`](decomp_full/WaypointInitializationSystem.cs)
ist ein DOTS-`SystemBase` (Singleton via `Instance`) mit privatem Feld
```csharp
private Dictionary<int, CableInfo> cables;        // 0x38
```
und einer öffentlichen API:
```csharp
public List<CableInfo> GetAllCables();
public CableInfo? GetCableInfo(int cableId);
public void UpdateCableInfo(int cableId, CableInfo info);     // <-- Registrar
public void LoadNetworkState(NetworkSaveData, List<RackPosition>, int saveVersion);
public void OnCableRemoved(int cableId);
private void RegisterCableInNetworkMap(CableInfo cableInfo);
```
`CableInfo` enthält `CableID, StartPoint:CableEndpoint, EndPoint:CableEndpoint,
Waypoints:List<Vector3>, MaxSpeed, ForwardSpawner:Entity, BackwardSpawner:Entity`.
`CableEndpoint` enthält `Type:CableLink.TypeOfLink, Position:Vector3, CustomerID,
SwitchID, ServerID`.

**Daraus folgt der Save-Pfad eindeutig:**
> `NetworkSaveData()` ⇒ iteriert `WaypointInitializationSystem.Instance.cables` ⇒
> baut pro Eintrag `CableSaveData { cableID, startPoint, endPoint, waypoints,
> midPointPositions, maxSpeed, cableColor }` und hängt es an `cables` an.

Pasted Cables landen aktuell **nie** in
`WaypointInitializationSystem.Instance.cables`. Daher werden sie nicht
serialisiert, daher sind sie nach Save→Load weg. **Das ist die echte Ursache.**

## 2. Wo der bisherige Mod-Code danebenliegt

In [`RackPlannerService.ApplyCables`](TestDecompileCSharp/Services/RackPlannerService.cs) (Zeile 836):

* Ruft `CablePositions.instance.LoadCable(saveData)` (Zeile 975) auf — das baut
  nur das visuelle Rohr (siehe `CablePositions` Felder: `cables`, `rawCablePoints`,
  `cableEntities`, `cableGameObjects`, `cableMaterials`, `rawLinkTransforms` —
  alle „Geometrie". Nicht das Routing.
* Hält ein eigenes `PastedCableSaveRegistry` (Zeile 31) und versucht, die
  fehlenden `CableSaveData`-Einträge per `SaveSystem.onSavingData`-Hook
  (`OnGameSavingData` → `MergePastedCableRegistryIntoCurrentSave`, Zeile 1216)
  in `SaveData.instance.networkData.cables` zu mergen.
* Genau diese Strategie hat **keine Wirkung**, weil `NetworkSaveData()` die
  Liste `cables` selbst beim Konstruieren neu aus der Live-Welt füllt — **nach**
  `onSavingData`. Unsere Einträge werden also vom Snapshot-Konstruktor
  überschrieben/ignoriert.

Für den **Server-Floor-Map-Bug** zeigt
[`RackPlannerService.GetRackInfos`](TestDecompileCSharp/Services/RackPlannerService.cs:122) →
`BuildDeviceMap` (Zeile 324):
* Filtert per `usableObject.currentRackPosition.rack == rack`.
* Aktuell wird `currentRackPosition` zwar gesetzt (Zeile 1639) bevor
  `Server.ServerInsertedInRack(null)` läuft — aber **nach** `Object.Instantiate`,
  also **nach** `Server.Awake()`. Wenn `Awake` (bzw. `OnLoadingStarted`) die
  Position validiert (`Server.ValidateRackPosition()`, Server.cs:284) und
  `currentRackPosition == null` findet, kann der Server sich selbst als „lost"
  markieren und in [`MainGameManager.placeToRespawnLostUsableObjects`](decomp_full/MainGameManager.cs:298)
  respawnt werden — was `currentRackPosition` zerstört.

## 3. So macht das Spiel es bei manueller Verlegung

### Server / Switch / PatchPanel manuell installieren
[`RackPosition`](decomp_full/RackPosition.cs) startet eine private Coroutine
`InsertItemInRack()` (Zeile 192) wenn der Spieler ein Gerät auf einen Slot zieht.
Die Display-Class (Zeile 14-48) referenziert `uo, server, networkSwitch,
patchPanel, startPosGO`. Der Ablauf ist:
1. Held UsableObject identifizieren (Server / NetworkSwitch / PatchPanel).
2. `uo.currentRackPosition = this; uo.rackPositionUID = this.rackPosGlobalUID;`
3. `this.SetUsed(true); rack.MarkPositionAsUsed(arrayStart, sizeU);`
4. `uo.RemoveRigidbody();`
5. Position/Parent setzen (typisch `MainGameManager.parentUsableObjects`).
6. Eines von:
   * `server.ServerInsertedInRack(null)` — fresh-insert Pfad
     ([Server.cs:198](decomp_full/Server.cs)).
   * `networkSwitch.SwitchInsertedInRack(null)` ([NetworkSwitch.cs:135](decomp_full/NetworkSwitch.cs)).
   * `patchPanel.InsertedInRack(null)` ([PatchPanel.cs:53](decomp_full/PatchPanel.cs)).

   Diese Funktionen vergeben ID, Customer, IP, EOL-Timer, registrieren das
   Gerät in [`NetworkMap.instance`](decomp_full/NetworkMap.cs:70) via
   `RegisterServer / RegisterSwitch / AddDevice` und initialisieren die
   `cableLinkSwitchPorts[] / cableLinkPorts[] / cablelinks[]`-Arrays.

### Kabel manuell verlegen
[`CablePositions`](decomp_full/CablePositions.cs) singleton + [`CableLink`](decomp_full/CableLink.cs):
1. Spieler klickt Start-Port (`CableLink.InteractOnClick`, Zeile 131):
   * `int id = CablePositions.instance.CreateNewCable();`
   * `CablePositions.instance.AssignNewPosition(id, startLink.transform, isStartPoint:true, false, type, serverID);`
   * `CablePositions` setzt intern `startCableLinkType, startServerID, startSwitchID, startCustomerID`.
   * `startLink.cableIDsOnLink = id; isStartOrEnd = true; typeOfLink = type; parentX = …;`
2. Spieler klickt Zwischenhalter (CableLink mit `isStartOrEnd=false`):
   * `CablePositions.instance.AssignNewPosition(id, holder.transform, false, false, None);`
3. Spieler klickt Ziel-Port (`CableLink.SecondActionOnClick`, Zeile 144):
   * `CablePositions.instance.AssignNewPosition(id, endLink.transform, false, isEndPoint:true, type, serverID);`
   * setzt `endCableLinkType, endServerID, endSwitchID, endCustomerID`.
   * `endLink.cableIDsOnLink = id; isStartOrEnd = true; isEndPoint = true; …`
   * Final: `CablePositions.GenerateFinalPath(id)` baut Mesh.
   * Registrierung: `WaypointInitializationSystem.Instance.UpdateCableInfo(id, info)` und
     intern `NetworkMap.instance.RegisterCableConnection(id, startPos, endPos,
     startType, endType, startSwitchID, endSwitchID, startCustomerID, endCustomerID,
     startServerID, endServerID)`.
4. `WaypointInitializationSystem.RequestRouteEvaluation()` → Routen werden neu berechnet,
   Spawner erzeugt (`CreateSpawnersForCable`, `ActivateSpawnersForCable`).

### Save / Load Symmetrie
* **Save**: `NetworkSaveData()`-Konstruktor iteriert `WaypointInitializationSystem.cables` →
  serialisiert pro Eintrag ein `CableSaveData`.
* **Load**: `WaypointInitializationSystem.LoadNetworkState(networkData, …)` →
  Coroutine `LoadNetworkStateCoroutine` → ruft pro `CableSaveData` zumindest:
  * `CablePositions.instance.LoadCable(cableSaveData)` (Visualisierung)
  * Schreibt einen entsprechenden `CableInfo`-Eintrag in `cables`
  * `NetworkMap.instance.RegisterCableConnection(...)`.

## 4. Konkrete Fixes

### Fix A — Pasted Cables persistieren
Im `ApplyCables`-Pfad nach `positions.LoadCable(saveData)`:
1. **`CableInfo` aufbauen** mit `CableID = newId`, beiden `CableEndpoint`s,
   `Waypoints = worldRoute (Il2CppSystem List<Vector3>)`, `MaxSpeed = cable.Speed`.
2. **`WaypointInitializationSystem.Instance.UpdateCableInfo(newId, info)`** —
   das ist die einzige öffentliche Methode, die in `cables` schreibt; sie wird
   auch beim Load benutzt und führt intern `RegisterCableInNetworkMap` aus.
3. **`NetworkMap.instance.RegisterCableConnection(newId, startPos, endPos,
   startType, endType, startSwitchID, endSwitchID, startCustomerID, endCustomerID,
   startServerID, endServerID)`** als Backup, falls `UpdateCableInfo` die
   Network-Map-Registrierung nicht mitnimmt (`AddDevice/Connect` sind
   idempotent über die internen Dict-Lookups).
4. **`WaypointInitializationSystem.Instance.RequestRouteEvaluation()`**.

`CableLink.cableIDsOnLink/isStartOrEnd/parentX` werden weiter wie bisher gesetzt.

**Entfernen:** `PastedCableSaveRegistry`, `OnGameSavingData`, `MergePastedCableRegistryIntoCurrentSave`,
`AddCableToCurrentSave`, `EnsureNetworkSaveData`, `Upsert*SaveData`, `RegisterPastedCableSaveData`,
`LogCableSaveComparison`, `DescribeCableSaveData`, `DescribeCableRuntimePresence`,
`DescribeCurrentCablePersistenceState`, `InitializeSaveDiagnostics`, der Hook auf
`SaveSystem.onSavingData`. Die ganze Workaround-Schicht ist obsolet, sobald die
Live-Registrierung stimmt.

### Fix B — Pasted Server belegen Slots in der Floor-Map

Ursache (begründete Hypothese): `Server.Awake()` läuft beim
`Object.Instantiate(prefab, …)` *bevor* der Mod `currentRackPosition` setzen
kann; falls die Awake/`Start`-Logik (`ValidateRackPosition`,
`OnLoadingStarted`/`OnLoadingComplete`) den Server als „lost" einstuft,
respawnt er an `MainGameManager.placeToRespawnLostUsableObjects` und
`currentRackPosition` bleibt null. `BuildDeviceMap` filtert ihn dann raus →
`UsedSlots == 0` → Floor-Map zeigt das Rack als leer.

**Fix:** Prefab-deaktivieren-Pattern verwenden, damit Awake erst läuft, nachdem
der Mod alle Felder gesetzt hat:
```csharp
var wasActive = prefab.activeSelf;
try { prefab.SetActive(false); }
catch { /* nicht zwingend nötig wenn Prefab schon inaktiv */ }
var instance = Object.Instantiate(prefab, parent);     // jetzt INAKTIV → Awake wartet
// alle currentRackPosition / rackPositionUID / labelText Felder setzen
instance.SetActive(true);                               // jetzt läuft Awake
prefab.SetActive(wasActive);
```
Plus: NACH `*InsertedInRack(null)` `currentRackPosition` und `rackPositionUID`
defensiv erneut setzen, falls die Game-Methode sie überschreibt.

Plus: `instance.transform.SetParent(MainGameManager.instance.parentUsableObjects, true)`
nach Setzen der Position — exakt wie das Spiel es bei manueller Installation
macht. Vermeidet, dass die Hierarchie unter `rackPosition.transform` hängen
bleibt (was Awake-Validierungen verwirren kann).

### Fix C — Diagnose
Neue Datei `Services/DiagnosticsService.cs` + Aufruf aus `LaptopButtonMod.OnUpdate`:
* Hotkey **F8** dumpt:
  * `Object.FindObjectsOfType<CableLink>().Count(l => l.isStartOrEnd)` (Live-Kabel-Endpunkte / 2 = Kabel)
  * `WaypointInitializationSystem.Instance.GetAllCables().Count`
  * `NetworkMap.instance.cableConnections.Count`
  * Pro Rack: `BuildDeviceMap`-Geräte vs. `Object.FindObjectsOfType<Server>()` Children, plus `Rack.isPositionUsed[]`.
  * `SaveData.instance?.networkData?.cables.Count`.
* Hotkey **F7** simuliert Save→Reload-Roundtrip-Diff: vergleicht oben gegen Werte
  *vor* dem Save.

### Fix D — Verifikations-Rezept
1. Spielstart, leeres Rack auswählen.
2. Manuell 1 Server installieren, 1 Kabel verlegen → F8 baseline.
3. Save → Reload → F8 → identisch.
4. Vorlage mit 1 Server + 1 Kabel auf leeres Rack pasten → F8 (sollte Werte
   wie nach manueller Installation zeigen).
5. Save → Reload → F8. **PASS** = identisch zu Schritt 4.

Wenn Schritt 5 fehlschlägt: Logs per `[RackPlanner][CABLE-VANILLA]`-Prefix
zeigen, welcher der vier Vanilla-Calls (`UpdateCableInfo`,
`RegisterCableConnection`, `RequestRouteEvaluation`, `LoadCable`) gefehlt hat.

## 5. Was im Code bleibt / verschwindet

| Datei | Status |
|---|---|
| `Services/RackPlannerService.cs` | refaktoriert (siehe Fix A + B) |
| `Services/DiagnosticsService.cs` | NEU |
| `Program.cs` | bekommt `OnUpdate` für Hotkey |
| `Patches/*` | unverändert |
| `UI/RackPlannerScreenController.cs` | unverändert |

## 6. Referenzen (alle Pfade relativ Workspace-Root)

* [decomp_full/CableLink.cs](decomp_full/CableLink.cs) — Felder (cableIDsOnLink, isStartOrEnd, isEndPoint, switchID, typeOfLink, parentServer, parentSwitch, parentPatchPanel, isSFPPort, sfpTypeInserted, …) und Methoden (`InteractOnClick` Z.131, `SecondActionOnClick` Z.144, `InsertSFP` Z.119, `RemoveSFP` Z.125, `GetRopeAttachPoint` Z.181).
* [decomp_full/CablePositions.cs](decomp_full/CablePositions.cs) — `CreateNewCable` Z.171, `AssignNewPosition` Z.185, `LoadCable` Z.165, `GenerateFinalPath` Z.191, `RemovePosition` Z.224.
* [decomp_full/CableSaveData.cs](decomp_full/CableSaveData.cs) — Save-Struktur.
* [decomp_full/CableEndpointSaveData.cs](decomp_full/CableEndpointSaveData.cs) — Endpunkt-Struktur.
* [decomp_full/NetworkSaveData.cs](decomp_full/NetworkSaveData.cs) — Wurzel-Liste; ctor RVA 0x1C21.
* [decomp_full/NetworkMap.cs](decomp_full/NetworkMap.cs) — `RegisterCableConnection` Z.274, `RegisterServer/Switch/AddDevice` Z.149/156/208, `RemoveCableConnection` Z.286, `cableConnections` Z.110.
* [decomp_full/WaypointInitializationSystem.cs](decomp_full/WaypointInitializationSystem.cs) — `Instance` Z.262, `cables` Z.240, `UpdateCableInfo` Z.297, `LoadNetworkState` Z.303, `OnCableRemoved` Z.457, `RequestRouteEvaluation` Z.368, `CableInfo` Z.42, `CableEndpoint` Z.18.
* [decomp_full/SaveSystem.cs](decomp_full/SaveSystem.cs) — `SaveGame` Z.69, `LoadGame` Z.88, `onSavingData` Z.47, `BinaryFormatter` Z.101.
* [decomp_full/SaveData.cs](decomp_full/SaveData.cs) — `instance` Z.118, `networkData` Z.36; ctor RVA 0x12EA.
* [decomp_full/Server.cs](decomp_full/Server.cs) — `ServerInsertedInRack(ServerSaveData=null)` Z.198, `RegisterLink/UnregisterLink` Z.204/210, `cablelinks[]` Z.44, `ValidateRackPosition` Z.284.
* [decomp_full/NetworkSwitch.cs](decomp_full/NetworkSwitch.cs) — `SwitchInsertedInRack(SwitchSaveData=null)` Z.135, `cableLinkSwitchPorts[]` Z.22.
* [decomp_full/PatchPanel.cs](decomp_full/PatchPanel.cs) — `InsertedInRack(PatchPanelSaveData=null)` Z.53, `cableLinkPorts[]` Z.9, `GetPairedLink` Z.27.
* [decomp_full/SFPModule.cs](decomp_full/SFPModule.cs) — `InsertedInSFPPort` Z.146, `InsertDirectlyIntoPort` Z.160, `RemoveFromPort` Z.166.
* [decomp_full/Rack.cs](decomp_full/Rack.cs) — `positions[]` Z.93, `isPositionUsed[]` Z.97, `IsPositionAvailable/MarkPositionAsUsed/MarkPositionAsUnused` Z.139/146/152, `InitializeLoadedRack` Z.164.
* [decomp_full/RackPosition.cs](decomp_full/RackPosition.cs) — `rackPosGlobalUID` Z.147, `SetUID/GetByUID/SetUsed` Z.159/165/199, private coroutine `InsertItemInRack` Z.192.
* [decomp_full/MainGameManager.cs](decomp_full/MainGameManager.cs) — `parentUsableObjects` Z.262, `placeToRespawnLostUsableObjects` Z.298, `serverPrefabs/switchesPrefabs/patchPanelsPrefabs` Z.199/204/209.
* [decomp_full/ServerSaveData.cs](decomp_full/ServerSaveData.cs), [SwitchSaveData.cs](decomp_full/SwitchSaveData.cs), [PatchPanelSaveData.cs](decomp_full/PatchPanelSaveData.cs), [SFPSaveData.cs](decomp_full/SFPSaveData.cs).

