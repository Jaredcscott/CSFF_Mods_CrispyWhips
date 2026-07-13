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
        public string Description { get; set; }   // one-line description shown in details panel
        public string Category { get; set; }   // "Framework", "Content", "Utility"

        // Embedded resource key: "mod_update_manager.Resources.EmbeddedMods.<FolderName>.zip"
        public string ResourceKey => $"mod_update_manager.Resources.EmbeddedMods.{FolderName}.zip";

        // Populated at runtime by SuiteVersionReader
        public string EmbeddedVersion { get; set; } = "—";
        public string InstalledVersion { get; set; } = "—";
        public SuiteInstallStatus Status { get; set; } = SuiteInstallStatus.Unknown;
    }

    public enum SuiteInstallStatus { Unknown, NotInstalled, UpToDate, OutOfDate }

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
            new SuiteModEntry { FolderName="Repeat_Action",               DisplayName="Repeat Action",               Category="Utility",   Description="Auto-repeat any card action with a configurable keybind." },
            new SuiteModEntry { FolderName="Quick_Transfer",              DisplayName="Quick Transfer",              Category="Utility",   Description="Hold a modifier key and click to bulk-transfer cards between containers." },
            new SuiteModEntry { FolderName="Skill_Speed_Boost",           DisplayName="Skill Training Speed Boost",  Category="Utility",   Description="Removes skill staleness decay and lets you tune skill training rates." },
        };
    }
}
