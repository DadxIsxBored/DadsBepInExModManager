# Thunderstore release procedure

1. Run `.\build.ps1 -Package`.
2. Confirm `dist\DadsBepInExModManager-1.1.0\` contains the unpacked package.
3. Upload `dist\DadsBepInExModManager-1.1.0.zip` to Thunderstore.
4. Select the Valheim community and the Mods category.

The ZIP is validated as a flat-root package and contains no framework or game assemblies.
