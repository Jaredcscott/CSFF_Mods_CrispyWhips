# Skill Speed Boost v2.0 — Developer API

Complete API reference for extending or integrating with Skill Speed Boost.

---

## SkillConfigManager

Manages per-skill multipliers and difficulty profiles.

### Static Methods

#### GetSkillMultiplier
```csharp
int GetSkillMultiplier(string skillKey)
```
Get XP multiplier for a specific skill.

**Returns:** int (0-10)
- 0 = skill disabled
- 1 = no multiplier (default)
- 2-10 = multiplier strength

**Example:**
```csharp
int smithingMultiplier = SkillConfigManager.GetSkillMultiplier("Smithing");
```

---

#### GetSkillUseStaleness
```csharp
bool GetSkillUseStaleness(string skillKey)
```
Check if a skill has staleness enabled.

**Returns:** bool (true = staleness enabled)

---

#### GetSkillStalenessMultiplier
```csharp
float GetSkillStalenessMultiplier(string skillKey)
```
Get staleness decay rate multiplier for a skill.

**Returns:** float (0.1-5.0)
- < 1.0 = slower decay
- > 1.0 = faster decay
- Default = 1.0

---

#### GetActiveProfile
```csharp
string GetActiveProfile()
```
Get the currently active difficulty profile.

**Returns:** string (profile name: "Balanced", "Casual", "Hardcore", etc.)

---

#### ApplyProfileSettings
```csharp
void ApplyProfileSettings(string profileName)
```
Apply settings from a named profile.

**Parameters:**
- `profileName` (string) — Profile name from `Profiles.All`

---

#### RegisterDynamicSkill
```csharp
void RegisterDynamicSkill(ConfigFile config, string skillName)
```
Register a new skill not in the core list (for mod additions).

**Parameters:**
- `config` (ConfigFile) — BepInEx ConfigFile
- `skillName` (string) — New skill name

---

#### GetAllSkillMultipliers
```csharp
IEnumerable<(string skillName, int multiplier)> GetAllSkillMultipliers()
```
Get all registered skills with non-zero multipliers.

**Returns:** Enumerable of (skillName, multiplier) tuples

---

### Profiles Class

Built-in difficulty profiles.

```csharp
public static class Profiles
{
    public const string VanillaPlus = "VanillaPlus";   // 2x XP + staleness
    public const string Casual = "Casual";             // 3x XP, no staleness
    public const string Hardcore = "Hardcore";         // 1x XP + staleness
    public const string Grinder = "Grinder";           // 10x XP, no staleness
    public const string Balanced = "Balanced";         // 2x XP + staleness
    public const string Legacy = "Legacy";             // 1x XP + staleness

    public static readonly string[] All = { ... };

    public static (int expMult, bool staleness)
        GetProfileSettings(string profile);
}
```

---

## Integration Examples

### Example 1: Reading a Skill Multiplier

```csharp
public int GetEffectiveMultiplier(string skillName)
{
    return SkillConfigManager.GetSkillMultiplier(skillName);
}
```

### Example 2: Dynamic Profile Switching

```csharp
public void SetPlaystyle(string playStyle)
{
    switch (playStyle.ToLower())
    {
        case "casual":
            SkillConfigManager.ApplyProfileSettings("Casual");
            break;
        case "hardcore":
            SkillConfigManager.ApplyProfileSettings("Hardcore");
            break;
        default:
            SkillConfigManager.ApplyProfileSettings("Balanced");
            break;
    }
}
```

---

## Configuration Access

Access configuration programmatically:

```csharp
// Via Plugin class
int globalMult = Plugin.SkillExpMultiplier;
bool stalenessEnabled = Plugin.EnableSkillStaleness;
bool perSkillEnabled = Plugin.EnablePerSkillMultipliers;
```

---

## Thread Safety

All static classes are thread-safe for reads. Avoid concurrent config writes.

```csharp
// Safe (read-only)
int mult = SkillConfigManager.GetSkillMultiplier("Smithing");

// Safe (infrequent write)
SkillConfigManager.RegisterDynamicSkill(config, "NewSkill");
```

---

## Performance Notes

- **Dictionary lookups:** O(1), negligible cost
- **Config reads:** Cached at startup
- **No GC pressure:** Reuses collections

---

## Extending the Mod

### Adding a Custom Skill

```csharp
// In your mod's OnGameLoad
SkillConfigManager.RegisterDynamicSkill(Plugin.Instance.Config, "MyCustomSkill");
```

### Creating a Custom Profile

```csharp
// Modify SkillConfigManager.Profiles to add a new profile
public const string MyProfile = "MyProfile";

// Extend GetProfileSettings() case statement
case MyProfile:
    return (expMult: 4, staleness: true);
```

---

## Version Compatibility

This API is stable for v2.0.0+. Minor versions (2.x.x) maintain API compatibility.

Breaking changes only in major versions (3.0.0+).

---

## Planned API (not yet shipped)

A `SkillSynergies` class was designed for v2.0 but never wired to a runtime XP hook. Its API surface is preserved in `Documentation/Ideas/SkillSpeedBoost/SKILL_SYNERGIES.md` for v2.1 implementation. Do not depend on it in current mods — the class is not shipped.
