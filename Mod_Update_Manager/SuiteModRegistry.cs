// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
// compiles this file so SuiteBatch has its SuiteModEntry and SuiteInstallStatus to drive (and
// PluginMetadataReader.cs beside it, for the DeclaredDependency a row carries).
using System.Collections.Generic;

namespace mod_update_manager
{
    /// <summary>
    /// Metadata for a single mod embedded in the suite bundle.
    /// </summary>
    public class SuiteModEntry
    {
        public string FolderName { get; set; }   // plugin subfolder name, e.g. "Herbs_And_Fungi"
        public string DisplayName { get; set; }   // human label in UI
        public string Description { get; set; }   // hand-typed one-liner: the details panel shows it only when the bundled mod's own ModInfo.json has no Description
        public string Category { get; set; }   // "Framework", "Content", "Utility"

        // Embedded resource key: "mod_update_manager.Resources.EmbeddedMods.<FolderName>.zip"
        public string ResourceKey => $"mod_update_manager.Resources.EmbeddedMods.{FolderName}.zip";

        // Populated at runtime by SuiteVersionReader
        public string EmbeddedVersion { get; set; } = "—";
        public string InstalledVersion { get; set; } = "—";
        public SuiteInstallStatus Status { get; set; } = SuiteInstallStatus.Unknown;

        // Read by SuiteVersionReader.RefreshAll from the ModInfo.json inside the embedded ZIP, null while
        // that could not be read. EmbeddedName is what SuiteInstalledElsewhere matches an installed copy
        // against (by Name, as the framework does); EmbeddedModInfoJson is the text the details panel
        // reads through SuiteDescription.Resolve.
        public string EmbeddedName { get; set; }
        public string EmbeddedModInfoJson { get; set; }

        // The folder of the copy SuiteInstalledElsewhere found when Status is InstalledElsewhere, else null.
        public string InstalledElsewherePath { get; set; }

        // The versions kept for this row by the opt-in "keep the previous version" option (feature 12),
        // newest first and only the ones that are whole, read by SuiteVersionReader.RefreshAll from
        // BepInEx/mum_work/kept/<FolderName>/ (SuiteInstaller.ListKeptVersions). Replaced, never changed
        // in place, so the tab can draw a copy of it while a rollback refreshes it. Empty when nothing is kept.
        public List<string> KeptVersions { get; set; } = new List<string>();

        // Read once by SuiteVersionReader.RefreshAll from the plugin DLLs inside the embedded ZIP
        // (SuiteDependencies.ReadEmbedded, through PluginMetadataReader.Read(byte[], string)): every
        // [BepInPlugin] GUID those DLLs register and every [BepInDependency] they declare. Null until
        // read, and again when the ZIP could not be read; empty when it was read and declares nothing.
        // Nothing here is typed by hand: SuiteDependencies matches a declared GUID to the row whose own
        // DLL registers it.
        public List<string> EmbeddedPluginGuids { get; set; }
        public List<DeclaredDependency> EmbeddedDependencies { get; set; }

        // Set by ModSuiteExtractor.ExtractSteps when SuiteInstaller's swap could neither finish nor be
        // undone (SuiteInstallOutcome.FailedFolderMissing): the plugin folder is gone and the
        // previous copy sits in BepInEx/mum_work/old/<FolderName> (or <FolderName>__mum_old inside
        // plugins/ when the session fell back to that layout). Every other failure leaves the installed copy
        // untouched, so it is not flagged. Deliberately separate from Status, which
        // SuiteVersionReader.RefreshAll() recomputes from ModInfo.json alone after every apply and
        // would read that state as a plain [Not Installed].
        public bool LastInstallIncomplete { get; set; }
    }

    // InstalledElsewhere is last, so the other values keep their numbers: the mod is not in
    // BepInEx/plugins/<FolderName> but a copy of it is under another folder, and Apply leaves it alone.
    public enum SuiteInstallStatus { Unknown, NotInstalled, UpToDate, OutOfDate, InstalledElsewhere }

