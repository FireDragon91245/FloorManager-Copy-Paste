# Floor Manager: Copy & Paste

A simple UI mod for Data Center that allows users to copy, template and paste rack layouts between different racks.

## Current Features

- See interactive map of racks in the floor layout.
- Copy / Tenlate existing rack layouts.
- Paste copied / templated layouts into empty racks.
- View / Delete saved templates.
- View Rack layout details of selected racks.

## Bugs
- UI Selection border stays on screen (ghost) or is not the correct size in the mod UI.
- Performance issues for large saves; When pasting racks; when saving the game

## Development

1. Install MelonLoader x64 into your `Data Center` game folder.
2. Launch the game once to generate `MelonLoader/Il2CppAssemblies`. If first launch crashes with duplicate `<>O`, run [`FixCoreModule`](https://github.com/V1ndicate1/FixCoreModule) against the game folder, then launch again.
3. Create `Directory.Build.props.user` and set `GameDir`; optional overrides: `Il2CppAssembliesDir`, `MelonLoaderRefDir`, `CopyModToGame`.
4. Default refs: `MelonLoaderRefDir=./MelonLoader_x64/MelonLoader/net6`, `Il2CppAssembliesDir=$(GameDir)/MelonLoader/Il2CppAssemblies`.
5. Build normally; if `CopyModToGame=true`, the mod is copied to `$(GameDir)/Mods`.

