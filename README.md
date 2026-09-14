# DadsBepInExModManager

DadsBepInExModManager opens a standalone BepInEx configuration manager with **F1**.

## Features

- Lists every loaded BepInEx plugin that exposes configuration entries.
- Supports booleans, ranged numbers, enums, acceptable-value lists, strings, numeric fields, and other TOML-convertible values.
- Includes mod and setting search, configuration sections, per-setting reset controls, reload, save, and cancel.
- Pauses Valheim automatically while open and restores the prior pause and time-scale state when closed.
- Uses a top-order full-screen Unity raycast blocker so clicks cannot reach pause-menu, inventory, or game controls behind the manager.
- Clears native UI selection and blocks player, inventory, and pause-menu input while open and through the closing frame.
- Saves through each owning BepInEx `ConfigFile` with **Save & Close**. **Cancel** restores the values present when the manager opened.

## Requirements

- Valheim 1.0.12
- BepInExPack Valheim 5.4.2350

## Build

```powershell
.\build.ps1 -Package
```

To copy the packaged build into the Thunderstore `Default` profile:

```powershell
.\build.ps1 -Package -Install
```

The script creates both an unpacked release folder and a flat-root Thunderstore ZIP in `dist/`. Existing distribution and installed builds are moved into `Archive/` before replacement.
