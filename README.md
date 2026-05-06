# Data Center MelonLoader Rack Planner Mod

Dieses Workspace-Projekt enthält jetzt eine MelonLoader-Mod für das IL2CPP-Spiel **Data Center**, die im Laptop einen neuen **Rack Planner**-Screen bereitstellt.

## Was enthalten ist

- `TestDecompileCSharp/TestDecompileCSharp.csproj` – Class-Library-Projekt für MelonLoader (`net6.0`)
- `TestDecompileCSharp/Program.cs` – Mod-Einstiegsklasse mit `MelonInfo`/`MelonGame`
- `TestDecompileCSharp/Patches/ComputerShopAwakePatch.cs` – injiziert einen neuen **RACK**-Button in die Laptop-Oberfläche
- `TestDecompileCSharp/Patches/ReturnMainScreenPatch.cs` – blendet den Rack-Planner beim Rücksprung korrekt aus
- `TestDecompileCSharp/UI/RackPlannerScreenController.cs` – baut den DCIM-inspirierten Floor-/Rack-Screen im Laptop auf
- `TestDecompileCSharp/Services/RackPlannerService.cs` – liest Racks, speichert Templates, berechnet Klon-Kosten und versucht Hardware direkt ins Ziel-Rack einzufügen
- `TestDecompileCSharp/Models/RackTemplateModels.cs` – Template-/Preview-Datenmodelle

## Aktuelle Features

- Floor-Layout-Übersicht aller gefundenen Racks
- Rack-Auswahl als **Quelle** oder **Ziel**
- Rack-Vorschau in U-Slots für Quelle und Ziel
- Rack als Template speichern/laden
- Clone-Vorschau mit Basispreis und **1,5x** Klonpreis
- Direktes Einfügen passender Hardware in freie Ziel-Slots

## Aktuelle Grenzen

- Fokus liegt derzeit auf **Hardware-Layout**: Server, Switches und Patch-Panels.
- Netzwerkkabel, eindeutige IDs, IP-Topologie und komplexe Laufzeitverknüpfungen werden bewusst **nicht 1:1 geklont**.
- Eine echte 3D-Vorschau ist noch nicht aktiviert; aktuell gibt es eine schnelle Slot-/Rack-Vorschau im Laptop.

## Referenzen

Das Projekt referenziert:

- lokale MelonLoader-Net6-DLLs aus `MelonLoader_x64/MelonLoader/net6`
- generierte IL2CPP-Interop-Assemblies aus
  `D:/SteamLibrary/steamapps/common/Data Center/MelonLoader/Il2CppAssemblies`

Wichtig: Die Unity-Referenzen (`UnityEngine.CoreModule`, `UnityEngine.UIModule`, `UnityEngine.TextRenderingModule`, `UnityEngine.UI`, `Unity.TextMeshPro`) werden konsistent aus `Il2CppAssemblies` bezogen und **nicht** mit `UnityDependencies` gemischt. Das vermeidet Delegate-/`UnityAction`-Inkompatibilitäten im Laufzeit-Patch.

## Bauen

Beim Build wird die erzeugte Mod-DLL automatisch nach
`D:/SteamLibrary/steamapps/common/Data Center/Mods`
kopiert.

```powershell
cd C:\Users\rivo\RiderProjects\TestDecompileCSharp

dotnet build .\TestDecompileCSharp\TestDecompileCSharp.csproj -c Debug
```

## Im Spiel testen

1. Spiel normal starten.
2. Laptop/Computer-Shop öffnen.
3. Auf dem Hauptscreen sollte ein neuer Button **RACK** erscheinen.
4. Beim Klick öffnet sich der Rack-Planner.
5. Mit **Quelle wählen** und **Ziel wählen** im Floor-Layout zwei Racks auswählen.
6. Optional mit **Quelle speichern** ein Template ablegen.
7. Mit **Quelle -> Ziel** oder **Vorlage -> Ziel** den Clone ausführen.

## Aktuell verifizierter Status

- Build erfolgreich
- DLL wird automatisch nach `D:/SteamLibrary/steamapps/common/Data Center/Mods` kopiert
- Der `UnityEngine.CoreModule`-Startfehler wurde durch den separat ausgeführten `FixCoreModule`-Patch behoben
- MelonLoader lädt `DataCenterLaptopButtonMod.dll` sauber als Mod
- `Support Module Loaded` erscheint im Laufzeitlog
- `Demo-Laptop-Mod initialisiert.` erscheint im Laufzeitlog, d. h. `OnInitializeMelon()` läuft erfolgreich
- Der neue Rack-Planner baut erfolgreich als `DataCenterLaptopButtonMod.dll`

Der nächste praktische Schritt ist jetzt der echte In-Game-Test des Laptop-Screens: prüfen, ob im Computer-Shop der **RACK**-Button sichtbar ist, ob Quelle/Ziel korrekt gewählt werden können und ob ein leeres Ziel-Rack sauber befüllt wird.

## Wichtige Hinweise

- Die Assembly-Referenzen setzen voraus, dass MelonLoader bereits mindestens einmal mit dem Spiel gelaufen ist.
- Wenn Unity-/Il2Cpp-Assemblies neu generiert werden, kann ein erneuter Build sinnvoll sein.
- `Latest_melon_log.txt` im Workspace dokumentiert, dass die IL2CPP-Interop-Erzeugung bereits erfolgreich durchgelaufen ist.
- Falls der `UnityEngine.CoreModule`-Fehler nach einem Spiel- oder Loader-Update zurückkehrt, den bekannten `FixCoreModule`-Patch erneut auf die neu erzeugten Assemblies anwenden.


