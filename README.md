# DadsBepInExModManager

DadsBepInExModManager places BepInEx configuration entries inside Valheim's native Settings window under a **Mods** tab.

## Features

- Uses Valheim's loaded button, toggle, slider, text, background, and font resources at runtime.
- Lists every loaded BepInEx plugin that exposes configuration entries.
- Supports booleans, ranged numbers, enums, acceptable-value lists, strings, numeric fields, and other TOML-convertible values.
- Scrolls large configuration files without expanding outside the Settings panel.
- Saves changed values through each owning BepInEx `ConfigFile` when **OK** is selected.
- Discards pending changes when **Back** is selected.
- Captures mouse/UI raycasts and makes `Player.TakeInput()` return false while the Mods tab is active, preventing player actions behind the manager.
- Packages no extracted Valheim UI assets; native resources are cloned from the running Settings window.

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
