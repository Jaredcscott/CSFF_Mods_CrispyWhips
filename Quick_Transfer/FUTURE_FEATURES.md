# Quick Transfer - Future Feature Ideas

## Core Functionality Expansion

### Smart Transfer Modes
- **Smart Filter Transfer** — Transfer only items matching a criteria (type, quality, rarity)
- **Stack-aware Transfer** — Move full stacks vs individual items intelligently
- **Partial Transfer** — Transfer specific number without modifying config (hotkey combo)
- **Drag-to-Transfer** — Hold modifier and drag card for alternate transfer behavior
- **Reverse Transfer** — Pull items FROM container TO inventory

### Transfer Targeting
- **Container Hotkeys** — Default target specific containers (Chest #1, Bag #2, etc.)
- **Auto-Target Closest Container** — Transfer to nearest open container automatically
- **Blacklist/Whitelist** — Exclude certain containers from auto-transfer
- **Smart Container Selection** — Transfer to first container with space
- **Route Planning** — Transfer chains (A → B → C automatically)

## Filtering & Organization

### Item Filtering
- **Filter by Type** — Transfer only food, tools, materials, etc.
- **Filter by Quality** — Move items above/below quality threshold
- **Filter by Rarity** — Transfer rare/common items separately
- **Filter by Condition** — Low durability items to repair station
- **Filter by Source** — Items only from specific containers
- **Complex Filters** — Combine multiple filter rules

### Organizational Presets
- **Save Layouts** — Save container organization as profiles
- **Load Layouts** — Quickly reorganize containers to preset state
- **Swap Containers** — Exchange contents between two containers
- **Auto-Sort** — Organize containers by item type automatically
- **Bulk Rename** — Rename multiple containers at once

## Inventory Management

### Quick Access
- **Favorite Items** — Mark frequently-transferred items for quick access
- **Recent Items** — Quick menu for last N transferred items
- **Search & Transfer** — Find item by name, then transfer
- **Transfer History** — See what was transferred and when
- **Undo Transfer** — Revert last transfer operation

### Container Enhancements
- **Container Labeling** — Name containers for organization
- **Container Sorting** — Sort containers by contents, size, location
- **Quick Preview** — Tooltip showing container contents on hover
- **Visual Indicators** — Icon/color coding for container types
- **Storage Capacity Display** — See container fill percentage

## Optimization & Automation

### Smart Transfer Automation
- **Auto-Organize on Close** — Auto-sort when closing a container
- **Drag-to-Organize** — Drag item type to prioritize location
- **Loot Autopickup** — Automatically transfer looted items to specific containers
- **Crafting Supply Routing** — Auto-pull materials from storage to crafting station
- **Spoilage Prevention** — Rotate items before they spoil

### Batch Operations
- **Bulk Transfer All** — Transfer all items of a type to target
- **Multi-Container Transfer** — Move items between multiple containers simultaneously
- **Sync Containers** — Keep multiple containers at same item levels
- **Distribution Mode** — Spread items evenly across containers

## UI & Quality of Life

### Visual Improvements
- **Transfer Preview** — See what will transfer before confirming
- **Visual Feedback** — Animations showing item movement
- **Notification System** — Alerts for important transfers
- **Customizable HUD** — Position and scale transfer indicators
- **Dark Mode** — Eye-friendly UI theme

### Configuration Interface
- **In-Game Config Menu** — F-key to open settings without file editing
- **Preset Manager** — Create/load transfer profiles
- **Keybind Customizer** — Rebind all keys visually
- **Quick Settings** — Speed dial for most-used options
- **Config Sync** — Backup/restore settings to file

## Game Integration

### Container Types
- **Support for All Containers** — Trunks, baskets, bags, satchels, wheelbarrows, etc.
- **Furnace/Mill Integration** — Transfer items to cooking surfaces
- **Workstation Support** — Transfer materials to forges, mortars, grindstones
- **Equipment Slots** — Transfer items to equipment with Shift+Click
- **Vendor Transfer** — Transfer to NPC merchants for selling

### Action Integration (with RA)
- **Repeat Transfer Action** — Use /repeat-action to bulk-transfer repeatedly
- **Macro Support** — Chain transfers with other actions
- **Travel-to-Transfer** — Auto-transfer when arriving at location
- **Crafting Pulldown** — Auto-transfer materials during crafting

## Advanced Systems

### Inventory Management AI
- **Smart Priority Sorting** — Organize by frequency of use
- **Weight Distribution** — Spread heavy items across containers
- **Quick Access Zones** — Keep frequently used items accessible
- **Container Optimization** — Calculate best storage arrangement
- **Predictive Stocking** — Pre-move items to where you'll need them

### Multi-Container Chains
- **Supply Chain Setup** — Configure transfer routes between containers
- **Automated Logistics** — Set triggers for automatic transfers
- **Warehouse Management** — Large-scale storage organization
- **Inventory Templates** — Create standard container setups
- **Sync Across Saves** — Share organization setups with other players

## Aesthetic & Immersion

### Visual Customization
- **Container Appearance Options** — Decorative skins/styles
- **Transfer Effect Customization** — Choose visual effects
- **Sound Effects Toggle** — Optional audio feedback
- **Animated Transfers** — Items visibly move between containers
- **Particle Effects** — Magic/sparkle effects on transfers

## Performance & Optimization

### Efficiency Features
- **Batch Processing** — Transfer large quantities in one operation
- **Deferred Updates** — Combine multiple transfers into single refresh
- **Lazy Loading** — Don't load container contents until needed
- **Cache System** — Remember recently accessed containers
- **Memory Management** — Optimize for large inventories

## Integration with Other Mods

### Smart Inventory Connection
- **Container Hotkey Support** — Work with Smart_Inventory container hotkeys
- **Item Lock Respect** — Don't transfer locked items unless forced
- **Smart Equip Integration** — Transfer best items to equipment slots

### ACT/WDI/H&F Connection
- **Bulk Craft Material Transfer** — Move materials to crafting stations
- **Harvest Transfer** — Auto-move crops/forage to storage
- **Machine Output Collection** — Bulk collect factory production

### RA Connection
- **Macro Support** — Create action sequences with repeat
- **Batch Crafting** — Transfer materials and repeat crafting

## Advanced Filtering

### Complex Filter Logic
- **AND/OR Conditions** — Combine multiple filter criteria
- **Regex Pattern Matching** — Filter by item name patterns
- **Stat-Based Filtering** — Filter by armor rating, healing value, etc.
- **Durability Filtering** — Move low-durability items to repair
- **Value Filtering** — Transfer items above/below value threshold

## Status Tracking

### Analytics & Reporting
- **Transfer History Log** — Detailed log of all transfers
- **Container Audit** — See what's in all containers at once
- **Item Tracking** — Find where specific items are stored
- **Usage Statistics** — Most/least transferred items
- **Optimization Suggestions** — AI recommendations for better organization

---

## Implementation Priority

**High Priority:**
- Container hotkey targeting (fundamental QoL)
- Smart filter modes (most requested feature)
- Container labeling/organization (essential for UX)
- In-game config menu (reduces file editing)

**Medium Priority:**
- Transfer preview/confirmation
- Batch operations beyond simple stacking
- Container presets/profiles
- Integration with other mods

**Low Priority (High effort):**
- AI-based smart sorting
- Complex logistics chains
- Automated inventory management
- Performance optimization for massive inventories

## Known Challenges
- Respecting game card state (stacked vs unstacked)
- Container capacity limits
- Mod compatibility with other inventory mods
- Performance with very large transfer operations
