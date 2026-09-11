# Quick Transfer

**Version:** 1.8.0
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.65)

A mod that lets you transfer multiple cards at once using modifier key combos + Right-Click.

## Features

- **Modifier Presets** (default, enabled) — Three quick-access combos:
  - **Shift + Right-Click** — Transfer 5 cards (Shift preset, default 5)
  - **Ctrl + Right-Click** — Transfer 10 cards (Ctrl preset, default 10)
  - **Ctrl + Shift + Right-Click** — Transfer the **entire stack** (or **half of it**, rounded up, with `Ctrl+Shift Preset Mode = Half`)
- **Adjustable Presets** — Hold the modifier then press **Plus/Minus** to tune that preset in real time:
  - **Ctrl + Plus/Minus** — Adjust the Ctrl preset amount (saves to config)
  - **Shift + Plus/Minus** — Adjust the Shift preset amount (saves to config)
- **Live Indicator** - While any transfer modifier is held, an on-screen overlay shows the effective transfer amount before you click, tagged with the combo producing it (`Quick Transfer: 10 (Ctrl)`).
- **Configurable Trigger Button** - Set `Transfer Mouse Button = Middle` to move bulk transfers off right-click, leaving modifier+right-click as the game's ordinary one-card quick-move. (Left is not offered - the game binds left-click to card inspection, not transfer.)
- **Reset Presets Hotkey** - Hold the modifier and press **Backspace** (rebindable, or `None` to disable) to snap Shift/Ctrl/custom amounts back to 5 / 10 / 5.
- **One Sound Per Batch** - The game plays a card sound for every card moved, so a bulk transfer would otherwise fire one per card on consecutive frames. Quick Transfer mutes those and plays a single sound when the batch finishes. Disable with `Consolidate Batch Sound = false`.
- **Half Preset** - Set `Ctrl+Shift Preset Mode = Half` and Ctrl+Shift moves half of the clicked stack, rounded up (8 -> 4, 7 -> 4, 1 -> 1), filling the gap between the Ctrl preset and All. The overlay reads `Quick Transfer: Half (Ctrl+Shift)` while the combo is held, because the number depends on which stack you click.
- **Drain Matching Stacks** - Set `Drain Matching Stacks = true` and a bulk transfer that empties the clicked slot keeps going with the same item from the other slots of the same container (the same open inventory, or the same board area), up to the requested count. The clicked slot always drains first. Off by default.
- **Localized Overlay** - The on-screen text follows the game's language (English and Chinese ship in `Localization/`), with built-in English used when a translation is missing. Config descriptions are BepInEx-owned and stay English.
- **Full Stack Mode** — Set `Full Stack Mode = true` to skip all count logic. Any modifier+right-click always transfers the entire stack. Count adjustment keys and preset amounts are ignored.
- **Legacy Custom Mode** — Set `Enable Modifier Presets = false` to use a single configurable modifier key with a fully custom transfer amount (original 1.5.x behavior).

## Installation

### Requirements
- [BepInEx 5.4.23.4+](https://github.com/BepInEx/BepInEx/releases) installed for Card Survival: Fantasy Forest
- CSFFModFramework (installed in `BepInEx/plugins/CSFF_Mod_Framework/`) — Quick Transfer uses its reflection API to locate the game's card-click handler and will not apply its patch without it

### Recommended
- [BepInEx.ConfigurationManager releases](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)

### Steps
1. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`.
2. Download the latest release.
3. Copy the `Quick_Transfer` folder into your `BepInEx/plugins/` directory.
4. Launch the game.

Your plugins folder should look like:
```
BepInEx/
  plugins/
    CSFF_Mod_Framework/
      CSFFModFramework.dll
    Quick_Transfer/
      Quick_Transfer.dll
      ModInfo.json
      Localization/
        SimpEn.csv
        SimpCn.csv
```

## Usage

1. Open a container, chest, or any inventory that accepts cards.
2. Hold a modifier key and **right-click** a card stack:
   - **Shift** → transfers 5 cards (adjustable)
   - **Ctrl** → transfers 10 cards (adjustable)
   - **Ctrl + Shift** → transfers the entire stack (or half of it with `Ctrl+Shift Preset Mode = Half`)

### Adjusting Preset Amounts In-Game
- Hold **Ctrl** and press **+** or **−** to change the Ctrl preset. The new value saves automatically.
- Hold **Shift** and press **+** or **−** to change the Shift preset. The new value saves automatically.
- Hold the modifier and press **Backspace** to reset every amount to its default (5 / 10 / 5).
- The on-screen indicator updates in real time while you hold the modifier, showing exactly how many cards will transfer before you click and which combo set that number.

## Configuration

After first launch, a config file is created at `BepInEx/config/crispywhips.quick_transfer.cfg`:

**Transfer Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| Transfer Amount | 5 | Cards transferred when modifier presets are disabled or a non-Ctrl/Shift modifier is used (range 1–9999) |
| Full Stack Mode | false | When true, all modifier+right-click transfers move the entire stack; count keys and presets are ignored |
| Transfer Mouse Button | Right | Mouse button that triggers a bulk transfer while a modifier is held (`Right` or `Middle`) |
| Consolidate Batch Sound | true | Mute the per-card move sound during a batch and play one sound when it finishes |
| Drain Matching Stacks | false | After the clicked slot empties, continue with the same item from the other slots of the same container, up to the requested count |

**Modifier Presets**

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Modifier Presets | true | Enable Shift/Ctrl/Ctrl+Shift preset combos |
| Shift Preset Amount | 5 | Cards transferred per Shift+Right-Click (range 1–9999) |
| Ctrl Preset Amount | 10 | Cards transferred per Ctrl+Right-Click (range 1–9999) |
| Ctrl+Shift Preset Mode | All | What Ctrl+Shift+click transfers: `All` (entire stack) or `Half` (half of the clicked stack, rounded up) |

**Keybindings**

| Setting | Default | Description |
|---------|---------|-------------|
| Modifier Key | LeftControl | Fallback modifier key (used when presets disabled) |
| Increase Amount Key | Equals (=) | Increase the active preset or custom amount |
| Decrease Amount Key | Minus (-) | Decrease the active preset or custom amount |
| Reset Presets Key | Backspace | Hold the modifier and press this to reset all amounts to 5 / 10 / 5; `None` disables it |

### In-Game Configuration (Recommended)

For easy in-game settings, install **BepInEx.ConfigurationManager**:

1. Download from [BepInEx.ConfigurationManager releases](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)
2. Extract `ConfigurationManager.dll` to your `BepInEx/plugins/` folder
3. Press **F1** in-game to open the configuration menu
4. Find **"Quick_Transfer"** in the plugin list
5. Adjust settings in real-time — changes apply immediately!

> **Tip:** ConfigurationManager works with most BepInEx mods, so it's a useful tool to have installed for managing all your mod settings from one place.

## Compatibility

- Card Survival: Fantasy Forest
- BepInEx 5.4.23.4+
- Compatible with other mods (does not modify game data, only adds input handling)

**Dependencies:** `CSFFModFramework` only (declared `SoftDependency`). Quick Transfer calls the framework's `Api.Reflect` utility to locate the game's card-click handler at load — the mod still loads without the framework present, but that lookup fails and the transfer patch is not applied. The framework also loads the overlay's `Localization/` CSVs for the active language; without it the overlay simply stays in its built-in English. No other mod depends on Quick Transfer, and it has no dependency on any content mod.

## Credits

Created by Jared (crispywhips)
Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX)