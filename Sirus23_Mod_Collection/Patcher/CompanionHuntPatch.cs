using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Implements "Send on Hunt" (wolf) and the four fetches (Flush Prey, Scout Ahead, Retrieve
/// Partridge, Retrieve Mouse) by spawning items after the action's DaytimeCost timer completes.
/// Every one of them is a chance roll (S23-B2).
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
    private const string HawkId = WolfTickPatch.HawkId;

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
    // The lone wild wolf (NOT Agent_WolfPack or Agent_PrimevalWolf, and never the NOT_USED_ twins).
    private const string VanillaWolfAgentUid = "0cb137c5a8daaed47ba291779d8146f8";
    // The wolf pack (one agent standing for up to four wolves): taming it gives a PUP, not the adult.
    private const string VanillaWolfPackAgentUid = "c3ac5867e8021ce43a5094a2bb469935";
    private const string WolfPupId = "fc_wolf_pup";

    // Vanilla shared wildlife NPCStat "AgentExists" (Documentation/GameData/.../NPCStat.json:
    // "1 if Agent is in the World. 0 if not. ... Change to 0 in Encounter when animal is killed.").
    // Setting this to 0 after a successful tame retires the specific wild fox instance the same
    // way vanilla's own kill path does, so the burrow can eventually spawn a fresh one.
    private const string AgentExistsStatUid = "16eb4865d583a1d4d82def0680f3065d";

    // S23-B2 (vanilla balance pass, 1.22.6): the four fetches were guaranteed food on demand. Each is now a
    // chance roll; the DA itself (JSON) costs the companion Hunger 48 and Morale 30 and arms a one-day
    // lockout stat (sh_stat_<companion>_fetch_lockout) that its RequiredStatValues checks, so a fetch is
    // at most once a day and can come back empty. Percent chances, rolled 1-100.
    private const int FlushPreyChance = 30;
    private const int FoxScoutChance = 40;
    private const int FoxRetrieveChance = 35;
    private const int OwlRetrieveChance = 50;

    // A fetched MeatRaw used to spawn at the card's defaults, Quality 0 and Meat Type 0 (no such cut),
    // the "spawned at default stats" defect in CLAUDE.md. Vanilla MeatRaw drops carry Quality 96; Meat
    // Type 3 is the plain "Meat" cut. SpawnService.Spawn(uid, stats) applies them (framework 2.28.0).
    private static readonly System.Collections.Generic.Dictionary<string, float> FetchedMeatStats =
        new(StringComparer.Ordinal)
        {
            ["SpecialDurability2"] = 96f,
            ["SpecialDurability3"] = 3f,
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
            After = _ => RollFetch("fox scout", FoxScoutChance, RawMeatUid, FetchedMeatStats),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "FlushPrey",
            CardUid = WolfId,
            ActionKeyPrefix = "FC_WolfCompanion_DA5",
            ActionNamePrefix = "Flush Prey",
            Timing = ActionTiming.AfterWrapped,
            After = _ => RollFetch("flush prey", FlushPreyChance, RawMeatUid, FetchedMeatStats),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "OwlRetrieve",
            CardUid = OwlId,
            ActionKeyPrefix = "FC_OwlCompanion_DA2",
            ActionNamePrefix = "Retrieve Mouse",
            Timing = ActionTiming.AfterWrapped,
            After = _ => RollFetch("owl retrieve", OwlRetrieveChance, DeadMouseUid, null),
        });

        // Registered here, once per process (RegisterActionHandlers runs once from Plugin), like the rest.
        ActionRouter.Register(new ActionHandler
        {
            Name = "HawkHunt",
            CardUid = HawkId,
            ActionKeyPrefix = "FC_HawkCompanion_DA2",
            ActionNamePrefix = "Send Hawk Hunting",
            Timing = ActionTiming.AfterWrapped,
            After = _ => SpawnHawkHuntPrey(),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "FoxRetrieve",
            CardUid = FoxId,
            ActionKeyPrefix = "FC_FoxCompanion_DA3",
            ActionNamePrefix = "Retrieve Partridge",
            Timing = ActionTiming.AfterWrapped,
            After = _ => RollFetch("fox retrieve", FoxRetrieveChance, DeadPartridgeUid, null),
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
            Before = ctx => BeginTame(ctx, FoxId, "fox"),
            After = ctx => InitFreshTamedCompanion(ctx.Card, FoxId, IsVanillaWildFoxNpc, "fox"),
        });

        // Registered here, once per process (RegisterActionHandlers runs once from Plugin), so a
        // save reload cannot stack a second copy.
        ActionRouter.Register(new ActionHandler
        {
            Name = "WolfTameInit",
            CardPredicate = ctx => IsVanillaWildWolfCard(ctx.Card),
            ActionNamePrefix = "Attempt to Tame",
            Timing = ActionTiming.AfterWrapped,
            Before = ctx => BeginTame(ctx, WolfId, "wolf"),
            After = ctx => InitFreshTamedCompanion(ctx.Card, WolfId, IsVanillaWildWolfNpc, "wolf"),
        });

        ActionRouter.Register(new ActionHandler
        {
            Name = "WolfPackTameInit",
            CardPredicate = ctx => IsVanillaWildWolfPackCard(ctx.Card),
            ActionNamePrefix = "Attempt to Tame",
            Timing = ActionTiming.AfterWrapped,
            Before = ctx => BeginTame(ctx, WolfPupId, "wolf pack pup"),
            After = ctx => InitFreshTamedCompanion(ctx.Card, WolfPupId, IsVanillaWildWolfPackNpc, "wolf pack pup"),
        });

        SpawnService.CardSpawned += OnCardSpawned;
    }

    // ── The tame's own drop, captured so it can be brought to the player's board ──────────────
    //
    // The tame DragAndDropAction costs one daytime point, and GameManager.ActionRoutineSteps spends
    // it (SpendDaytimePoints, which ticks every agent) BEFORE ProduceCards runs. A wild agent on a
    // duty takes that tick's step: measured 2026-10-10 on rig 4 (npc/duty-trace), the lone wolf
    // picked AgentDuty_Wolf_7DaysCave step "WolfMoveToCave" and walked one environment on the very
    // next tick, and GameManager.MoveNPC moves the agent's board card with it (SetNewEnvironment).
    // ProduceCards spawns into _FromCard.CardEnvironment, so the companion was created in the
    // environment the animal had just walked into, InBackground, off the player's board. The
    // wolf, the wolf pack and the fox all did it. Vanilla never notices: no vanilla tame exists.
    // Before arms a capture of this action's own spawns (SpawnService.CardSpawned, vanilla
    // OnCardSpawned), After re-homes any that landed elsewhere (RehomeIfOffBoard).
    //
    // Why not the framework's ActionContext.Produced (audit row R7, 2026-10-12): it collects from the
    // same event over the same window, but it exists only from framework 2.28.0, and this mod
    // supports 2.14.1 and later (README "Requires"). Reading it from the After handler would throw
    // MissingMethodException when that handler is compiled on an older framework, which would lose the
    // stat init and the wild animal's retirement along with the re-home. SpawnService.CardSpawned
    // dates from 2.6.0. If an After handler is ever skipped (the action did not complete cleanly), the
    // latch stays armed until the next BeginTame clears it; nothing reads _captured in between.

    private static string _captureUid;
    private static readonly System.Collections.Generic.List<object> _captured = new();

    private static string EnvText(object env)
        => env == null ? "null" : $"'{CardUtil.GetMemberValue(env, "SimpleEnvName")}' MatchesPlayerEnv={CardUtil.GetMemberValue(env, "MatchesPlayerEnv")}";

    private static bool BeginTame(ActionContext ctx, string companionUid, string label)
    {
        _captured.Clear();
        _captureUid = companionUid;
        Plugin.Logger.LogDebug($"[CompanionHunt] {label} tame BEFORE: source card env {EnvText(CardUtil.GetMemberValue(ctx.Card, "CardEnvironment"))}.");
        return false;
    }

    private static void OnCardSpawned(object card, string uid)
    {
        if (_captureUid == null || uid != _captureUid || card == null) return;
        _captured.Add(card);
    }

    private static bool IsInPlayerEnv(object card)
        => CardUtil.GetMemberValue(CardUtil.GetMemberValue(card, "CardEnvironment"), "MatchesPlayerEnv") is true;

    /// <summary>Brings a freshly tamed card that landed off the player's board back onto it: its
    /// current stat values (already initialized) are read, the off-board card is removed through
    /// the game's own RemoveCard, and the same card is spawned on the current board with those
    /// values (SpawnService.Spawn with stat overrides, vanilla's drop path). A card already on the
    /// player's board is left alone. Returns true when a re-home was started.</summary>
    private static bool RehomeIfOffBoard(object card, string companionUid, string label)
    {
        if (IsInPlayerEnv(card)) return false;

        var stats = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var name in new[] { "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
                     "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4" })
        {
            float max = CardUtil.GetDurabilityMax(card, name);
            float cur = CardUtil.GetDurability(card, name);
            if (float.IsNaN(max) || max <= 0f || float.IsNaN(cur)) continue;
            stats[name] = cur;
        }

        string from = EnvText(CardUtil.GetMemberValue(card, "CardEnvironment"));
        if (!CardUtil.TryRemoveCard(card))
        {
            Plugin.Logger.LogError($"[CompanionHunt] tamed {label} ({companionUid}) landed in env {from}, off the player's board, and could not be removed there; it stays where it is.");
            return false;
        }
        SpawnService.Spawn(companionUid, stats.Count > 0 ? stats : null);
        Plugin.Logger.LogInfo($"[CompanionHunt] tamed {label} ({companionUid}) landed in env {from} (the animal walked on during the tame); moved to the player's board with {stats.Count} stat value(s) kept.");
        return true;
    }

    private static bool IsVanillaWildWolfPackCard(object card)
        => card != null && TryFindNpcByCard(card, out var npc) && IsVanillaWildWolfPackNpc(npc);

    private static bool IsVanillaWildWolfPackNpc(object npc)
    {
        var model = Reflect.GetMember(npc, "NPCModel");
        return model != null && Reflect.GetMember(model, "UniqueID") as string == VanillaWolfPackAgentUid;
    }

    private static bool IsVanillaWildWolfCard(object card)
        => card != null && TryFindNpcByCard(card, out var npc) && IsVanillaWildWolfNpc(npc);

    private static bool IsVanillaWildWolfNpc(object npc)
    {
        var model = Reflect.GetMember(npc, "NPCModel");
        return model != null && Reflect.GetMember(model, "UniqueID") as string == VanillaWolfAgentUid;
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
    // One cause (a game update renaming GameManager.AllNPCs), so a single dedicated latch rather
    // than a shared bool that could silence an unrelated cause (CLAUDE.md Silent Catch Blocks, G5).
    private static bool _warnedAllNpcsMissing;

    private static bool TryFindNpcByCard(object card, out object npc)
    {
        npc = null;
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return false;   // evaluated outside gameplay: benign negative, stays silent
        if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
        {
            // Reflect.GetMember returns null (never throws) on a missing member, so without this
            // line a renamed AllNPCs would make every tame CardPredicate silently return false and
            // kill the wild-tame feature with zero log output (the retirement error at line ~428 is
            // never reached, because the handler never dispatches).
            if (!_warnedAllNpcsMissing)
            {
                _warnedAllNpcsMissing = true;
                Plugin.Logger.LogWarning("[CompanionHunt] GameManager.AllNPCs could not be read as an IEnumerable; wild-animal taming (fox/wolf/wolf pack) will no longer detect its target. Logged once.");
            }
            return false;
        }

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
    private static void InitFreshTamedCompanion(object tamedCard, string companionUid,
        Func<object, bool> isWildNpc, string label)
    {
        Plugin.Logger.LogDebug($"[CompanionHunt] {label} tame fired - retiring wild {label} and initializing companion stats.");

        // This action's own drop, captured between Before and now. Taken and disarmed first, so a
        // re-home spawn below is never captured.
        var produced = new System.Collections.Generic.List<object>(_captured);
        _captured.Clear();
        _captureUid = null;

        int seen = 0;
        foreach (var obj in produced)
        {
            if (CardUtil.GetMemberValue(obj, "Destroyed") is true) continue;
            seen++;
            Plugin.Logger.LogDebug($"[CompanionHunt] tamed {label} card {companionUid} landed in env " +
                $"{EnvText(CardUtil.GetMemberValue(obj, "CardEnvironment"))}, InBackground={CardUtil.GetMemberValue(obj, "InBackground")}.");
            if (WolfTickPatch.TryInitFreshSpawn(obj) || TryInitFreshPup(obj))
                Plugin.Logger.LogDebug($"[CompanionHunt] tamed {label} initialized to full stats immediately after tame.");
            RehomeIfOffBoard(obj, companionUid, label);
        }

        // No capture (the spawn event did not reach us): fall back to a scan, which can only
        // initialize stats. It cannot tell this tame's card from a companion the player left in
        // another environment, so it never re-homes. FindObjectsOfTypeAll because an off-board card
        // is inactive and FindObjectsOfType skips it; it also returns pooled and prefab cards, which
        // the UID match and the Destroyed check filter out. Once per tame, never on a tick.
        var cardBaseType = seen == 0 ? CardUtil.FindGameType("InGameCardBase") : null;
        if (cardBaseType != null)
        {
            Plugin.Logger.LogWarning($"[CompanionHunt] the {label} tame's drop was not captured; scanning for {companionUid} to initialize its stats (it is not moved if it landed off the board).");
            var all = UnityEngine.Resources.FindObjectsOfTypeAll(cardBaseType);
            if (all != null)
            {
                foreach (var obj in all)
                {
                    if (CardUtil.GetCardUniqueId(obj) != companionUid) continue;
                    if (CardUtil.GetMemberValue(obj, "Destroyed") is true) continue;
                    seen++;
                    if (WolfTickPatch.TryInitFreshSpawn(obj) || TryInitFreshPup(obj))
                        Plugin.Logger.LogDebug($"[CompanionHunt] tamed {label} initialized to full stats immediately after tame.");
                }
            }
        }
        if (seen == 0)
            Plugin.Logger.LogWarning($"[CompanionHunt] no live {companionUid} card found after the {label} tame; its stats were not initialized.");

        // tamedCard is the WILD fox's own board card (ReceivingCardChanges.ModType is None on
        // the tame DragAndDropAction — vanilla never destroys/transforms it), still resolvable to
        // the same live InGameNPC the CardPredicate matched at dispatch time. No safety-net
        // ticker exists for a real vanilla agent (that's an Animals/*.json-only mechanism), so
        // failures here are logged as errors rather than "will retry next tick".
        if (tamedCard != null && TryFindNpcByCard(tamedCard, out var npc) && isWildNpc(npc))
        {
            if (!TrySetStatValue(npc, AgentExistsStatUid, 0f))
                Plugin.Logger.LogError($"[CompanionHunt] TrySetStatValue(AgentExists, 0) failed on tamed vanilla {label}.");
            else
                Plugin.Logger.LogDebug($"[CompanionHunt] Vanilla wild {label} retired (AgentExists -> 0) after successful tame.");

            // Relocate immediately rather than relying on vanilla's own "If Agent Exist is 0 Go
            // to Spirit World" AgentAction, which also gates on AgentRespawnCounter being in a
            // specific range — not guaranteed true at the moment of taming.
            if (TryRelocateToSpiritWorldNow(npc))
                Plugin.Logger.LogDebug($"[CompanionHunt] Vanilla wild {label} relocated to Spirit World immediately.");
            else
                Plugin.Logger.LogError($"[CompanionHunt] immediate relocation of tamed vanilla {label} failed.");
        }
        else
            Plugin.Logger.LogError($"[CompanionHunt] could not resolve tamed vanilla {label}'s NPC instance for retirement.");
    }

    /// <summary>A pup tamed from the wolf pack starts its Growth bar (SpoilageTime) full, not at the
    /// zero a ProducedCards drop leaves it (a zero Growth would fire its OnZero and grow it at once).
    /// The drop's TransferRules already ask for the full value; this is the same fix applied directly,
    /// so a failed transfer cannot grow the pup on the next tick. Only touches a pup that reads 0.</summary>
    private static bool TryInitFreshPup(object card)
    {
        if (CardUtil.GetCardUniqueId(card) != WolfPupId) return false;
        float cur = CardUtil.GetDurability(card, "SpoilageTime");
        float max = CardUtil.GetDurabilityMax(card, "SpoilageTime");
        if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f || cur > 0f) return false;
        CardUtil.SetDurability(card, "SpoilageTime", max);
        return true;
    }

    // ── Vanilla NPC stat/relocation helpers ─────────────────────────────────────────────────

    private static object FindStatInstance(object npc, string statUid, out string cause)
    {
        cause = null;
        if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary dict)
        {
            cause = "NPCStatsDict missing or not an IDictionary";
            return null;
        }
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key == null) continue;
            if (Reflect.GetMember(entry.Key, "UniqueID") as string == statUid)
                return entry.Value;
        }
        cause = $"stat '{statUid}' not in NPCStatsDict ({dict.Count} entries)";
        return null;
    }

    private static MethodInfo _setStatValueFromEditor;

    /// <summary>Sets an NPC stat's permanent value. InGameNPCStat has no public SetStatValue: the
    /// real one is a private IEnumerator SetStatValue(float, NPCStatModifierTypes), which a bare
    /// Invoke would only construct and never run. The public SetStatValueFromEditor(float) starts
    /// that coroutine itself with the Permanent modifier type (.decomp/InGameNPCStat.cs), so it is
    /// the entry to call. Every exit logs its own cause.</summary>
    private static bool TrySetStatValue(object npc, string statUid, float value)
    {
        try
        {
            var statInstance = FindStatInstance(npc, statUid, out var cause);
            if (statInstance == null)
            {
                Plugin.Logger.LogError($"[CompanionHunt] TrySetStatValue: {cause}.");
                return false;
            }
            _setStatValueFromEditor ??= statInstance.GetType().GetMethod("SetStatValueFromEditor",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            if (_setStatValueFromEditor == null)
            {
                Plugin.Logger.LogError($"[CompanionHunt] TrySetStatValue: {statInstance.GetType().Name}.SetStatValueFromEditor(float) not found.");
                return false;
            }
            _setStatValueFromEditor.Invoke(statInstance, new object[] { value });
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[CompanionHunt] TrySetStatValue failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }

    private const string SpiritWorldUid = "db69b1711fb0190419ce0c5466591772";   // Env_SpiritWorld, CardType 4, not instanced

    /// <summary>Directly relocates an NPC to the Spirit World via GameManager.MoveNPC instead of
    /// waiting for the engine's own next AgentAction evaluation pass, and returns true only when the
    /// NPC is in the Spirit World afterwards. This is what the framework's Npc.MoveTo does (the EnvID
    /// built at the moment of use, never cached, then the landing checked, since MoveNPC moves nobody
    /// without a word when something intercepts it); it is not called because it exists only from
    /// framework 2.28.0 and this mod supports 2.14.1 and later (audit row R7, 2026-10-12). MoveNPC is
    /// synchronous, so the check right after it reads the result. Every false return logs its cause.</summary>
    private static bool TryRelocateToSpiritWorldNow(object npc)
    {
        try
        {
            if (CardUtil.GetGameManagerInstance() is not GameManager gm || gm == null)
            {
                Plugin.Logger.LogError("[CompanionHunt] Spirit World move: no live GameManager.");
                return false;
            }
            if (npc is not InGameNPC live || live == null)
            {
                Plugin.Logger.LogError($"[CompanionHunt] Spirit World move: the NPC is {(npc == null ? "null" : npc.GetType().Name)}, not a live InGameNPC.");
                return false;
            }
            if (CardUtil.GetCardDataById(SpiritWorldUid) is not CardData spiritWorld || spiritWorld == null)
            {
                Plugin.Logger.LogError($"[CompanionHunt] Spirit World move: environment card {SpiritWorldUid} is not loaded.");
                return false;
            }

            var dest = new EnvID(spiritWorld, false);
            if (dest.IsNull)
            {
                Plugin.Logger.LogError("[CompanionHunt] Spirit World move: the game built a null EnvID for the Spirit World card.");
                return false;
            }
            gm.MoveNPC(live, dest);
            if (!dest.MatchesEnv(live.CurrentEnvironment))
            {
                Plugin.Logger.LogError($"[CompanionHunt] Spirit World move: MoveNPC returned but '{live.name}' is in '{live.CurrentEnvironment.SimpleEnvName}', not the Spirit World; something intercepted the move.");
                return false;
            }
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

    /// <summary>Feature 28. The hawk catches only the vanilla hare and partridge CARCASSES (never the captive
    /// PartridgeFree livestock cards), and about half as often as the wolf: the wolf catches something nine
    /// hunts in ten, the hawk 45 in 100 (partridge 25, hare 20, nothing 55). A balance placeholder until
    /// Sirus reviews (plan open question 14).</summary>
    private static void SpawnHawkHuntPrey()
    {
        int roll = UnityEngine.Random.Range(1, 21);  // 1-20 inclusive
        string uid =
            roll <= 5 ? DeadPartridgeUid :
            roll <= 9 ? DeadHareUid :
            null;

        Plugin.Logger.LogDebug($"[CompanionHunt] hawk hunt roll={roll} -> {uid ?? "nothing"}");
        if (uid != null)
            SpawnService.Spawn(uid);
    }

    /// <summary>S23-B2: one fetch roll. <paramref name="chance"/> is a percent; on a hit one
    /// <paramref name="uid"/> is spawned on the player's board, with <paramref name="stats"/> when given.</summary>
    private static void RollFetch(string label, int chance, string uid,
        System.Collections.Generic.Dictionary<string, float> stats)
    {
        int roll = UnityEngine.Random.Range(1, 101);  // 1-100 inclusive
        bool hit = roll <= chance;
        Plugin.Logger.LogDebug($"[CompanionHunt] {label} roll={roll} chance={chance} -> {(hit ? uid : "nothing")}");
        if (!hit) return;
        if (stats != null)
            SpawnService.Spawn(uid, stats);
        else
            SpawnService.Spawn(uid);
    }

    private static void GrantPackBondPerk()
    {
        // The framework grant joins InRunAddedPerks and applies the perk's starting modifiers in one
        // step, rolls the list back if ApplyPerk fails (so a later Bond can retry), is idempotent,
        // and warns once per failing cause. A hand-rolled add-then-apply left Pack Bond recorded but
        // never applied when the apply missed: a reload does not re-apply starting modifiers.
        try
        {
            if (GrantPackBondThroughFramework())
                Plugin.Logger.LogDebug("[BondWithWolf] Pack Bond perk held - tracking sharpened.");
        }
        catch (MissingMemberException ex)
        {
            Plugin.Logger.LogWarning($"[BondWithWolf] Pack Bond needs CSFF Mod Framework 2.28.0 or later (CardUtil.GrantInRunPerk); the perk was not granted: {ex.Message}");
        }
    }

    // NoInlining is load-bearing: it keeps the JIT from folding this method, and its typed framework
    // call, into the wrapper above, which would move a missing-member failure out of the try/catch.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool GrantPackBondThroughFramework() => CardUtil.GrantInRunPerk(PackBondPerkUid);
}
