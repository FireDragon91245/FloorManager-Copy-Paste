# Project Documentation

## 1. Projektziel

Dieses Projekt ist eine **MelonLoader-Mod für das IL2CPP-Spiel _Data Center_**, geschrieben in **C# / .NET 6**, mit **Harmony**-Patches und einer eigenen **Unity-UI** im Ingame-Laptop.

Das ursprüngliche Ziel war zuerst ein minimaler Laptop-Button-Demo-Mod. Danach wurde das Projekt schrittweise zu einem **DCIM-inspirierten Rack Planner** erweitert.

Die aktuelle Zielrichtung ist:

- im Laptop einen neuen **RACK**-Button anzeigen
- einen eigenen Planner-Screen öffnen
- Racks im Spiel erkennen und visualisieren
- ein Rack als **Quelle** wählen
- ein Rack als **Ziel** wählen
- Rack-Inhalte als **Template** speichern
- auf Basis der Quelle oder eines Templates einen **Clone-/Paste-Vorgang** vorbereiten
- neue Geräte mit **1,5x Basispreis** einsetzen, damit das Feature nicht overpowered ist

Nicht-Ziel im aktuellen Stand:

- vollständige 1:1-Reproduktion aller Laufzeitdaten
- vollständiges Kabel-/Netzwerk-Cloning
- vollständige 3D-Vorschau wie ein echter Ingame-Renderer

---

## 2. Workspace- und Projektstruktur

### Root-Ordner

`C:\Users\rivo\RiderProjects\TestDecompileCSharp`

Wichtige Root-Dateien:

- `README.md` – kompakte Projektübersicht
- `Project.md` – diese ausführliche technische Projektdokumentation
- `FixCoreModule_README.md` – Dokumentation des externen CoreModule-Fixes
- `Latest_melon_log.txt` – historischer MelonLoader-Log
- `latest_runtime_log.txt` – historischer Runtime-/Load-Fehler-Log
- `fixcoremodule_admin_output.txt` – protokollierter Testlauf des FixCoreModule-Tools

### Mod-Projekt

`C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp`

Wichtige Dateien:

- `Program.cs`
- `TestDecompileCSharp.csproj`
- `Patches\ComputerShopAwakePatch.cs`
- `Patches\ReturnMainScreenPatch.cs`
- `UI\RackPlannerScreenController.cs`
- `Services\RackPlannerService.cs`
- `Models\RackTemplateModels.cs`

---

## 3. Technischer Stack

- **C#**
- **.NET 6.0**
- **MelonLoader 0.7.2**
- **Harmony**
- **Il2CppInterop**
- **Unity UI**
- **TextMeshPro**
- **Mono.Cecil** (indirekt für das externe FixCoreModule-Werkzeug und für frühere Analysearbeiten)

Das Zielspiel läuft als:

- **IL2CPP**
- **x64**
- Unity-Version: **6000.4.2f1**

---

## 4. Build- und Referenzarchitektur

### Projektdatei

`C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\TestDecompileCSharp.csproj`

Wichtige Eigenschaften:

- `TargetFramework`: `net6.0`
- Output: Library / Mod-DLL
- Assemblierungsname: `DataCenterLaptopButtonMod`
- automatisches Kopieren nach:
  `D:\SteamLibrary\steamapps\common\Data Center\Mods`

### Referenzquellen

Es gibt zwei grundsätzlich verschiedene Referenzwelten, die **nicht gemischt werden dürfen**:

1. **MelonLoader net6 Laufzeitbibliotheken**
   - aus `C:\Users\rivo\RiderProjects\TestDecompileCSharp\MelonLoader_x64\MelonLoader\net6`

2. **generierte IL2CPP-Interop-/Game-Assemblies**
   - aus `D:\SteamLibrary\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies`

### Kritische Entdeckung

Die Unity-Referenzen müssen **konsistent** aus `Il2CppAssemblies` kommen, insbesondere:

- `UnityEngine.CoreModule.dll`
- `UnityEngine.UIModule.dll`
- `UnityEngine.UI.dll`
- `UnityEngine.TextRenderingModule.dll`
- `Unity.TextMeshPro.dll`

Sie dürfen **nicht** mit Assemblies aus `UnityDependencies` gemischt werden.

