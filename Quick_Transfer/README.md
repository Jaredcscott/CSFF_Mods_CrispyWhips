# Quick Transfer

**Version:** 1.6.1

A **Card Survival: Fantasy Forest** mod that lets you transfer multiple cards at once using modifier key combos + Right-Click.

## Features

- **Modifier Presets** (default, enabled) — Three quick-access combos:
  - **Shift + Right-Click** — Transfer 5 cards (Shift preset, default 5)
  - **Ctrl + Right-Click** — Transfer 10 cards (Ctrl preset, default 10)
  - **Ctrl + Shift + Right-Click** — Transfer the **entire stack**
- **Adjustable Presets** — Hold the modifier then press **Plus/Minus** to tune that preset in real time:
  - **Ctrl + Plus/Minus** — Adjust the Ctrl preset amount (saves to config)
  - **Shift + Plus/Minus** — Adjust the Shift preset amount (saves to config)
- **Live Indicator** — While any transfer modifier is held, an on-screen overlay shows the effective transfer amount before you click.
- **Legacy Custom Mode** — Set `Enable Modifier Presets = false` to use a single configurable modifier key with a fully custom transfer amount (original 1.5.x behavior).

## Installation

### Requirements
- [BepInEx 5.4.23.4+](https://github.com/BepInEx/BepInEx/releases) installed for Card Survival: Fantasy Forest

### Recommended
- [BepInEx.ConfigurationManager releases](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)

### Steps
1. Download the latest release.
2. Copy the `Quick_Transfer` folder into your `BepInEx/plugins/` directory.
3. Launch the game.

Your plugins folder should look like:
```
BepInEx/
  plugins/
    Quick_Transfer/
      Quick_Transfer.dll
      ModInfo.json
```

## Usage

1. Open a container, chest, or any inventory that accepts cards.
2. Hold a modifier key and **right-click** a card stack:
   - **Shift** → transfers 5 cards (adjustable)
   - **Ctrl** → transfers 10 cards (adjustable)
   - **Ctrl + Shift** → transfers the entire stack

### Adjusting Preset Amounts In-Game
- Hold **Ctrl** and press **+** or **−** to change the Ctrl preset. The new value saves automatically.
- Hold **Shift** and press **+** or **−** to change the Shift preset. The new value saves automatically.
- The on-screen indicator updates in real time while you hold the modifier, showing exactly how many cards will transfer before you click.

## Configuration

After first launch, a config file is created at `BepInEx/config/crispywhips.quick_transfer.cfg`:

**Transfer Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| Transfer Amount | 5 | Cards transferred when modifier presets are disabled or a non-Ctrl/Shift modifier is used |

**Modifier Presets**

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Modifier Presets | true | Enable Shift/Ctrl/Ctrl+Shift preset combos |
| Shift Preset Amount | 5 | Cards transferred per Shift+Right-Click |
| Ctrl Preset Amount | 10 | Cards transferred per Ctrl+Right-Click |

**Keybindings**

| Setting | Default | Description |
|---------|---------|-------------|
| Modifier Key | LeftControl | Fallback modifier key (used when presets disabled) |
| Increase Amount Key | Equals (=) | Increase the active preset or custom amount |
| Decrease Amount Key | Minus (-) | Decrease the active preset or custom amount |

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

## Credits

Created by (CrispyWhips)
Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX)