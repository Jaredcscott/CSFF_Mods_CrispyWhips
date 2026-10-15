using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// "Graduate" perk (cmcperkgraduate, 3 Stars): a quick study. Every Academy course takes half its sessions, so
    /// half its tuition, while the perk is held (<see cref="AcademyPatch"/> adds a second +2 hours after each
    /// study session, never past the final-exam threshold, so the exam and its course perk are still earned).
    ///
    /// <para>Vanilla balance pass (1.69.17, plan Part II row 3.3.2, owner tiles 2 and 12). Until 1.69.16 this perk
    /// granted all seven Academy graduate perks and backfilled the lectern at run start. Those perks ARE the course
    /// gates (<see cref="AcademyCourseService"/>), so the grant opened every course blueprint and the Herbalism
    /// forage bonus on day 1 for 3 Stars (vanilla's 3 Stars buy Custom Partner or Everwinter). A run that already
    /// holds the seven perks keeps them: they live in the save's InRunAddedPerks and nothing here removes them.</para>
    /// </summary>
    internal static class GraduatePerkPatch
    {
        internal const string PerkUid = "cmcperkgraduate";

        private static bool _initialized;

        /// <summary>True while the character holds the Graduate perk (a character-creation perk, so
        /// <c>CardUtil.IsPerkEquipped</c> sees it).</summary>
        internal static bool Held => CardUtil.IsPerkEquipped(PerkUid);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;
            Plugin.Logger.LogDebug("[GraduatePerkPatch] initialized (half-length courses while held).");
        }
    }
}
