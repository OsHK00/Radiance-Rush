# RadianceRush

Chains several Radiance mods at the **Absolute Radiance**


## Install

1. Copy `RadianceRush.dll` and `fights.json` (from the repo root) to `Hollow Knight_Data\Managed\Mods\RadianceRush\`.
2. Install the Radiance mods you want to chain in (Mods\Rush\) and with no subfolders:
`Mods\Rush\ImmortalLight.dll`, `Mods\Rush\AuraRadiance.dll`, …

## Choosing the fights

Fights are read from `Mods\RadianceRush\fights.json`, next to the DLL.

```jsonc
{
  "StartIndex": 0,
  "Fights": [
    "AnyRadiance",                
    // "Omega Radiance",
    "DoubleRadiance",
    "SupernovaRadiance",
    // "ImmortalLight",
  ]
}
```

- Each entry is a mod name exactly as shown in the game's mod list


## Supported mods

Rn only mods that can be toggled in the mod list menu (`ITogglableMod`). RadianceRush switches them with the
API's own `ModLoader.LoadMod` / `UnloadMod`. I will try to include others later on

Only one dll per internal assembly name can load. The AnyRadiance variants (Any Radiance (1.0), Any Radiance(2.0),
Omega Radiance) share one name, and so do Dumb / Ultimatum / Atomic Radiance.

idk if I can fix this without modifying the code of the other mods, even though that would be a simple solution so...