**Grund:** Das verursacht Typinkompatibilitäten bei IL2CPP-Wrappern, insbesondere bei `UnityAction` und Delegate-Konvertierungen.

---

## 5. Aktuelle Architektur des Mods

### 5.1 Einstiegspunkt

Datei: `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Program.cs`

Verantwortung:

- MelonLoader-Registrierung (`MelonInfo`)
- Zielspieldefinition (`MelonGame`)
- optionale Unity-Abhängigkeiten (`MelonOptionalDependencies`)
- Start der Harmony-Patches in `OnInitializeMelon()`

Aktueller Status:

- Version: `0.2.0`
- Mod-Name: `Data Center Laptop Button Mod`
- loggt beim Start: `Demo-Laptop-Mod initialisiert.`

### 5.2 Patch-Einstieg im Laptop

Datei: `...\Patches\ComputerShopAwakePatch.cs`

Verantwortung:

- hängt sich an `ComputerShop.Awake`
- liest `canvasComputerShop` und `mainScreen`
- speichert `LaptopButtonMod.MainScreen`
- erzeugt / verknüpft den Custom-Screen
- fügt im Hauptscreen einen neuen **RACK**-Button ein
- registriert den Button-Callback IL2CPP-kompatibel

Wichtige Schutzmaßnahmen:

- Null-Checks auf `canvasComputerShop`
- Null-Checks auf `mainScreen`
- Null-Checks auf `LayoutGroup`
- Verhindert doppelte Button-Injektion über `DemoButtonObjectName`

### 5.3 Rücksprung zum Hauptscreen

Datei: `...\Patches\ReturnMainScreenPatch.cs`

Verantwortung:

- Patch auf `ComputerShop.ButtonReturnMainScreen`
- blendet den Rack-Planner wieder aus
- sorgt für sauberen Screen-Lifecycle

### 5.4 UI-Controller

Datei: `...\UI\RackPlannerScreenController.cs`

Verantwortung:

- baut den gesamten Planner-Screen auf
- verwaltet Source-/Target-Auswahl
- rendert Floor-Layout der Racks
- rendert Rack-Vorschauen in U-Slots
- rendert Template-/Plan-Bereich
- reagiert auf Button-Klicks
- speichert Templates über den Service
- löst Clone-/Apply-Aktionen aus

Kernbestandteile:

- `EnsureScreen(...)`
- `Open(...)`
- `Close(...)`
- `RefreshData(...)`
- `RenderFloorMap()`
- `RenderRackPreview(...)`
- `RenderPlanPanel()`
- `ApplyFromSource()`
- `ApplySavedTemplate()`

### 5.5 Service-Layer

Datei: `...\Services\RackPlannerService.cs`

Verantwortung:

- liest reale Racks aus der Szene
- mappt vorhandene `UsableObject`s auf Rack-Inhalte
- baut `RackTemplate`s aus Laufzeitdaten
- lädt / speichert Templates als JSON
- baut eine Vorschau (`RackApplyPreview`)
- berechnet Konflikte, Kosten und Matches
- versucht Geräte direkt in Ziel-Racks zu instanziieren
- zieht Geld beim Spieler ab
- aktualisiert SaveData und UI-Geldanzeige

### 5.6 Datenmodelle

Datei: `...\Models\RackTemplateModels.cs`

Modelle:

- `RackDeviceKind`
  - `Server`
  - `NetworkSwitch`
  - `PatchPanel`
- `RackTemplate`
- `RackDeviceTemplate`
- `RackRuntimeInfo`
- `RackApplyPreview`
- `RackApplyResult`

---

## 6. Funktionsweise des Rack Planner

### 6.1 Rack-Erkennung

`RackPlannerService.GetRackInfos()`

- nutzt `Object.FindObjectsOfType<Rack>()`
- liest `rack.positions`
- sammelt vorhandene Geräte über `UsableObject.currentRackPosition`
- ordnet Geräte pro Rack zu
- sortiert Racks nach Weltkoordinaten (`z`, dann `x`)

### 6.2 Unterstützte Gerätetypen

Bisher unterstützt:

- `Server`
- `NetworkSwitch`
- `PatchPanel`

Aus jedem Gerät werden u. a. erfasst:

