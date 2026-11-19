using System;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Temporary combat diagnostic (2026-08-14) — player report: Town Watch fights land many
    /// "hit" log lines but never damage the character.
    ///
    /// <c>EncounterPopup.GenerateAndApplyPlayerWound</c> rolls wound severity from an
    /// Attack/Defense ratio (.decomp/EncounterPopup.cs:3220-3251,
    /// <c>EncounterEnemyDamageReport.EnemyDamage</c> / <c>.PlayerDefense</c>) that is completely
    /// independent of the clash roll that produces the "the spear connects" log line — a landed
    /// hit can still resolve to <c>WoundSeverity.NoWound</c> (the guard Encounters' own
    /// <c>UnharmedResults[0].StatChanges</c> is empty) if the ratio falls under the lowest
    /// configured band (0.7, read from the live <c>EncounterPopup.WoundSeverityMappings</c>
    /// asset). CMC's own <see cref="ArmorCardsPatch"/> registers the mod's combat armor
    /// (Bone Lamellar, Wooden Shield, quilted gear, various apparel) into
    /// <c>GameManager.ArmorCards</c> so it counts toward that ratio's defense side — plausible,
    /// but not confirmed against a live save, root cause.
    ///
    /// Subscribes to the vanilla public static event <c>EncounterPopup.OnEnemyInflictedDamage</c>
    /// (.decomp/EncounterPopup.cs:297, fired every enemy attack round at :3640) and logs the exact
    /// breakdown via vanilla's own <c>EncounterEnemyDamageReport.ReportToString()</c> — same
    /// no-Harmony, plain "+=" precedent as vanilla's <c>CombatSubObjective</c> subscribing to
    /// <c>GameManager.OnEncounter*</c>. No vanilla data is mutated. CLAUDE.md Debugging Discipline
    /// #2: diagnostics before fixes, always LogInfo so a single restart + one guard fight is
    /// enough to read real numbers instead of guessing a damage-value fix blind.
    ///
    /// Since 2026-11-12 (modwide A10) <c>Plugin.cs</c> registers this class only when the
    /// <c>EnableGuardDiagnostics</c> config entry is on (default off), because it logged one Info line
    /// per enemy attack round of every fight in the game. The line itself stays at LogInfo, so with the
    /// flag on and the game relaunched it still reaches LogOutput.log as retro
    /// <c>cmc-guard-combat-no-damage</c> expects.
    ///
    /// Demote to LogDebug (or delete this file) once the root cause is confirmed and fixed.
    /// </summary>
    internal static class GuardCombatDiagnosticPatch
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                EncounterPopup.OnEnemyInflictedDamage += LogDamage;
                Plugin.Logger.LogDebug("[GuardCombatDiag] subscribed to EncounterPopup.OnEnemyInflictedDamage.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardCombatDiag] subscribe failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void LogDamage(EncounterEnemyDamageReport report)
        {
            try
            {
                Plugin.Logger.LogInfo("[GuardCombatDiag] " + report.ReportToString().Replace("\n", " | "));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[GuardCombatDiag] log failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
