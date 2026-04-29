# Water Driven Infrastructure

**Version:** 1.2.2  
**Author:** Jared (crispywhips)  
**For:** Card Survival: Fantasy Forest (EA 0.62d)

---

## Overview

Water Driven Infrastructure adds large-scale water-powered construction to CSFF. Build water wheels, sawmills, forges, grinding mills, ore sluices, and more along rivers.

## WDI Feature Infographic

```mermaid
flowchart TD
	A[Water Source] --> B[Mill Race]
	B --> C[Water Wheel]
	C --> D[Water Mill Core]

	D --> E[Water-Driven Sawmill]
	D --> F[Water-Driven Grinding Mill]
	D --> G[Water-Driven Forge]
	D --> H[Ore Sluice]

	E --> E1[Automated wood processing]
	F --> F1[Automated grinding workflows]
	G --> G1[Smelts copper at 1100+ temperature]
	G --> G2[Fires 15 clay item types]
	H --> H1[Water-based ore processing]

	I[Crafted Components] --> I1[Large and small copper gears]
	I --> I2[Copper saw blade]
	I --> I3[Portable build kits]
	I --> D

	J[Blueprint System] --> J1[11 blueprints]
	J --> J2[Injected via BlueprintTabs.json]

	K[Perk System] --> K1[3 character creation perks]

	classDef featured fill:#ffe8a3,stroke:#9a6a00,stroke-width:2px,color:#1b1b1b;
	classDef infra fill:#d6ecff,stroke:#1d5f8c,stroke-width:1px,color:#0f2a3a;
	classDef support fill:#e9f7e9,stroke:#2f7a2f,stroke-width:1px,color:#123512;

	class E,F,G,H featured;
	class A,B,C,D infra;
	class I,J,K support;
```

## Content

### Structures
- **Water Wheel** — powers other water-driven machines
- **Water Mill** — base milling structure
- **Mill Race** — channels water to power structures
- **Water-Driven Sawmill** — automated wood processing
- **Water-Driven Forge** — water-powered metalworking
- **Water-Driven Grinding Mill** — automated grinding
- **Ore Sluice** — water-based ore processing

### Crafted Components
- Copper Gear (Large & Small) — cast copper gears for machinery
- Copper Saw Blade — circular blade for the sawmill
- Various kit items for portable structure assembly

### Water-Driven Forge
- Smelts copper items (vanilla and mod) at 1100+ temperature
- Fires 15 clay item types (bowls, plates, pots, crucibles, etc.)
- Water-powered bellows for rapid heating
- Vanilla smelting recipes and mod recipes injected automatically by CSFFModFramework's SmeltingRecipeInjector

### Blueprints
11 blueprints across casting, construction, and assembly categories. Blueprints are injected into crafting tabs via `BlueprintTabs.json` (framework handles injection automatically).

### Perks
3 character creation perks for starting with pre-built infrastructure.

## Requirements

- BepInEx 5.x
- CSFFModFramework (latest)
- Card Survival: Fantasy Forest (EA 0.62d)

## Installation

1. Install BepInEx if not already installed
2. Install CSFFModFramework
3. Extract to `BepInEx/plugins/Water_Driven_Infrastructure/`
4. Launch game

## Status

v1.1.1. All systems functional: sawmill, forge (with kiln recipes), grinding mill, ore sluice, water wheel, mill race, copper components, 11 blueprints, 3 starting perks.

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** CSFFModFramework + BepInEx & Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