- Start-Slot (`StartIndex`)
- Größe in U (`SizeInU`)
- Prefab-ID / Variant-ID
- Basispreis
- Anzeigename
- Label
- Power-Status

### 6.3 Template-Erstellung

`CaptureRackTemplate(...)`

- erstellt ein Template aus den Geräten eines Quell-Racks
- Name automatisch aus Rack-Label + Zeitstempel oder explizitem Namen
- speichert Metadaten wie `CreatedUtc`

### 6.4 Template-Speicherung

Persistenzort:

`MelonEnvironment.UserDataDirectory\DataCenterLaptopButtonMod\rack-templates.json`

Format:

- JSON
- eingerückt (`WriteIndented = true`)

### 6.5 Vorschau / Planungsphase

`BuildPreview(...)`

Für jedes Template-Gerät wird geprüft:

- liegt das Gerät vollständig im Ziel-Rack?
- blockiert ein anderes Gerät die Slots?
- ist das blockierende Gerät eigentlich äquivalent?
- muss ein Gerät gekauft/gesetzt werden?

Ergebnis:

- `Purchases`
- `Conflicts`
- `MatchingDevices`
- `BaseCost`
- `AdjustedCost`

### 6.6 Preislogik

`PriceMultiplier = 1.5f`

Preisberechnung:

- Basispreis wird aus `shopItemSO.price` gelesen
- Clone-/Paste-Preis wird berechnet mit:
  `Mathf.CeilToInt(basePrice * 1.5f)`

### 6.7 Apply / Spawn

`ApplyTemplate(...)`

Ablauf:

1. Vorschau bauen
2. Konflikte prüfen
3. prüfen, ob Käufe überhaupt nötig sind
4. `PlayerManager.instance?.playerClass` holen
5. vorhandenes Geld gegen `AdjustedCost` prüfen
6. Geräte einzeln versuchen einzufügen
7. Geld abziehen
8. `SaveData.instance.playerData.coins` synchronisieren
9. Top-Left-Coin-UI aktualisieren

### 6.8 Rack-Spawning

`TrySpawnIntoRack(...)`

Ablauf:

- Zielslot validieren
- `rack.IsPositionAvailable(...)` prüfen
- `RackPosition` holen
- passendes Prefab aus `MainGameManager` auflösen
- Prefab instanziieren
- `UsableObject` initialisieren
- Rack-Position als benutzt markieren
- typabhängig passende Insert-Methode aufrufen:
  - `Server.ServerInsertedInRack(...)`
  - `NetworkSwitch.SwitchInsertedInRack(...)`
  - `PatchPanel.InsertedInRack(...)`

---

## 7. Reverse-Engineering-Entdeckungen

Während der Arbeit wurden gezielt Typen, Methoden und Laufzeitbeziehungen identifiziert.

### 7.1 Relevante Spieltypen

Wichtige Typen im Spiel-/Interop-Modell:

- `Rack`
- `RackPosition`
- `UsableObject`
- `Server`
- `NetworkSwitch`
- `PatchPanel`
- `ComputerShop`
- `PlayerManager`
- `MainGameManager`
- `SaveData`
- `StaticUIElements`

### 7.2 Wichtige entdeckte Methoden / Felder

Feasibility-Hinweise aus der Analyse:

- `Rack.IsPositionAvailable(...)`
- `Rack.MarkPositionAsUsed(...)`
- `RackPosition.positionIndex`
- `RackPosition.rackPosGlobalUID`
- `Server.ServerInsertedInRack(...)`
- `NetworkSwitch.SwitchInsertedInRack(...)`
- `PatchPanel.InsertedInRack(...)`
- `Player.money`
- `SaveData.instance.playerData.coins`
- `StaticUIElements.instance.topLeft_coinTXT`
- Prefab-Arrays in `MainGameManager`

### 7.3 Zentrale Erkenntnis

Der echte DCIM-ähnliche Workflow ist **teilweise möglich**, wenn man sich auf **Hardware-Platzierung** konzentriert.

Was gut machbar aussieht:

- Rack-Auswahl
- Slot-Vorschau
- Template-Speicherung
- Konflikterkennung
- Preisberechnung
- Instanziierung kompatibler Geräte in freie Slots

Was schwierig / unvollständig bleibt:

