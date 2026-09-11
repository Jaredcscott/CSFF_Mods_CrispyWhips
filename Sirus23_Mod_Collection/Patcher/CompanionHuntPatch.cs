using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Implements "Send on Hunt" (wolf) and "Scout Ahead" (fox) by spawning items
/// after the action's DaytimeCost timer completes.
///
/// CardsToCreate in ReceivingCardChanges is not processed by the vanilla game
/// engine (zero vanilla DAs use it). ActionRouter.AfterWrapped handles the
/// spawn after the full timed action routine runs.
/// </summary>
internal static class CompanionHuntPatch
{
    private const string WolfId = WolfTickPatch.WolfId;
    private const string FoxId = "fc_fox_companion";
    private const string OwlId = "fc_owl_companion";

    private const string RawMeatUid = "fe07d4d800bcc8646a0ff2513c78d5df";
    private const string PackBondPerkUid = "fc_perk_pack_bond";

    // Wolf hunt: 40% squirrel, 30% partridge, 20% hare, 10% nothing
    private const string DeadSquirrelUid = "e32cb25589e676c49baa6c8f54637fa3";
    private const string DeadPartridgeUid = "f72ca8f6249870d4b9306c2d779c93a9";
    private const string DeadHareUid = "81766d95f54c99242ab3d18be522db49";

    // Owl retrieve
    private const string DeadMouseUid = "cea21fa218c28ee49a49ba2ffc8eba7c";

    // Real vanilla wild-fox NPCAgents (Documentation/GameData/.../NPCAgent/Agent_Fox1.json,
    // Agent_Fox2.json) — taming targets these directly via GameSourceModify/VanillaFox_Agent1.json
    // and VanillaFox_Agent2.json instead of a separate hand-authored species, so there is only
    // ever one wild fox roaming the world (the one vanilla already spawns from its burrows).
    private const string VanillaFoxAgent1Uid = "bfcea028139f9764d868218d1bab60ad";
    private const string VanillaFoxAgent2Uid = "1f30109bedb07754fa6c49c8066a2078";

    // Vanilla shared wildlife NPCStat "AgentExists" (Documentation/GameData/.../NPCStat.json:
    // "1 if Agent is in the World. 0 if not. ... Change to 0 in Encounter when animal is killed.").
    // Setting this to 0 after a successful tame retires the specific wild fox instance the same
    // way vanilla's own kill path does, so the burrow can eventually spawn a fresh one.
    private const string AgentExistsStatUid = "16eb4865d583a1d4d82def0680f3065d";

    private static readonly (string uid, int min, int max)[] FoxScoutDrops =
    {
        (RawMeatUid, 1, 2),
    };