    /// <summary>
    /// Static definition of every mod bundled inside the Mod Update Manager assembly.
    /// </summary>
    public static class SuiteModRegistry
    {
        // Order matters: Framework first, then content, then utilities.
        public static readonly List<SuiteModEntry> All = new List<SuiteModEntry>
        {
            new SuiteModEntry { FolderName="CSFF_Mod_Framework",          DisplayName="CSFF Mod Framework",          Category="Framework", Description="Asset loader and declarative database engine. Required by all content mods." },
            new SuiteModEntry { FolderName="Herbs_And_Fungi",             DisplayName="Herbs & Fungi Expansion",     Category="Content",   Description="Hemp farming, wild mushrooms, herb drying, oil press, and pickling." },
            new SuiteModEntry { FolderName="Advanced_Copper_Tools",       DisplayName="Advanced Copper Tools",       Category="Content",   Description="Copper bathtubs, wheelbarrows, and extended metalworking recipes." },
            new SuiteModEntry { FolderName="Water_Driven_Infrastructure", DisplayName="Water Driven Infrastructure", Category="Content",   Description="Water wheels, sawmills, mechanical workshops, and water-powered forges." },
            new SuiteModEntry { FolderName="Community_Mod_Chest",         DisplayName="Community Mod Chest",         Category="Content",   Description="Community-curated perks, map nodes, items, and encounters." },
            new SuiteModEntry { FolderName="Homestead_Perks",             DisplayName="Homestead Perks",             Category="Content",   Description="Forty-four character-creation perks; nineteen grant a one-time placeable structure kit, such as the Homestead, Cabin, Well, Forge and Rain Cistern kits." },
            // Pre-release (owner, 2026-10-06: Forest Lore and Far Territory, then "give al the new mods the same treatment"):
            // in the suite while still new to it; the label rides on
            // DisplayName so Category stays "Content" for SuiteDependencies.FrameworkSkew.
            new SuiteModEntry { FolderName="Forest_Lore",                 DisplayName="Forest Lore (Pre-release)",   Category="Content",   Description="Pre-release. The forest's own landmarks, ruins and hidden places, found on the map." },
            new SuiteModEntry { FolderName="Far_Territory",               DisplayName="Far Territory (Pre-release)", Category="Content",   Description="Pre-release. Lands beyond the forest, starting with the coast at the end of the river." },
            new SuiteModEntry { FolderName="Harsh_Wilds",                 DisplayName="Harsh Wilds (Pre-release)",   Category="Content",   Description="Pre-release. Optional threats for a harder forest, each behind its own config switch." },
            new SuiteModEntry { FolderName="Partner_Overhaul",            DisplayName="Partner Overhaul (Pre-release)", Category="Content", Description="Pre-release. Fixes a cluster of vanilla Partner (companion NPC) bugs." },
            new SuiteModEntry { FolderName="Sirus23_Mod_Collection",      DisplayName="Sirus23 Mod Collection (Pre-release)", Category="Content", Description="Pre-release. Animal companions and husbandry: wolf, fox and owl companions, sheep and oxen." },
            new SuiteModEntry { FolderName="Repeat_Action",               DisplayName="Repeat Action",               Category="Utility",   Description="Auto-repeat any card action with a configurable keybind." },
            new SuiteModEntry { FolderName="Quick_Transfer",              DisplayName="Quick Transfer",              Category="Utility",   Description="Hold a modifier key and click to bulk-transfer cards between containers." },
            new SuiteModEntry { FolderName="Skill_Speed_Boost",           DisplayName="Skill Training Speed Boost",  Category="Utility",   Description="Removes skill staleness decay and lets you tune skill training rates." },
            new SuiteModEntry { FolderName="Utility_Belt",                DisplayName="Utility Belt (Pre-release)",  Category="Utility",   Description="Pre-release. Small utilities in one download; this version includes Repeat Action, Quick Transfer and Skill Speed Boost." },
        };
    }
}
