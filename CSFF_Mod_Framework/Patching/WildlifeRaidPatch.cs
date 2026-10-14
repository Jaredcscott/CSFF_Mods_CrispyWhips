using System.Reflection;
using CSFFModFramework.Util;
using CSFFModFramework.Wildlife;

namespace CSFFModFramework.Patching;

/// <summary>
/// Hooks EncounterPopup.StartEncounter to roll BearRaidChance when a bear combat encounter
/// begins. The copper chest and other sealed containers (those lacking tag_NotSafeFromBears
/// and tag_NotSafeFromAnimals) are unaffected.
/// </summary>
internal static class WildlifeRaidPatch
{
    // Fallbacks for a missing or empty embedded registry (EA 0.68b values). The live lists are
    // Api.VanillaIds.BearEncounters (Encounter UniqueIDs) and Api.VanillaIds.BearAgents (NPCAgent
    // UniqueIDs), regenerated per game version by Development_Tools/Generate-VanillaIds.ps1.
    // Combat_EncounterBear is the Encounter every vanilla bear fight starts (Agent_Bear's
    // Approach button and its enraged AgentAction). Before 2.28.0 this constant and the registry
    // held the CARD Combat_EventBear_1_Explore (72c629f4...), which StartEncounter never receives,
    // so the raid never fired. Gate: Development_Tools/Tests/Framework-VanillaIdsGroupTypes.Tests.ps1.
    private const string FallbackBearEncounterUID = "4ce4b386231d17741aa540b11fd8e832";
    // Agent_Bear: the brown bear NPC agent those starts pass as _WithNPC.
    private const string FallbackBearAgentUID = "61dd2882b59388346b42628f5e76432b";

    private static bool InGroup(string uid, IReadOnlyList<string> registry, string fallback)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        if (registry.Count == 0) return uid == fallback;
        foreach (var known in registry)
            if (uid == known) return true;
        return false;
    }

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            Type encounterPopupType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { encounterPopupType = asm.GetType("EncounterPopup", false); }
                catch (Exception ex) { Log.Debug($"[WildlifeRaidPatch] EncounterPopup type lookup failed on assembly {asm.GetName().Name}: {ex.GetType().Name} {ex.Message}"); }
                if (encounterPopupType != null) break;
            }

            if (encounterPopupType == null)
            {
                Log.Debug("[WildlifeRaidPatch] EncounterPopup type not found — bear raid patch skipped.");
                return;
            }

            // StartEncounter(Encounter encounter, InGameNPC npc, bool flag)
            var startEncounter = encounterPopupType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);

            if (startEncounter == null)
            {
                Log.Debug("[WildlifeRaidPatch] StartEncounter(3-param) not found — bear raid patch skipped.");
                return;
            }

            var postfix = typeof(WildlifeRaidPatch).GetMethod(nameof(StartEncounter_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(startEncounter, postfix: new HarmonyMethod(postfix));
            Log.Debug("[WildlifeRaidPatch] bear encounter raid patch applied.");

            // The escalation bear starts with no NPC. EncounterPopup.GiveChase dereferences
            // CurrentEncounter.AssociatedNPC.NPCModel on a successful chase; the UI disables the
            // "Give chase" button for a null-NPC fight (W0-10 runtime leg D), so this only guards a
            // non-UI caller. Installed only while the escalation can run.
            if (WildlifeRaidService.Enabled && WildlifeRaidService.BearRaidEscalationDays > 0)
            {
                var giveChase = AccessTools.Method(typeof(EncounterPopup), "GiveChase", Type.EmptyTypes);
                if (giveChase == null)
                    Log.Warn("[WildlifeRaidPatch] EncounterPopup.GiveChase() not found - null-NPC chase guard not applied.");
                else
                    harmony.Patch(giveChase, prefix: new HarmonyMethod(typeof(WildlifeRaidPatch).GetMethod(
                        nameof(GiveChase_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[WildlifeRaidPatch] ApplyPatch failed: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = Encounter, __1 = InGameNPC, __2 = bool (_SkipEvent).
    // __1 is GameManager.FindNPC(EncounterNPC): every live vanilla start (Approach button, NPC
    // AgentAction, StartEncounterDutyAction, dialog, chase) passes the NPC it came from, so a
    // real bear fight arrives WITH Agent_Bear. It is null only on a save-reload resume or a drop
    // that names no EncounterNPC.
    private static void StartEncounter_Postfix(object __0, object __1)
    {
        if (!WildlifeRaidService.Enabled) return;

        // Plan feature 20: the first start after the escalation's throwaway action began IS the
        // escalation bear. It never chains a second raid through OnBearEncounter, and a start an
        // encounter guard suppressed keeps the raid-day streak instead of resetting it.
        // Only the escalation's own encounter with no NPC counts: an unrelated start that lands in
        // the same window (a wild owl's night duty) takes the ordinary path below and leaves the flag set.
        if (WildlifeRaidService.EscalationStartInFlight && __1 == null
            && (__0 as Encounter)?.UniqueID == WildlifeRaidService.EscalationEncounterUID)
        {
            WildlifeRaidService.OnEscalationStartObserved(Api.EncounterGuards.WasSuppressedThisFrame);
            return;
        }

        // Harmony runs postfixes even when a prefix skipped the original - an encounter
        // suppressed by EncounterGuards must not still trigger a bear raid.
        if (Api.EncounterGuards.WasSuppressedThisFrame) return;

        var encounter = __0 as Encounter;
        if (encounter == null) return;
        if (!InGroup(encounter.UniqueID, Api.VanillaIds.BearEncounters, FallbackBearEncounterUID)) return;

        // An NPC-started encounter counts only when that NPC is a bear agent, so a human NPC
        // (trader, partner, villager) can never trigger a raid, whatever encounter it starts.
        string agentUid = null;
        if (__1 != null)
        {
            var npc = __1 as InGameNPC;
            if (npc != null && npc.NPCModel != null) agentUid = npc.NPCModel.UniqueID;
            if (!InGroup(agentUid, Api.VanillaIds.BearAgents, FallbackBearAgentUID)) return;
        }

        // A save-reload that resumes a fight in progress calls StartEncounter again, with no NPC
        // (GameManager's HasEncounterData branch after OnGMInitialized). The raid already rolled
        // when that fight began, so a reload must not roll it again.
        var gm = MBSingleton<GameManager>.Instance;
        if (gm != null && gm.CurrentSaveData != null && gm.CurrentSaveData.HasEncounterData) return;

        Log.Debug($"[WildlifeRaidPatch] bear encounter started (npc={agentUid ?? "none"}) - rolling BearRaidChance.");
        WildlifeRaidService.OnBearEncounter();
    }

    // A null-NPC fight cannot be chased (EncounterChasingReport sets CannotEscape for it), so
    // instead of throwing on the NPCModel dereference it ends the way "Leave it be" does.
    private static bool GiveChase_Prefix(EncounterPopup __instance)
    {
        var current = __instance != null ? __instance.CurrentEncounter : null;
        if (current == null || current.AssociatedNPC != null) return true;
        Log.Debug("[WildlifeRaidPatch] GiveChase on a fight with no NPC - handled as Leave it be.");
        __instance.LeaveBe();
        return false;
    }
}
