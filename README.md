# RadianceRush

Chains several Radiance mods at the **Absolute Radiance**

This mod doesn't modify any other mod or fight, it only reads them and manages them (turns them on and off
and picks their version).

## Install

1. Copy `RadianceRush.dll` and `fights.json` (from the repo root) to `Hollow Knight_Data\Managed\Mods\RadianceRush\`.
2. Install the Radiance mods you want to chain in (Mods\Rush\) and with no subfolders:
`Mods\Rush\ImmortalLight.dll`, `Mods\Rush\AuraRadiance.dll`, …
3. Keep in `Mods\Rush\` only the mods that are in your fight list, any other one stays on in every fight.

## Choosing the fights

Fights are read from `Mods\RadianceRush\fights.json`, next to the DLL. Uncomment the ones you want, in the order you want.

```jsonc
{
  "Fights": [
    "Any Radiance",
    "DoubleRadiance",
    "Bench",
    "SUNSET:hard",
    "chongming:easy",
    // "ImmortalLight",
  ]
}
```

- Each entry is a mod name as shown in the game's mod list, or its DLL file name (`"AnyRadiance (2)"`).
- `"Name:mode"` picks a version of mods that have more than one:
  - SUNSET / PURITY / RadianceBoss: `normal`, `hard`
  - chongming: `easy`, `normal`, `hard`
  - Abskoth: `1` to `4`
- `"Bench"` after a fight takes you to the pantheon rest room.

## Menu

- **Enabled**: turns the rush on or off.
- **Show counter**: shows the fight number on screen.
- **Auto continue**: after a win, goes straight to the next fight instead of back to the workshop.
- **Carry HP, soul and Baldur**: keeps them between fights. Off, every fight starts fresh (lifeblood from the bench still carries over).

