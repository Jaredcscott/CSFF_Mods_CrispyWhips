using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// N6, repairing a relationship. A DETECTED Copper Chest theft now costs the robbed resident's Trust, and a
    /// per-resident "make amends" errand (all of it dialog JSON) wins it back over time, never past where it stood.
    /// Undetected theft costs nothing: the resident never learns of it (<see cref="CopperChestPatch"/> keeps that rule).
    ///
    /// <para>This class owns only the two things JSON cannot do: the <b>loss</b> (a caught theft lowers Trust by a fixed
    /// amount, records the debt and remembers the ceiling Trust stood at before the first unrepaid theft) and the
    /// <b>restore</b> (a 5 s poll that sees the clamped <c>AmendsPaid</c> flag, which the resident's thanks line raised,
    /// gives back a fixed chunk of Trust, capped by the debt and the ceiling, and clears the flag). A JSON delta cannot be
    /// capped at a per-save ceiling, which is why Trust is written from C#. Trust is written through
    /// <see cref="CopperChestPatch.TryWriteResidentTrust"/>, the one writer the sale bump uses.</para>
    ///
    /// <para>The errand marker (offered 1 / accepted 0 / delivered 0.5) is JSON-only: dialog and the hand-over drag write
    /// it, this class never does (root CLAUDE.md "NPC Errand Stat Design"; the same split as
    /// <see cref="CommunityServicePatch"/>). The debt, ceiling and paid flag are clamped stats, never counters.</para>
    ///
    /// <para>Every number here is a TUNABLE PLACEHOLDER (session-inferred design, not an owner decision). The Inn Keeper's
    /// are the 100-scale ones times 0.3 because his Friendship runs 0 to 30. Registered once per process (the early return
    /// on <c>_initialized</c> is load-bearing, root CLAUDE.md "Per-Run Event Registration").</para>
    /// </summary>
    internal static class AmendsPatch
    {
        internal sealed class Row
        {
            public string Name;          // CopperChestPatch.ChestConfig.Name
            public string DebtUid;       // Trust still held against the player, 0..DebtMax
            public string CeilingUid;    // Trust just before the first unrepaid theft
            public string PaidUid;       // 0..1: a restore is owed (the thanks line raises it, this class clears it)
            public string CooldownUid;   // 0..288, -1 per tick: the offer shows only at 0
            public float TheftLoss;      // Trust lost per detected theft
            public float RestorePerErrand;
            public float DebtMax;
        }

        /// <summary>TUNABLE PLACEHOLDER: cool-off after a catch, in ticks (96 ticks is one game day).</summary>
        internal const float CatchCooldownTicks = 96f;

        /// <summary>A restore smaller than this leaves the ceiling reached for the purposes of closing the debt.</summary>
        internal const float CeilingEpsilon = 0.01f;

        internal static readonly Row[] Rows =
        {
            new Row { Name = "Miller", DebtUid = "cmcStatMillerAmendsDebt", CeilingUid = "cmcStatMillerAmendsCeiling", PaidUid = "cmcStatMillerAmendsPaid", CooldownUid = "cmcStatMillerAmendsCooldown", TheftLoss = 20f, RestorePerErrand = 10f, DebtMax = 40f },
            new Row { Name = "Weaver", DebtUid = "cmcStatWeaverAmendsDebt", CeilingUid = "cmcStatWeaverAmendsCeiling", PaidUid = "cmcStatWeaverAmendsPaid", CooldownUid = "cmcStatWeaverAmendsCooldown", TheftLoss = 20f, RestorePerErrand = 10f, DebtMax = 40f },
            new Row { Name = "Apothecary", DebtUid = "cmcStatApothecaryAmendsDebt", CeilingUid = "cmcStatApothecaryAmendsCeiling", PaidUid = "cmcStatApothecaryAmendsPaid", CooldownUid = "cmcStatApothecaryAmendsCooldown", TheftLoss = 20f, RestorePerErrand = 10f, DebtMax = 40f },
            // The Inn Keeper's Friendship runs 0 to 30, so his numbers are the 100-scale ones times 0.3.
            new Row { Name = "Inn Keeper", DebtUid = "cmcStatInnAmendsDebt", CeilingUid = "cmcStatInnAmendsCeiling", PaidUid = "cmcStatInnAmendsPaid", CooldownUid = "cmcStatInnAmendsCooldown", TheftLoss = 6f, RestorePerErrand = 3f, DebtMax = 12f },
            new Row { Name = "Professor", DebtUid = "cmcStatProfAmendsDebt", CeilingUid = "cmcStatProfAmendsCeiling", PaidUid = "cmcStatProfAmendsPaid", CooldownUid = "cmcStatProfAmendsCooldown", TheftLoss = 20f, RestorePerErrand = 10f, DebtMax = 40f },
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[AmendsPatch] " + message);
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunTick, "Amends");
            Plugin.Logger.LogDebug("[AmendsPatch] initialized.");
        }

        internal static Row RowFor(string chestName)
        {
            if (string.IsNullOrEmpty(chestName)) return null;
            foreach (var row in Rows)
                if (string.Equals(row.Name, chestName, StringComparison.Ordinal)) return row;
            return null;
        }

        /// <summary>Trust a detected theft takes: the per-theft loss, but never more than the Trust there is, and never
        /// enough to push the open debt past its cap.</summary>
        internal static float ComputeLoss(float trust, float debt, float debtMax, float perTheft)
        {
            return Math.Max(0f, Math.Min(perTheft, Math.Min(trust, debtMax - debt)));
        }

        /// <summary>Trust one thanked errand gives back: one errand's worth, never more than the debt, never above the
        /// ceiling the Trust stood at before the first unrepaid theft.</summary>
        internal static float ComputeRestore(float trust, float ceiling, float debt, float perErrand)
        {
            return Math.Max(0f, Math.Min(perErrand, Math.Min(debt, Math.Max(0f, ceiling - trust))));
        }

        /// <summary>Called from the caught branch of the chest's theft roll. Never throws.</summary>
        internal static void OnTheftDetected(CopperChestPatch.ChestConfig cfg)
        {
            try
            {
                var row = RowFor(cfg?.Name);
                if (row == null) return;

                if (!CopperChestPatch.TryReadResidentTrust(cfg, out float trust))
                {
                    // Resident not moved in (or the stat is unreadable): there is no Trust to lose.
                    Plugin.Logger.LogDebug($"[AmendsPatch] The {cfg.Name}'s Trust is not readable, so a caught theft costs none.");
                    return;
                }
                float debt = HiddenStat.Get(row.DebtUid);
                if (debt < 0f)
                {
                    WarnOnce("debt-read-" + row.Name, $"{row.DebtUid} is not readable, so a caught theft from the {row.Name} costs no Trust.");
                    return;
                }

                float loss = ComputeLoss(trust, debt, row.DebtMax, row.TheftLoss);
                if (loss > 0f)
                {
                    // First theft of a new debt: remember where Trust stood. Written before the loss, so a failed
                    // write leaves Trust alone.
                    if (debt < 0.5f && !HiddenStat.Set(row.CeilingUid, trust))
                    {
                        WarnOnce("ceiling-write-" + row.Name, $"{row.CeilingUid} could not be written, so a caught theft from the {row.Name} costs no Trust.");
                        return;
                    }
                    if (!CopperChestPatch.TryWriteResidentTrust(cfg, trust - loss))
                    {
                        WarnOnce("trust-write-" + row.Name, $"The {row.Name}'s Trust could not be written, so the caught theft cost none.");
                        return;
                    }
                    if (!HiddenStat.Set(row.DebtUid, debt + loss))
                        WarnOnce("debt-write-" + row.Name, $"{row.DebtUid} could not be written after the {row.Name}'s Trust fell, so no amends errand is owed.");
                    Plugin.Logger.LogDebug($"[AmendsPatch] A caught theft cost the {row.Name} {loss:0.#} Trust ({trust:0.#} -> {trust - loss:0.#}); debt {debt + loss:0.#}.");
                }

                // No amends offer the same day the player was caught.
                if (HiddenStat.Get(row.CooldownUid) < CatchCooldownTicks) HiddenStat.Set(row.CooldownUid, CatchCooldownTicks);
            }
            catch (Exception ex)
            {
                WarnOnce("loss-exception", "OnTheftDetected failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void RunTick()
        {
            try
            {
                foreach (var row in Rows)
                {
                    float paid = HiddenStat.Get(row.PaidUid);
                    if (paid < 0.5f) continue; // -1 (unreadable) and 0 both land here
                    TryRestore(row);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("tick-exception", "RunTick failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void TryRestore(Row row)
        {
            CopperChestPatch.ChestConfig cfg = null;
            foreach (var c in CopperChestPatch.Chests)
                if (string.Equals(c.Name, row.Name, StringComparison.Ordinal)) { cfg = c; break; }
            if (cfg == null) return;

            // Unreadable now means try again next poll: the flag stays up and nothing is changed.
            if (!CopperChestPatch.TryReadResidentTrust(cfg, out float trust)) return;
            float ceiling = HiddenStat.Get(row.CeilingUid);
            float debt = HiddenStat.Get(row.DebtUid);
            if (ceiling < 0f || debt < 0f)
            {
                WarnOnce("restore-read-" + row.Name, $"{row.CeilingUid} or {row.DebtUid} is not readable, so the {row.Name}'s Trust is not restored yet.");
                return;
            }

            float restore = ComputeRestore(trust, ceiling, debt, row.RestorePerErrand);
            float newTrust = trust;
            if (restore > 0f)
            {
                // Trust FIRST; on a failed write change nothing and retry next poll.
                if (!CopperChestPatch.TryWriteResidentTrust(cfg, trust + restore))
                {
                    WarnOnce("restore-write-" + row.Name, $"The {row.Name}'s Trust could not be written, so the amends are not paid out yet.");
                    return;
                }
                newTrust = trust + restore;
            }

            float newDebt = debt - restore;
            if (ceiling - newTrust < CeilingEpsilon) newDebt = 0f; // back at the ceiling by any road: the debt is closed
            if (!HiddenStat.Set(row.DebtUid, newDebt)) return;
            if (!HiddenStat.Set(row.PaidUid, 0f)) return; // cleared last, only after the writes above succeeded
            Plugin.Logger.LogDebug($"[AmendsPatch] Amends restored {restore:0.#} of the {row.Name}'s Trust ({trust:0.#} -> {newTrust:0.#}); debt {newDebt:0.#}.");
        }
    }
}