- vollständige Kabelbeziehungen
- IP-/Server-Identitäten
- IDs und Save-Integrität für alle Spezialfälle
- exakte Reproduktion aller Live-Subsysteme

---

## 8. Große Debugging- und Integrationsfunde

### 8.1 Frühere Loader-/Startup-Krise: `UnityEngine.CoreModule`

Historischer Fehler:

- `System.BadImageFormatException`
- `Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'`

Auswirkung:

- MelonLoader konnte Unity-Abhängigkeiten nicht sauber laden
- Mod-Load war vor eigentlicher Gameplay-Initialisierung gestört

Root Cause:

- generiertes `UnityEngine.CoreModule.dll` war beschädigt
- doppelte Typdefinition `<>O`
- das Problem trat im Kontext von MelonLoader 0.7.2 / IL2CPP-Assembly-Generierung auf

### 8.2 Externer Workaround: FixCoreModule

Dokumentiert in:

`C:\Users\rivo\RiderProjects\TestDecompileCSharp\FixCoreModule_README.md`

Erkenntnisse:

- das Tool scannt lokale Steam-Libraries
- sucht korrupte `UnityEngine.CoreModule.dll`
- entfernt doppelte Typdefinitionen mit `Mono.Cecil`
- schreibt Backups (`.bak`)
- macht laut README keine Netzwerkanfragen während des Fixes

Praktisches Ergebnis im Projekt:

- nach externem Ausführen des Fixes startete das Spiel sauber
- MelonLoader konnte die Mod laden
- `Support Module Loaded` erschien wieder im Laufzeitlog

### 8.3 Historischer IL2CPP-Delegate-Fehler

Früherer Fehler beim Öffnen der UI:

- `System.MissingMethodException`
- `Method not found: 'Void UnityEngine.Events.UnityAction..ctor(System.Object, IntPtr)'`

Root Cause:

- Delegate-/`UnityAction`-Bridging war im IL2CPP-Kontext falsch
- zusätzlich wurden Unity-Assemblies aus falschen Quellen gemischt

Fix:

- **keine direkten normalen Delegate-Konstruktionen** für diese UI-Callbacks
- stattdessen:
  `DelegateSupport.ConvertDelegate<UnityAction>(...)`
- alle Unity-Referenzen konsistent aus `Il2CppAssemblies`

### 8.4 Kritischer Symbol-/Assembly-Quirk

Es existieren mehrere ähnlich aussehende, aber semantisch unterschiedliche Assembly-Quellen:

- `MelonLoader\Il2CppAssemblies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator\UnityDependencies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out`

Wichtige Quirk-Regel:

- **Für die Mod-Referenzen ausschließlich `Il2CppAssemblies` verwenden**, wenn der Typ ein IL2CPP-Interop-Wrapper sein muss.

Sonst drohen:

- generische Constraint-Fehler
- Delegate-Inkompatibilitäten
- Laufzeitfehler bei `UnityAction`
- schwer erkennbare Typmismatches trotz identischer Namen

---

## 9. UI-spezifische Quirks und Symbol-/Typbesonderheiten

### 9.1 `UnityAction`

Im IL2CPP-Kontext ist `UnityAction` nicht einfach ein normaler .NET-Delegate wie in rein verwalteten Standard-Unity-Projekten.

Praktische Konsequenz:

- `button.onClick.AddListener(...)` darf nicht blind mit normalen Lambdas verkabelt werden
- stattdessen wird `DelegateSupport.ConvertDelegate<UnityAction>(...)` verwendet

### 9.2 `RectTransform`-Erstellung

Aktuelle wichtige Korrektur im Rack Planner:

Früher wurde versucht, UI-Elemente so zu erzeugen:

- normales `new GameObject(...)`
- danach `AddComponent<RectTransform>()`

Das führte im realen UI-Ladepfad zu einem `NullReferenceException` beim Aufbau des Planner-Screens.

Beobachteter Stacktrace:

- `RackPlannerScreenController.BuildScrollRegion(...)`
- `RackPlannerScreenController.BuildScreen(...)`
- `RackPlannerScreenController.EnsureScreen(...)`
- `ComputerShopAwakePatch.EnsureDemoScreen(...)`
- `ComputerShopAwakePatch.Postfix(...)`