    public static void Register()
    {
        Plugin.Logger.LogDebug("[CompanionHunt] registering WolfHunt, FoxScout, and FlushPrey handlers...");

        ActionRouter.Register(new ActionHandler
        {
            Name = "WolfHunt",
            CardUid = WolfId,
            ActionKeyPrefix = "FC_WolfCompanion_DA2",
            ActionNamePrefix = "Send on Hunt",
            Timing = ActionTiming.AfterWrapped,
            After = ctx => {
                Plugin.Logger.LogDebug($"[CompanionHunt] WolfHunt After fired (route={ctx?.Route}, actionKey={ctx?.ActionKey}, actionName={ctx?.ActionName})");
                SpawnWolfHuntPrey();
            },
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "FoxScout",
            CardUid = FoxId,
            ActionKeyPrefix = "FC_FoxCompanion_DA2",
            ActionNamePrefix = "Scout Ahead",
            Timing = ActionTiming.AfterWrapped,
            After = _ => SpawnDrops(FoxScoutDrops),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "FlushPrey",
            CardUid = WolfId,
            ActionKeyPrefix = "FC_WolfCompanion_DA5",
            ActionNamePrefix = "Flush Prey",
            Timing = ActionTiming.AfterWrapped,
            After = _ => SpawnService.Spawn(RawMeatUid),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "OwlRetrieve",
            CardUid = OwlId,
            ActionKeyPrefix = "FC_OwlCompanion_DA2",
            ActionNamePrefix = "Retrieve Mouse",
            Timing = ActionTiming.AfterWrapped,
            After = _ => SpawnService.Spawn(DeadMouseUid),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "FoxRetrieve",
            CardUid = FoxId,
            ActionKeyPrefix = "FC_FoxCompanion_DA3",
            ActionNamePrefix = "Retrieve Partridge",
            Timing = ActionTiming.AfterWrapped,
            After = _ => SpawnService.Spawn(DeadPartridgeUid),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "BondWithWolf",
            CardUid = WolfId,
            ActionKeyPrefix = "FC_WolfCompanion_DA6",
            ActionNamePrefix = "Bond with Wolf",
            Timing = ActionTiming.AfterWrapped,
            After = _ => GrantPackBondPerk(),
        });

        // OwlTameInit removed (M6, framework 2.24.0): the owl's tame roll, retirement, and
        // companion stat init are now generated entirely from Animals/Owl.json's Interactions/
        // Companion sections and executed by the framework's TameInteractionBuilder +
        // CompanionService — see Documentation/Design/Animal_System_M0-M2_As_Built.md and
        // CSFFModFramework/Animals/CompanionService.cs. Fox tames the REAL vanilla Agent_Fox1/
        // Agent_Fox2 (GameSourceModify/VanillaFox_Agent1.json, VanillaFox_Agent2.json append the
        // "Attempt to Tame" DragAndDropAction onto them directly), so it isn't on the framework
        // companion service either — FoxTameInit stays below to do the same two post-tame fixes
        // OwlTameInit used to, now against the vanilla agent instance instead of a custom one.

        ActionRouter.Register(new ActionHandler
        {
            Name = "FoxTameInit",
            // Same CardPredicate necessity as OwlTameInit above (InGameNPC.CreateModelCard()
            // never sets CardData.UniqueID on a live NPCAgent's synthesized board card) — matching
            // by ActionNamePrefix alone would also match the Owl's identical "Attempt to Tame"
            // action name, but ActionRouter evaluates every registered handler's CardPredicate
            // independently per dispatch (CSFFModFramework/Api/ActionRouter.cs, plain List<>, not
            // a name-keyed lookup), so the two coexist safely — only the handler whose predicate
            // matches the actual card the action fired on will run.
            CardPredicate = ctx => IsVanillaWildFoxCard(ctx.Card),
            ActionNamePrefix = "Attempt to Tame",
            Timing = ActionTiming.AfterWrapped,
            After = ctx => InitFreshFoxCompanion(ctx.Card),
        });
    }

    /// <summary>True when <paramref name="card"/> is the live board card of a real vanilla
    /// Agent_Fox1/Agent_Fox2 NPC (not a mod-authored species) — found by scanning GameManager's
    /// live NPC list for one whose AssociatedCard matches, then checking its NPCAgent UID.</summary>
    private static bool IsVanillaWildFoxCard(object card)
        => card != null && TryFindNpcByCard(card, out var npc) && IsVanillaWildFoxNpc(npc);

    private static bool IsVanillaWildFoxNpc(object npc)
    {
        var model = Reflect.GetMember(npc, "NPCModel");
        var uid = model != null ? Reflect.GetMember(model, "UniqueID") as string : null;
        return uid == VanillaFoxAgent1Uid || uid == VanillaFoxAgent2Uid;
    }

