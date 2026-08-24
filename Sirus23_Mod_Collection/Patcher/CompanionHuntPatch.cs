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

    private const string WildFoxExistsStatUid = "wildfox_stat_exists";

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
        // CSFFModFramework/Animals/CompanionService.cs. Fox has not migrated yet (see
        // WildFoxLifecyclePatch's doc comment), so FoxTameInit stays below.

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
            CardPredicate = ctx => IsWildFoxCard(ctx.Card),
            ActionNamePrefix = "Attempt to Tame",
            Timing = ActionTiming.AfterWrapped,
            After = _ => InitFreshFoxCompanion(),
        });
    }

    private static bool IsWildFoxCard(object card)
        => card != null
           && WildFoxLifecyclePatch.TryGetFoxNpc(out var npc)
           && ReferenceEquals(Reflect.GetMember(npc, "AssociatedCard"), card);

    /// <summary>
    /// Two fixes for the same action, both needing to run AFTER the tame action's own
    /// coroutine (including its ProducedCards spawn) has fully completed — the same two fixes
    /// the framework's CompanionService now applies generically for the owl (M6,
    /// CSFFModFramework/Animals/CompanionService.cs), duplicated here for the fox until it
    /// migrates onto Animals/Fox.json's own Interactions/Companion sections:
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
    private static void InitFreshFoxCompanion()
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

        if (WildFoxLifecyclePatch.TryGetFoxNpc(out var npc))
        {
            // Set exists=0 to retire the wild fox. Failure here is non-fatal — the framework's
            // AnimalLifecycleTicker safety net will force-retire on the next DTP tick.
            if (!WildFoxLifecyclePatch.TrySetStatValue(npc, WildFoxExistsStatUid, 0f))
                Plugin.Logger.LogError("[CompanionHunt] TrySetStatValue(exists, 0) failed — AnimalLifecycleTicker safety net will retry.");
            else
                Plugin.Logger.LogDebug("[CompanionHunt] Wild Fox retired (exists → 0) after successful tame.");

            // Relocate immediately rather than waiting for the engine's own next
            // UpdatePassiveEffects pass — same interactivity-gap fix as the owl.
            if (WildFoxLifecyclePatch.TryRelocateToSpiritWorldNow(npc))
                Plugin.Logger.LogDebug("[CompanionHunt] Wild Fox relocated to Spirit World immediately — no interactivity gap.");
            else
                Plugin.Logger.LogError("[CompanionHunt] immediate relocation failed — AnimalLifecycleTicker safety net will retire on next DTP tick.");
        }
        else
            Plugin.Logger.LogError("[CompanionHunt] could not resolve Wild Fox NPC — AnimalLifecycleTicker safety net will force-retire on next DTP tick.");
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
