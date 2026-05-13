# Quick Transfer

**Version:** 1.5.5

A **Card Survival: Fantasy Forest** mod that lets you transfer multiple cards at once using a modifier key + Right-Click (configurable).

## Features

- **Bulk Transfer** — Hold modifier key (default: CTRL) and right-click a card stack to transfer multiple cards to an open container/inventory at once.
- **Adjustable Amount** — Change how many cards transfer per click:
  - **Modifier + Plus (+)** — Increase transfer amount
  - **Modifier + Minus (-)** — Decrease transfer amount
- **On-Screen Indicator** — Visual notification displays the current transfer amount when adjusted.
- **Fully Configurable** — Modifier key, transfer amount, and keybindings are all configurable via BepInEx config.

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
2. Hold **CTRL** (or your configured modifier key) and **right-click** a card stack.
3. The configured number of cards will transfer automatically.

### Adjusting Transfer Amount
- Hold modifier key and press **+ (Equals key)** to increase the amount.
- Hold modifier key and press **- (Minus key)** to decrease the amount.
- The current amount displays briefly on screen when changed.
- Default: **5 cards** per transfer (range: 1–1000).

## Configuration

After first launch, a config file is created at `BepInEx/config/crispywhips.quick_transfer.cfg`:

| Setting | Default | Description |
|---------|---------|-------------|
| Transfer Amount | 5 | Cards transferred per Modifier+Right-Click (1-1000) |
| Modifier Key | LeftControl | The key to hold while right-clicking (LeftControl, LeftShift, LeftAlt, etc.) |
| Increase Amount Key | Equals (=) | Key to increase transfer amount |
| Decrease Amount Key | Minus (-) | Key to decrease transfer amount |

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