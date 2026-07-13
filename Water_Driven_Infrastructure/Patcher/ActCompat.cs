using System.Collections;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Single "is AdvancedCopperTools installed" detection point, computed once at
    /// LoadMainGameData postfix time and cached for the run. WDI no longer has a hard
    /// dependency on ACT (root CLAUDE.md §WDI/ACT Decoupling) — GameLoadPatch (alternate
    /// ingredient acceptance) and ActionInterceptPatch (Workshop craft output) share this
    /// one answer instead of each independently re-scanning AllData.
    /// </summary>
    public static class ActCompat
    {
        public const string CopperNailsUid = "advanced_copper_tools_copper_nails";
        public const string MetalSheetUid = "advanced_copper_tools_metal_sheet";

        public static bool IsInstalled { get; private set; }

        public static void Detect(IEnumerable allData)
        {
            IsInstalled = false;
            if (allData == null) return;

            foreach (var item in allData)
            {
                if (item == null) continue;
                if (CardUtil.GetCardUniqueId(item) == CopperNailsUid)
                {
                    IsInstalled = true;
                    return;
                }
            }
        }
    }
}