Fix:

- UI-Objekte werden jetzt direkt mit `RectTransform` erzeugt
- Helper: `CreateUiObject(string name, Transform parent = null)`
- Umsetzung über:
  `new GameObject(name, Il2CppType.Of<RectTransform>())`

### 9.3 Screen-Lifecycle

Es gibt zwei zentrale globale Referenzen im Mod:

- `LaptopButtonMod.MainScreen`
- `LaptopButtonMod.DemoScreen`

Quirk:

- da der Custom-Screen außerhalb des normalen UI-Flows injiziert wird, muss der Rücksprung zum MainScreen explizit gehandhabt werden
- das passiert über den Patch auf `ButtonReturnMainScreen`

---

## 10. Was konkret umgesetzt wurde

### Phase 1: Basis-Mod

- MelonLoader-Mod-Projekt angelegt
- Mod sauber als DLL gebaut
- automatische DLL-Kopie in den Spielordner eingerichtet
- Laptop-Button-Demo implementiert

### Phase 2: Loader-/Interop-Stabilisierung

- Ursache des `UnityEngine.CoreModule`-Fehlers identifiziert
- `FixCoreModule` als plausiblen externen Workaround geprüft
- Laufzeit-/Loganalyse durchgeführt
- Assembly-Referenzen korrigiert
- Unity-Referenzen auf `Il2CppAssemblies` vereinheitlicht

### Phase 3: Delegate-/Callback-Fix

- `UnityAction`-Laufzeitproblem identifiziert
- IL2CPP-kompatibles Delegate-Bridging eingebaut
- funktionierende UI-Button-Callbacks hergestellt

### Phase 4: Rack Planner Architektur

- neues Model-Layer für Rack-Templates eingeführt
- Service-Layer für Rack-Erkennung und Apply-Logik gebaut
- kompletten `RackPlannerScreenController` angelegt
- Floor-Layout-Visualisierung eingebaut
- Source-/Target-Selektionslogik eingebaut
- Template-Speicherung und Template-Auswahl eingebaut
- Kosten-/Konfliktvorschau eingebaut
- direkten Spawn-/Insert-Versuch implementiert

### Phase 5: Aktueller UI-Fix

- aktuellen NullReferenceException im Planner-Screen analysiert
- Ursache auf fehlerhafte `RectTransform`-Erzeugung eingegrenzt
- UI-Erzeugung auf direkten `RectTransform`-Pfad umgestellt
- Projekt erfolgreich neu gebaut
- aktualisierte DLL in `Mods` verifiziert

---

## 11. Aktueller verifizierter Stand

Folgendes ist verifiziert:

- das Projekt baut erfolgreich
- die DLL wird automatisch nach `D:\SteamLibrary\steamapps\common\Data Center\Mods` kopiert
- die Mod wird von MelonLoader geladen
- `OnInitializeMelon()` läuft
- der Rack Planner ist architektonisch im Projekt integriert
- die aktuellen Codeänderungen für die UI-Erzeugung kompilieren erfolgreich

Zuletzt verifizierter Build:

- `dotnet build "C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\TestDecompileCSharp.csproj" -c Debug`
- Build erfolgreich

Zuletzt verifizierte Mod-DLL:

- `D:\SteamLibrary\steamapps\common\Data Center\Mods\DataCenterLaptopButtonMod.dll`
- Zeitstempel: `05.05.2026 20:16:24`

Wichtig:

- Der **Code-Fix** für den letzten Planner-UI-Fehler ist verifiziert.
- Ein **erneuter Ingame-Test nach genau diesem letzten UI-Fix** sollte separat durchgeführt werden, um den nächsten möglichen Laufzeitfehler zu sehen.

---

## 12. Bekannte Grenzen und offene Risiken

### Funktionale Grenzen

Noch nicht vollständig umgesetzt / abgesichert:

- Kabel-/Patch-Verbindungen
- IP-/Server-Identitäten
- persistente IDs aller gespawnten Geräte
- komplexe Save-/Load-Konsistenz bei Spezialfällen
- echte 3D-Vorschau

### Technische Risiken