    /// <summary>Scans GameManager.AllNPCs for the live InGameNPC whose synthesized board card is
    /// <paramref name="card"/>. There can be multiple live wild animals on the board at once (two
    /// separate vanilla fox burrows), so — unlike a single-species cache — every candidate must be
    /// checked; never return the first NPC found regardless of match (see CLAUDE.md's
    /// UniqueOnBoard-duplicate-instance lesson: resolvers must match the ACTUAL instance the
    /// player interacted with, not just any instance of a similar type).</summary>
    private static bool TryFindNpcByCard(object card, out object npc)
    {
        npc = null;
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return false;
        if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return false;

        foreach (var candidate in allNpcs)
        {
            if (candidate == null) continue;
            if (ReferenceEquals(Reflect.GetMember(candidate, "AssociatedCard"), card))
            {
                npc = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Two fixes for the same action, both needing to run AFTER the tame action's own
    /// coroutine (including its ProducedCards spawn) has fully completed — the same two fixes
    /// the framework's CompanionService now applies generically for the owl (M6,
    /// CSFFModFramework/Animals/CompanionService.cs). The fox tames a REAL vanilla NPCAgent
    /// (Agent_Fox1/Agent_Fox2) rather than a framework-managed Animals/*.json species, so it
    /// isn't eligible for that generic service and keeps its own copy of both fixes here:
    ///
    /// 1. A companion card whose SpoilageTime carries HasActionOnZero ("flies/runs away if
    /// starved") spawns at Hunger 0 (GiveCard 0-stat pitfall, memory
    /// reference_givecard_postfix_stat_init). CardAction.DestroysReceivingCard is
    /// unconditionally true whenever ReceivingCardChanges.ModType is Destroy (decomp
    /// CardAction.cs:315), so the vanilla zero-check fires the very next time ANYTHING spends
    /// a daytime point. Fix: init stats to full the instant the tame action itself completes.
    ///
    /// 2. Retiring the wild agent (its Exists stat -> 0) must NOT be JSON NPCStatModifications
    /// on this same DragAndDropAction — that runs INSIDE GameManager's ActionRoutine stat
    /// block, which synchronously re-enters InGameNPC.CheckForActions() (the confirmed
    /// move-before-drop race, memory reference_npcaction_move_before_drop_race), firing the
    /// exists-zero retirement action BEFORE this action's own ProducedCards ever spawns the
    /// companion, silently eating the spawn entirely. Fix: set the stat via reflection here
    /// instead — CheckForActions only picks up the change on its own next periodic pass,
    /// safely after this coroutine (and the companion spawn) has already finished.
    /// </summary>
    private static void InitFreshFoxCompanion(object tamedCard)
    {
        Plugin.Logger.LogDebug("[CompanionHunt] FoxTameInit fired — retiring wild fox and initializing companion stats.");

        var cardBaseType = CardUtil.FindGameType("InGameCardBase");
        if (cardBaseType != null)
        {
            var all = UnityEngine.Object.FindObjectsOfType(cardBaseType);
            if (all != null)
            {
                foreach (var obj in all)
                {
                    if (CardUtil.GetCardUniqueId(obj) != FoxId) continue;
                    if (WolfTickPatch.TryInitFreshSpawn(obj))
                        Plugin.Logger.LogDebug("[CompanionHunt] tamed fox initialized to full stats immediately after tame.");
                }
            }
        }

        // tamedCard is the WILD fox's own board card (ReceivingCardChanges.ModType is None on
        // the tame DragAndDropAction — vanilla never destroys/transforms it), still resolvable to
        // the same live InGameNPC the CardPredicate matched at dispatch time. No safety-net
        // ticker exists for a real vanilla agent (that's an Animals/*.json-only mechanism), so
        // failures here are logged as errors rather than "will retry next tick".
        if (tamedCard != null && TryFindNpcByCard(tamedCard, out var npc) && IsVanillaWildFoxNpc(npc))
        {
            if (!TrySetStatValue(npc, AgentExistsStatUid, 0f))
                Plugin.Logger.LogError("[CompanionHunt] TrySetStatValue(AgentExists, 0) failed on tamed vanilla fox.");
            else
                Plugin.Logger.LogDebug("[CompanionHunt] Vanilla wild fox retired (AgentExists → 0) after successful tame.");

            // Relocate immediately rather than relying on vanilla's own "If Agent Exist is 0 Go
            // to Spirit World" AgentAction, which also gates on AgentRespawnCounter being in a
            // specific range — not guaranteed true at the moment of taming.
            if (TryRelocateToSpiritWorldNow(npc))
                Plugin.Logger.LogDebug("[CompanionHunt] Vanilla wild fox relocated to Spirit World immediately — no interactivity gap.");
            else
                Plugin.Logger.LogError("[CompanionHunt] immediate relocation of tamed vanilla fox failed.");
        }
        else
            Plugin.Logger.LogError("[CompanionHunt] could not resolve tamed vanilla fox's NPC instance for retirement.");
    }

    // ── Vanilla NPC stat/relocation helpers (reflection-only — this mod's Assembly-CSharp
    // reference is the nstrip build; CLAUDE.md §Harmony Patching Pitfalls: nstrip field renames
    // cause MissingFieldException on direct typed access) ────────────────────────────────────

    private static object FindStatInstance(object npc, string statUid)
    {
        if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary dict) return null;
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key == null) continue;
            if (Reflect.GetMember(entry.Key, "UniqueID") as string == statUid)
                return entry.Value;
        }
        return null;
    }

    private static bool TrySetStatValue(object npc, string statUid, float value)
    {
        try
        {
            var statInstance = FindStatInstance(npc, statUid);
            if (statInstance == null) return false;
            var method = statInstance.GetType().GetMethod("SetStatValue",
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;
            method.Invoke(statInstance, new object[] { value });
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[CompanionHunt] TrySetStatValue failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }

    private const string SpiritWorldUid = "db69b1711fb0190419ce0c5466591772";
    private static object _spiritWorldCard;
    private static ConstructorInfo _envIdCtor;
    private static MethodInfo _moveNpcMethod;

    /// <summary>Directly relocates an NPC to the Spirit World via GameManager.MoveNPC instead of
    /// waiting for the engine's own next AgentAction evaluation pass.</summary>
    private static bool TryRelocateToSpiritWorldNow(object npc)
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;

            if (_spiritWorldCard == null || _envIdCtor == null || _moveNpcMethod == null)
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                var uidType = asm?.GetType("UniqueIDScriptable");
                var cardDataType = asm?.GetType("CardData");
                var envIdType = asm?.GetType("EnvID");
                var getFromId = uidType?.GetMethods()
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (getFromId == null || cardDataType == null || envIdType == null)
                    return false;

                _spiritWorldCard ??= getFromId.MakeGenericMethod(cardDataType).Invoke(null, new object[] { SpiritWorldUid });
                if (_spiritWorldCard == null) return false;

                _envIdCtor ??= envIdType.GetConstructor(new[] { cardDataType, typeof(bool) });
                _moveNpcMethod ??= gm.GetType().GetMethod("MoveNPC", BindingFlags.Public | BindingFlags.Instance);
                if (_envIdCtor == null || _moveNpcMethod == null) return false;
            }

            object envId = _envIdCtor.Invoke(new object[] { _spiritWorldCard, false });
            _moveNpcMethod.Invoke(gm, new object[] { npc, envId });
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[CompanionHunt] TryRelocateToSpiritWorldNow failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }

    private static void SpawnWolfHuntPrey()
    {
        int roll = UnityEngine.Random.Range(1, 11);  // 1–10 inclusive
        string uid =
            roll <= 4 ? DeadSquirrelUid :
            roll <= 7 ? DeadPartridgeUid :
            roll <= 9 ? DeadHareUid :
            null;  // roll == 10 → nothing

        Plugin.Logger.LogDebug($"[CompanionHunt] wolf hunt roll={roll} → {uid ?? "nothing"}");
        if (uid != null)
            SpawnService.Spawn(uid);
    }

    private static void SpawnDrops((string uid, int min, int max)[] drops)
    {
        foreach (var (uid, min, max) in drops)
        {
            int qty = max <= min ? min : UnityEngine.Random.Range(min, max + 1);
            for (int i = 0; i < qty; i++)
                SpawnService.Spawn(uid);
        }
    }

    private static void GrantPackBondPerk()
    {
        try
        {
            // Resolve types from Assembly-CSharp to avoid shadowing by ModCore etc.
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) { Plugin.Logger.LogError("[BondWithWolf] Assembly-CSharp not found"); return; }

            var gmType = asm.GetType("GameManager");
            var perkType = asm.GetType("CharacterPerk");
            var uidType = asm.GetType("UniqueIDScriptable");
            if (gmType == null || perkType == null || uidType == null)
            {
                Plugin.Logger.LogError("[BondWithWolf] Required game types not found");
                return;
            }

            var gmInstance = CardUtil.GetGameManagerInstance();
            if (gmInstance == null) { Plugin.Logger.LogError("[BondWithWolf] GameManager instance not found"); return; }

            // GetFromID<CharacterPerk>(PackBondPerkUid)
            var getFromId = uidType.GetMethods()
                .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
            if (getFromId == null) { Plugin.Logger.LogError("[BondWithWolf] GetFromID not found"); return; }

            var perkSO = getFromId.MakeGenericMethod(perkType).Invoke(null, new object[] { PackBondPerkUid });
            if (perkSO == null) { Plugin.Logger.LogError($"[BondWithWolf] Perk '{PackBondPerkUid}' not found in AllData"); return; }

            // Check InRunAddedPerks — skip if already granted
            var inRunField = gmType.GetField("InRunAddedPerks",
                BindingFlags.Public | BindingFlags.Instance);
            if (inRunField == null) { Plugin.Logger.LogError("[BondWithWolf] InRunAddedPerks field not found"); return; }

            var inRunList = inRunField.GetValue(gmInstance) as IList;
            if (inRunList == null) { Plugin.Logger.LogError("[BondWithWolf] InRunAddedPerks is null"); return; }

            foreach (var existing in inRunList)
                if (existing == perkSO) { Plugin.Logger.LogDebug("[BondWithWolf] Pack Bond already granted — bond is deep, nothing more to prove."); return; }

            inRunList.Add(perkSO);

            // ApplyPerk(perk, applyStartingStatMods: true, character: null)
            var applyMethod = gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "ApplyPerk");
            if (applyMethod != null)
            {
                var applyParams = applyMethod.GetParameters();
                object[] callArgs = applyParams.Length == 3
                    ? new object[] { perkSO, true, null }
                    : new object[] { perkSO };
                applyMethod.Invoke(gmInstance, callArgs);
            }

            Plugin.Logger.LogDebug("[BondWithWolf] Pack Bond perk granted — tracking sharpened.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[BondWithWolf] Error granting perk: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