- Game-Updates können Prefab-Arrays oder SaveData-Strukturen ändern
- MelonLoader-/Il2CppInterop-Änderungen können erneute Delegate-/Assembly-Probleme erzeugen
- bei neu generierten Assemblies kann `UnityEngine.CoreModule.dll` erneut beschädigt werden
- direkte Prefab-Instanziierung kann für bestimmte Gerätetypen zusätzliche Initialisierung brauchen, die aktuell noch nicht entdeckt wurde

---

## 13. Quirks mit Symbolen, Assemblies und Typen

Kurzfassung der wichtigsten Regeln:

1. **Nicht `UnityDependencies` und `Il2CppAssemblies` mischen**
2. **Für UI-Callbacks `DelegateSupport.ConvertDelegate<UnityAction>` verwenden**
3. **UI-Objekte direkt mit `RectTransform` erzeugen**
4. **Bei Mod-Startup-Problemen zuerst Logs prüfen, bevor Code geändert wird**
5. **`Cpp2IL\cpp2il_out` ist nicht automatisch die richtige Referenzbasis für das Mod-Projekt**
6. **identische Typnamen bedeuten im IL2CPP-Interop-Kontext nicht automatisch identische Laufzeittypen**

---

## 14. Wichtige Dateien und Verantwortlichkeiten auf einen Blick

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Program.cs`
- Mod-Metadaten
- Harmony-Patch-Initialisierung

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Patches\ComputerShopAwakePatch.cs`
- UI-Injektion in den ComputerShop
- neuer `RACK`-Button
- Screen-Erzeugung und Öffnen

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Patches\ReturnMainScreenPatch.cs`
- sauberer Rücksprung vom Planner zurück

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\UI\RackPlannerScreenController.cs`
- UI-Komposition und Bedienlogik

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Services\RackPlannerService.cs`
- Rack-/Geräteanalyse
- Template-Persistenz
- Preview-/Apply-Logik
- Preislogik
- Spawn-/Insert-Logik

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\TestDecompileCSharp\Models\RackTemplateModels.cs`
- Datenmodell für Templates, Preview und Apply-Ergebnisse

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\README.md`
- Kurzüberblick für Build und Nutzung

### `C:\Users\rivo\RiderProjects\TestDecompileCSharp\FixCoreModule_README.md`
- Dokumentation zum externen Loader-Fix

---

## 15. Empfohlene nächste Schritte

### Kurzfristig

1. Spiel starten
2. Laptop/Computer-Shop öffnen
3. prüfen, ob der `RACK`-Button sichtbar ist
4. prüfen, ob der Planner-Screen jetzt ohne NullReferenceException öffnet
5. Source-/Target-Auswahl testen
6. Test mit leerem Ziel-Rack durchführen
7. Log danach erneut auswerten

### Mittelfristig

- Spawn-/Insert-Flow für mehr Gerätetypen härten
- bessere Fehlermeldungen im Planner anzeigen
- Template-Import/Export robuster machen
- Matching-Logik erweitern (nicht nur `VariantId`, ggf. mehr Eigenschaften)
- optional Undo-/Dry-Run-Modus

### Langfristig

- Kabel-/Patching-Rekonstruktion untersuchen
- 3D-Vorschau oder zumindest richer Preview evaluieren
- Save-/Load-Konsistenz und Persistenz tiefer verifizieren
- echte DCIM-ähnliche Planungsfeatures erweitern

---

## 16. Fazit

Das Projekt ist von einem kleinen Demo-Laptop-Button zu einer **echten Rack-Planer-Mod mit eigener UI, Template-System, Preislogik und Spawn-/Insert-Versuchen** gewachsen.

Die größten technischen Hürden waren nicht die eigentliche UI, sondern:

- beschädigte generierte Unity-Assemblies
- IL2CPP-Delegate-Bridging
- symbolisch identische, aber technisch inkompatible Assembly-/Typquellen
- Unity-UI-Erzeugung im IL2CPP-Kontext

Der aktuelle Stand ist für die nächste Runde Ingame-Tests gut vorbereitet. Die Architektur ist jetzt klar getrennt in:

- **Patches** für Integration in das Spiel
- **UI Controller** für Darstellung und Bedienung
- **Service Layer** für Spiel-/Rack-Logik
- **Model Layer** für Template- und Preview-Daten

Damit ist das Projekt in einem guten Zustand für weitere Iteration.

