using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using CSFFModFramework.Api;
using Herbs_And_Fungi;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Main orchestrator for game load patches.
    /// Coordinates all runtime data repairs and feature injections via specialized injector classes.
    /// </summary>
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static float DensityScale => Plugin.ForageDropDensityScale?.Value ?? 1.0f;

        /// <summary>
        /// Registers all Harmony patches for game load and UI initialization.
        /// </summary>
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("GameLoad type not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMainGameDataMethod == null)
                {
                    Logger.LogError("GameLoad.LoadMainGameData not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                var postfixMethod = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                if (postfixMethod == null)
                {
                    Logger.LogError("GameLoadPatch.LoadMainGameData_Postfix not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                harmony.Patch(loadMainGameDataMethod, postfix: new HarmonyMethod(postfixMethod));

                // Blueprint tab injection is now handled by CSFFModFramework's BlueprintInjector
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch GameLoad.LoadMainGameData: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Main postfix called after GameLoad.LoadMainGameData completes.
        /// Orchestrates all data repairs and feature injections in proper sequence.
        /// </summary>
        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                // Access DataBase.AllData
                var gameLoadType = __instance.GetType();
                var dataBaseField = AccessTools.Field(gameLoadType, "DataBase");
                var dataBase = dataBaseField?.GetValue(__instance);

                if (dataBase == null)
                {
                    Logger.LogError("Could not access GameLoad.DataBase!");
                    return;
                }

                var dataBaseType = dataBase.GetType();
                var allDataField = AccessTools.Field(dataBaseType, "AllData");
                var allData = allDataField?.GetValue(dataBase);

                if (allData == null)
                {
                    Logger.LogError("Could not access DataBase.AllData!");
                    return;
                }

                var allDataEnumerable = allData as IEnumerable;
                if (allDataEnumerable == null)
                {
                    Logger.LogError("AllData is not enumerable!");
                    return;
                }

                // === Feature Injection ===
                // (WarpData resolution, PassiveEffect normalization, sprite resolution,
                //  DroppedCards population, perk injection, and blueprint tab injection
                //  are now handled by CSFFModFramework)

                // Vanilla-location forage drops are DropInjections.json rules (plan feature 29), injected by the
                // framework before this postfix. A rule cannot create an action, so Dig for Truffles stays here.
                AddDigForTrufflesActions(allDataEnumerable);

                // ForageDropDensityScale (default 1.0 = no-op) scales every H&F forage/dig drop added above.
                ApplyForageDropDensityScale(allDataEnumerable);

                // Cache CardTag scan once for both fermentable-tag and pouch-patch methods
                var cardTagType = AccessTools.TypeByName("CardTag");
                var allCardTags = cardTagType != null && typeof(UnityEngine.Object).IsAssignableFrom(cardTagType)
                    ? Resources.FindObjectsOfTypeAll(cardTagType)
                    : null;

                // Extend vanilla Turnroot/Fireroot with tag_Fermentable so they work in the pickle vat
                AddFermentableTagToVanillaRoots(allDataEnumerable, allCardTags);

                // Accelerate Tendon drying on the vanilla DryingRack and ACT Tea Blending Station
                AddTendonDryingRecipe(allDataEnumerable);

                // Allow the vanilla Pouch to store herb/mushroom powders
                PatchVanillaPouchForPowderStorage(allDataEnumerable, allCardTags);

                // The Herbal Salve's oil slot takes any oil, and Sirus23's Lanolin when that mod is installed:
                // TagGroups.json (GpTag_HnF_SalveBase), filled by the framework (2.28.9), not here.

                // Populate the four pickleable GpTags (auto-created by framework) with raw-only ingredient lists
                GpTagContentPatch.Populate();

                // Blueprint tab injection is now handled by CSFFModFramework's BlueprintInjector
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in LoadMainGameData postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// The 51 vanilla oak and alder Forage locations (EA 0.68b) that get the "Dig for Truffles" action:
        /// the same UIDs, in the same order, as the oak/alder rule of DropInjections.json (plan feature 29;
        /// Documentation/Research/HerbsAndFungi/forage_injection.md section 2.9, rule 5).
        /// HerbsAndFungi-ForageSeasons.Tests.ps1 fails if the two lists drift. Exact UIDs, never a
        /// LocalizationKey.Contains test (CLAUDE.md "Forage Drop Injection").
        /// </summary>
        private static readonly HashSet<string> TruffleDigLocationUids = new HashSet<string>(StringComparer.Ordinal)
        {
            "8442ec3009ef4b348ba4224299b569de", "48763d81ff15a3247af3a2a9db6adbb1", "08a9ab83720ce8d43940b3948290fd63",
            "9222a58ac48eabe4c96aa7c9419a3d53", "1ec714ca136119e499e7afb4a000867b", "4f23f1c0f6efed9439c68d9b0e9ec051",
            "e8783dad39492f847ae400920fb920ba", "1627c16d685a1ce48bd5275a9be19d74", "47b1d75f4127d7c4faa461529f937ae6",
            "716c18156d6816d40b26b4b81a6b9d22", "f874739d449a5c948a0cfa9890fa344c", "b71c4ef8847555b4abcab5730a529145",
            "f5db0a50fd23fe44abce34c36b281dcd", "f1f12161603a1674980559b8b1c67325", "d3bccca6f0e37d649923ea9a996e8388",
            "b9107e4f48ff0d044bd31c7a51d13d40", "3282546098b590d4ba2d40e96460720f", "b618bec5e3d2de64694f5cfb170626d4",
            "49e2b4be940a84f4ca5c08024b5702b6", "2c3a29815c94ebe46a4bdbd1ccb754d0", "4e870b81748a38f4d8922a3fac6cc69a",
            "a9c7fdb278b19414c9d311177402e60b", "5b8ba1433e086464297992c9a8a09dc0", "4b5884ebbb8244a45a119a1701365b1c",
            "a019c1902ef42df4490429cf28534518", "494b063d0a8ca66498dcd0e55b7f15be", "24b989bdc5b3c8a42a16ac3c0fb703a8",
            "975248d7e988366438f877c451ca118d", "3c7144d78a55ebb45b0fdf1598cf6a04", "49c0c329d382aab498b4ff7903f4a38a",
            "a631af4cc22e93a43b70992c974fbbf0", "20dc038ca0f9e3046aa2a401dba16b09", "4f1ceadde2272e74e9b08b7d40eb264c",
            "e40f442246e85014c9a786342fb78a25", "e715a7bb358c727408ca2f454850bca0", "9b72ab8ae43c10345a6d9168d3096b50",
            "4a85ea00396fbe84bb494ddc7abe0de2", "f516c75849230e04e8b386d9f3b25d53", "343475c3f2d73c84f8ffbbbe1f288414",
            "1761b5a45b73fee43a68f6dcec138ef6", "a37bdecbef22b354bbddd37732a54a03", "945a359be2dda5144a0a91ae81ba140f",
            "6f1df28efe418cd4caeb66c8228583f1", "392726f7792a24849b9f3af9148c0871", "5428355fcca1b464e9dfad18d69bf4f6",
            "e2fe83127f87959488d0bc35d65760a4", "30a11e1f41c65774fbdb291aadd64ccf", "b32c74532ed8c634d991a6028bb435e2",
            "5aba809c3bd15904d80260034b976c24", "7eb4ea3c101f2824686e53241133d0cc", "d64538c6aa0ac014e83221fda09ea8eb",
        };

        /// <summary>
        /// Every H&amp;F forage, clear and dig drop at a vanilla location is a DropInjections.json rule, injected
        /// by CSFFModFramework's DropInjector before this postfix runs (plan feature 29 retired the
        /// LocalizationKey.Contains buckets that used to live here). A rule cannot create an action, so the oak
        /// and alder locations still get their "Dig for Truffles" action here.
        /// </summary>
        public static void AddDigForTrufflesActions(IEnumerable allDataEnumerable)
        {
            try
            {
                object truffle = null;
                var locations = new List<object>(TruffleDigLocationUids.Count);
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var uid = CachedField(item.GetType(), "UniqueID")?.GetValue(item) as string;
                    if (uid == null) continue;
                    if (uid == "herbs_fungi_truffle") truffle = item;
                    else if (TruffleDigLocationUids.Contains(uid)) locations.Add(item);
                }

                if (truffle == null)
                {
                    Logger?.LogWarning("[Truffle] herbs_fungi_truffle not found in AllData; no Dig for Truffles action was added.");
                    return;
                }
                if (locations.Count != TruffleDigLocationUids.Count)
                    Logger?.LogWarning($"[Truffle] {locations.Count} of {TruffleDigLocationUids.Count} oak/alder locations resolved; a game update may have renamed or removed the rest.");

                int added = 0, alreadyPresent = 0;
                foreach (var item in locations)
                {
                    var dismantleActionsField = CachedField(item.GetType(), "DismantleActions");
                    var dismantleActions = dismantleActionsField?.GetValue(item) as IList;
                    if (dismantleActions == null || dismantleActions.Count == 0)
                    {
                        Logger?.LogDebug($"[Truffle] {(item as UnityEngine.Object)?.name} has no DismantleActions; skipped.");
                        continue;
                    }
                    if (AddDigForTrufflesAction(item, dismantleActionsField, dismantleActions, truffle, out bool present)) added++;
                    else if (present) alreadyPresent++;
                }
                if (added == 0 && alreadyPresent == 0 && locations.Count > 0)
                    Logger?.LogWarning($"[Truffle] Dig for Truffles was added to none of the {locations.Count} resolved oak/alder locations; see the [Truffle] warning above for the cause (a game update may have changed the Forage action).");
                Logger?.LogDebug($"[Truffle] Added Dig for Truffles to {added} oak/alder locations ({alreadyPresent} already had it).");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Truffle] Error adding Dig for Truffles actions: {ex}");
            }
        }

        /// <summary>
        /// Appends one drop (Quantity 1-1, year-round) to the first collection of a ProducedCards list.
        /// Used only for the fresh "Dig for Truffles" collection. Returns false (after one Warn per
        /// cause) when the drop could not be built, so the caller does not append an action that
        /// digs up nothing.
        /// </summary>
        static bool AddDropToFirstCollection(IList producedCards, object card, float dropChance)
        {
            if (card == null || producedCards == null || producedCards.Count == 0) return false;

            try
            {
                var collection = producedCards[0];
                var dropsField = CachedField(collection.GetType(), "DroppedCards");
                if (!(dropsField?.GetValue(collection) is Array dropsArray))
                {
                    WarnTruffleOnce("no-droppedcards", "CardsDropCollection.DroppedCards is missing or not an array; no truffle drop added.");
                    return false;
                }

                var dropType = dropsArray.GetType().GetElementType();
                var dropChanceField = CachedField(dropType, "DropChance");
                if (dropChanceField == null)
                {
                    WarnTruffleOnce("no-dropchance", "CardDrop.DropChance is missing; no truffle drop added (an unset chance drops every time).");
                    return false;
                }

                // Resolve every field the drop needs BEFORE building it: a renamed one would
                // otherwise leave a null-card drop or an unset chance on every location.
                var droppedCardField = CachedField(dropType, "DroppedCard");
                var quantityField = CachedField(dropType, "Quantity");
                var activeField = CachedField(dropChanceField.FieldType, "Active");
                var baseChanceField = CachedField(dropChanceField.FieldType, "BaseDropChance");
                if (droppedCardField == null || quantityField == null || activeField == null || baseChanceField == null)
                {
                    var missing = new List<string>();
                    if (droppedCardField == null) missing.Add("CardDrop.DroppedCard");
                    if (quantityField == null) missing.Add("CardDrop.Quantity");
                    if (activeField == null) missing.Add($"{dropChanceField.FieldType.Name}.Active");
                    if (baseChanceField == null) missing.Add($"{dropChanceField.FieldType.Name}.BaseDropChance");
                    WarnTruffleOnce("carddrop-fields", $"{string.Join(", ", missing)} not found; no truffle drop added and Dig for Truffles skipped (a game update may have renamed it).");
                    return false;
                }

                var newDrop = Activator.CreateInstance(dropType);
                droppedCardField.SetValue(newDrop, card);
                // Set Quantity to {1, 1} so the item actually drops
                quantityField.SetValue(newDrop, new UnityEngine.Vector2Int(1, 1));

                var dropChanceObj = Activator.CreateInstance(dropChanceField.FieldType);
                activeField.SetValue(dropChanceObj, true);
                baseChanceField.SetValue(dropChanceObj, dropChance);
                dropChanceField.SetValue(newDrop, dropChanceObj);

                var newDropsArray = Array.CreateInstance(dropType, dropsArray.Length + 1);
                Array.Copy(dropsArray, newDropsArray, dropsArray.Length);
                newDropsArray.SetValue(newDrop, dropsArray.Length);
                dropsField.SetValue(collection, newDropsArray);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Truffle] Error adding truffle drop: {ex}");
                return false;
            }
        }

        // ── ForageDropDensityScale ───────────────────────────────────────────
        // The framework writes each DropInjections.json Chance as it is, so the config is applied here,
        // once, after the framework's DropInjector and after Dig for Truffles.

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        private static readonly HashSet<object> _densityScaledCollections = new HashSet<object>(new ReferenceComparer());
        private static readonly HashSet<string> _densityWarned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnDensityOnce(string cause, string message)
        {
            if (_densityWarned.Add(cause)) Logger?.LogWarning($"[Forage] ForageDropDensityScale: {message}");
        }

        /// <summary>
        /// Multiplies the chance of every Herbs and Fungi drop (a card whose UniqueID starts herbs_fungi_) in
        /// every Forage, Clear, Dig up Mud, Dig up Dirt and Dig for Truffles action by the
        /// ForageDropDensityScale config. A seasonal flush (a single positive season modifier, the
        /// DropInjections.json SeasonChance shape) is scaled with it so the flush keeps its proportion; the
        /// -1000 season suppressors are left alone. No-op at the default 1.0. Each drop collection is scaled
        /// at most once per process, so a second LoadMainGameData pass cannot compound it.
        /// </summary>
        public static void ApplyForageDropDensityScale(IEnumerable allDataEnumerable)
        {
            float scale = DensityScale;
            if (Mathf.Approximately(scale, 1f)) return;
            if (scale < 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            {
                WarnDensityOnce("range", $"value {scale} is not a usable multiplier; forage chances left unscaled.");
                return;
            }

            int scaled = 0;
            try
            {
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var dismantleActions = CachedField(item.GetType(), "DismantleActions")?.GetValue(item) as IList;
                    if (dismantleActions == null) continue;

                    foreach (var action in dismantleActions)
                    {
                        if (action == null) continue;
                        var actionName = GetMember(GetMember(action, "ActionName"), "DefaultText") as string;
                        if (actionName == null) continue;
                        bool forageLike = actionName.Contains("Forage") || actionName.Contains("Clear")
                            || actionName == "Dig up Mud" || actionName == "Dig up Dirt" || actionName == "Dig for Truffles";
                        if (!forageLike) continue;

                        var producedCards = CachedField(action.GetType(), "ProducedCards")?.GetValue(action) as IList;
                        if (producedCards == null) continue;
                        foreach (var collection in producedCards)
                        {
                            if (collection == null || !_densityScaledCollections.Add(collection)) continue;
                            scaled += ScaleCollection(collection, scale);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Forage] ForageDropDensityScale: error scaling drops: {ex}");
            }
            Logger?.LogInfo($"[Forage] ForageDropDensityScale {scale}: scaled {scaled} Herbs and Fungi forage/dig drop chances.");
        }

        private static int ScaleCollection(object collection, float scale)
        {
            var dropsField = CachedField(collection.GetType(), "DroppedCards");
            if (!(dropsField?.GetValue(collection) is Array drops))
            {
                WarnDensityOnce("droppedcards", "CardsDropCollection.DroppedCards is missing or not an array; chances left unscaled.");
                return 0;
            }
            var dropType = drops.GetType().GetElementType();
            var cardField = CachedField(dropType, "DroppedCard");
            var chanceField = CachedField(dropType, "DropChance");
            if (cardField == null || chanceField == null)
            {
                WarnDensityOnce("carddrop", "CardDrop.DroppedCard or .DropChance is missing; chances left unscaled.");
                return 0;
            }

            int n = 0;
            for (int i = 0; i < drops.Length; i++)
            {
                var drop = drops.GetValue(i); // boxed CardDrop (a struct): edit the box, then write it back
                var card = cardField.GetValue(drop);
                if (card == null) continue;
                var uid = CachedField(card.GetType(), "UniqueID")?.GetValue(card) as string;
                if (uid == null || !uid.StartsWith("herbs_fungi_", StringComparison.Ordinal)) continue;

                var chance = chanceField.GetValue(drop); // boxed CardDropChance (a struct)
                var baseField = CachedField(chance.GetType(), "BaseDropChance");
                if (baseField == null)
                {
                    WarnDensityOnce("base", "CardDropChance.BaseDropChance is missing; chances left unscaled.");
                    return n;
                }
                // Cap at 100 (BaseDropChance is a 0-100 percent): a scale above 1.0 raises a chance to at most
                // certain, never past it. A chance already authored above 100 is left where it was.
                float baseChance = (float)baseField.GetValue(chance);
                float newBase = Mathf.Min(baseChance * scale, Mathf.Max(baseChance, 100f));
                baseField.SetValue(chance, newBase);
                ScaleFlushModifiers(chance, scale, newBase);
                chanceField.SetValue(drop, chance);
                drops.SetValue(drop, i);
                n++;
            }
            return n;
        }

        // Scales each modifier whose output is one positive value (the framework's SeasonChance flush shape,
        // x == y > 0); the -1000 suppressors and every other shape are left as they are.
        // A flush is capped so base + flush stays at or below 100 when the scale raises it (never below its own
        // authored value), matching the base cap above; at a scale <= 1 the cap never binds.
        private static void ScaleFlushModifiers(object chanceBox, float scale, float scaledBase)
        {
            var modsField = CachedField(chanceBox.GetType(), "StatsModifiers");
            if (!(modsField?.GetValue(chanceBox) is Array mods) || mods.Length == 0) return;
            var modType = mods.GetType().GetElementType();
            var valueField = CachedField(modType, "Value");
            if (valueField == null) { WarnDensityOnce("value", "StatInterpolatedValue.Value is missing; season flushes left unscaled."); return; }
            var outField = CachedField(valueField.FieldType, "OutputValueRange");
            if (outField == null) { WarnDensityOnce("output", "InterpolatedValue.OutputValueRange is missing; season flushes left unscaled."); return; }

            for (int i = 0; i < mods.Length; i++)
            {
                var mod = mods.GetValue(i);
                var iv = valueField.GetValue(mod);
                var range = (Vector2)outField.GetValue(iv);
                if (range.x <= 0f || range.x != range.y) continue;
                float flush = Mathf.Min(range.x * scale, Mathf.Max(range.x, 100f - scaledBase));
                outField.SetValue(iv, new Vector2(flush, flush));
                valueField.SetValue(mod, iv);
                mods.SetValue(mod, i);
            }
        }

        /// <summary>
        /// Clones the oak location's Forage action into a new "Dig for Truffles" action that
        /// only drops truffles, then appends it to the location's DismantleActions.
        /// </summary>
        // One Warn per shape-drift cause in AddDigForTrufflesAction (it runs once per location, so a per-call
        // Warn would print the same line up to 51 times).
        private static readonly HashSet<string> _truffleWarned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnTruffleOnce(string cause, string message)
        {
            if (_truffleWarned.Add(cause)) Logger?.LogWarning($"[Truffle] {message}");
        }

        static bool AddDigForTrufflesAction(object locationItem, FieldInfo dismantleActionsField, IList dismantleActions, object truffle, out bool alreadyPresent)
        {
            alreadyPresent = false;
            try
            {
                // Already added? Search by ActionName.DefaultText.
                object template = null;
                foreach (var action in dismantleActions)
                {
                    if (action == null) continue;
                    var nameObj = GetMember(action, "ActionName");
                    var defText = GetMember(nameObj, "DefaultText") as string;
                    if (defText == "Dig for Truffles") { alreadyPresent = true; return false; }
                    if (defText == "Forage" && template == null) template = action;
                }

                if (template == null)
                {
                    WarnTruffleOnce("no-forage", $"{(locationItem as UnityEngine.Object)?.name} has no \"Forage\" action to clone; Dig for Truffles skipped there (a game update may have renamed it).");
                    return false;
                }

                var actionType = template.GetType();
                var newAction = Activator.CreateInstance(actionType);

                // Shallow-copy every field from template; shared references are fine for
                // stat modifications, action tags, sounds, etc.
                foreach (var fi in actionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    fi.SetValue(newAction, fi.GetValue(template));
                }

                // Replace ActionName with a fresh LocalizedString so we don't mutate the template.
                // Every write is checked: a renamed member would leave the clone named "Forage",
                // which also defeats the "already added?" DefaultText check above.
                var templateNameObj = GetMember(template, "ActionName");
                if (templateNameObj == null)
                {
                    WarnTruffleOnce("no-actionname", "the Forage action has no ActionName; Dig for Truffles skipped (a game update may have renamed it).");
                    return false;
                }
                var nameType = templateNameObj.GetType();
                var newName = Activator.CreateInstance(nameType);
                bool nameOk = SetMember(newName, "ParentObjectID", "")
                    & SetMember(newName, "LocalizationKey", "Herbs_And_Fungi_Action_DigForTruffles")
                    & SetMember(newName, "DefaultText", "Dig for Truffles");
                if (!nameOk || !SetMember(newAction, "ActionName", newName))
                {
                    WarnTruffleOnce("actionname-members", $"could not write the Dig for Truffles name onto {nameType.Name} / {actionType.Name}.ActionName; Dig for Truffles skipped (a game update may have renamed a member).");
                    return false;
                }

                // Digging is harder than foraging; give it a longer daytime cost.
                var daytimeCostField = CachedField(actionType, "DaytimeCost");
                if (daytimeCostField != null) daytimeCostField.SetValue(newAction, 3);
                else WarnTruffleOnce("no-daytimecost", $"{actionType.Name}.DaytimeCost not found; Dig for Truffles keeps the Forage action's time cost.");

                // Build a fresh ProducedCards collection by cloning the template's first entry
                // (inherits CollectionUses, modifiers, messages, etc.) and emptying DroppedCards.
                var producedCardsField = CachedField(actionType, "ProducedCards");
                if (producedCardsField == null)
                {
                    WarnTruffleOnce("no-producedcards-field", $"{actionType.Name}.ProducedCards not found; no Dig for Truffles action can be built.");
                    return false;
                }

                var templateProduced = producedCardsField.GetValue(template) as IList;
                if (templateProduced == null || templateProduced.Count == 0)
                {
                    WarnTruffleOnce("forage-no-collections", $"the Forage action at {(locationItem as UnityEngine.Object)?.name} has no ProducedCards collection to clone; Dig for Truffles skipped there.");
                    return false;
                }

                var templateCollection = templateProduced[0];
                if (templateCollection == null)
                {
                    WarnTruffleOnce("forage-null-collection", $"the Forage action at {(locationItem as UnityEngine.Object)?.name} has a null first ProducedCards collection; Dig for Truffles skipped there.");
                    return false;
                }

                var collectionType = templateCollection.GetType();
                var freshCollection = Activator.CreateInstance(collectionType);
                foreach (var fi in collectionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    fi.SetValue(freshCollection, fi.GetValue(templateCollection));
                }

                // Rename and reset drops so only truffle appears.
                var collectionNameField = CachedField(collectionType, "CollectionName");
                if (collectionNameField != null) collectionNameField.SetValue(freshCollection, "Truffle");
                else WarnTruffleOnce("no-collectionname", $"{collectionType.Name}.CollectionName not found; the truffle collection keeps the Forage collection's name.");
                var droppedCardsField = CachedField(collectionType, "DroppedCards");
                if (droppedCardsField == null)
                {
                    // Without a reset the clone would keep every Forage drop alongside the truffle.
                    WarnTruffleOnce("no-collection-droppedcards", $"{collectionType.Name}.DroppedCards not found; Dig for Truffles skipped (a game update may have renamed it).");
                    return false;
                }
                var dcType = droppedCardsField.FieldType;
                if (dcType.IsArray)
                    droppedCardsField.SetValue(freshCollection, Array.CreateInstance(dcType.GetElementType(), 0));
                else
                    droppedCardsField.SetValue(freshCollection, Activator.CreateInstance(dcType));

                var producedCardsType = producedCardsField.FieldType;
                object freshProducedCards;
                if (producedCardsType.IsArray)
                {
                    var arr = Array.CreateInstance(producedCardsType.GetElementType(), 1);
                    arr.SetValue(freshCollection, 0);
                    freshProducedCards = arr;
                }
                else
                {
                    var list = (IList)Activator.CreateInstance(producedCardsType);
                    list.Add(freshCollection);
                    freshProducedCards = list;
                }

                producedCardsField.SetValue(newAction, freshProducedCards);

                // Now add the truffle drop (25% chance — player chose to dig here)
                if (!AddDropToFirstCollection(freshProducedCards as IList, truffle, 25.0f)) return false;

                // Append newAction to DismantleActions (handle both Array and List<T>)
                if (dismantleActionsField.FieldType.IsArray)
                {
                    var oldArr = dismantleActionsField.GetValue(locationItem) as Array;
                    var elemType = oldArr.GetType().GetElementType();
                    var newArr = Array.CreateInstance(elemType, oldArr.Length + 1);
                    Array.Copy(oldArr, newArr, oldArr.Length);
                    newArr.SetValue(newAction, oldArr.Length);
                    dismantleActionsField.SetValue(locationItem, newArr);
                }
                else
                {
                    dismantleActions.Add(newAction);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Truffle] Error adding Dig for Truffles action: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Appends tag_Fermentable to vanilla TurnrootFresh and FirerootFresh so they can be placed
        /// into the H&F pickle vat alongside tagged mod items.
        /// </summary>
        static void AddFermentableTagToVanillaRoots(IEnumerable allDataEnumerable, UnityEngine.Object[] allCardTags)
        {
            try
            {
                const string TurnrootGuid = "ac174c399999d14489fb3788c8931e93";
                const string FirerootGuid = "5ad63f32ed767c64190a64d79841d023";

                if (allCardTags == null || allCardTags.Length == 0)
                {
                    Logger?.LogError("[FermentableTag] CardTag scan was empty.");
                    return;
                }

                UnityEngine.Object fermentableTag = null;
                foreach (var tag in allCardTags)
                {
                    if (tag == null) continue;
                    if (tag.name == "tag_Fermentable") { fermentableTag = tag; break; }
                }

                if (fermentableTag == null)
                {
                    Logger?.LogError("[FermentableTag] tag_Fermentable not found in Resources.");
                    return;
                }

                // Locate TurnrootFresh and FirerootFresh by UniqueID in AllData
                object turnroot = null;
                object fireroot = null;
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var uniqueIdField = CachedField(item.GetType(), "UniqueID");
                    var uniqueId = uniqueIdField?.GetValue(item) as string;
                    if (uniqueId == TurnrootGuid) turnroot = item;
                    else if (uniqueId == FirerootGuid) fireroot = item;
                    if (turnroot != null && fireroot != null) break;
                }

                AppendFermentableTag(turnroot, fermentableTag, "TurnrootFresh");
                AppendFermentableTag(fireroot, fermentableTag, "FirerootFresh");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FermentableTag] Error: {ex}");
            }
        }

        /// <summary>
        /// Injects a Tendon-specific CookingRecipe onto the vanilla DryingRack, H&F Drying Tray,
        /// and (if loaded) both ACT Tea Blending Station variants. Tendon's "Wetness" countdown lives on
        /// FuelCapacity (-1/dtp passive). The drying rack's built-in recipes only drive
        /// UsageChange, so without this injection the rack has no effect on Tendon.
        /// The injected recipe drives FuelChange: -2/dtp, tripling the dry rate on any
        /// drying surface (passive -1 + recipe -2 = -3/dtp → ~1 in-game day on the rack).
        /// </summary>
        static void AddTendonDryingRecipe(IEnumerable allDataEnumerable)
        {
            const string TendonGuid      = "6d9b61e72f4d5334d91da54dcd939a8a";
            const string DryingRackGuid  = "4e2b3e00c88f8d14cb52a614584a66d5";
            const string DryingTrayId    = "herbs_fungi_drying_tray";
            const string TeaStationId    = "advanced_copper_tools_tea_blending_station";
            const string TeaStationLitId = "advanced_copper_tools_tea_blending_station_lit";

            object tendon = null, dryingRack = null, dryingTray = null, teaStation = null, teaStationLit = null;

            foreach (var item in allDataEnumerable)
            {
                if (item == null) continue;
                var uid = CachedField(item.GetType(), "UniqueID")?.GetValue(item) as string;
                if      (uid == TendonGuid)       tendon       = item;
                else if (uid == DryingRackGuid)   dryingRack   = item;
                else if (uid == DryingTrayId)     dryingTray   = item;
                else if (uid == TeaStationId)     teaStation   = item;
                else if (uid == TeaStationLitId)  teaStationLit = item;
            }

            if (tendon    == null) { Logger?.LogError("[TendonDry] Tendon not found in AllData.");    return; }
            if (dryingRack == null){ Logger?.LogError("[TendonDry] DryingRack not found in AllData."); return; }

            InjectTendonDryingRecipe(dryingRack,    tendon, "DryingRack");
            if (dryingTray   != null) InjectTendonDryingRecipe(dryingTray,   tendon, "DryingTray");
            if (teaStation    != null) InjectTendonDryingRecipe(teaStation,    tendon, "TeaBlendingStation");
            if (teaStationLit != null) InjectTendonDryingRecipe(teaStationLit, tendon, "TeaBlendingStationLit");
        }

        static void InjectTendonDryingRecipe(object stationCard, object tendon, string label)
        {
            try
            {
                var spec = new RecipeSpec
                {
                    CompatibleCards = new[] { tendon },
                    CompatibleTags = Array.Empty<object>(),
                    ConditionsCard = 0, // no heat required
                    Duration = 1,
                    CookerModType = 0, // no change to the station itself
                    IngredientModType = 1, // modify tendon in place
                    UsageChange = Vector2.zero,
                    SpoilageChange = Vector2.zero,
                    FuelChange = new Vector2(-2f, -2f), // drains Wetness faster
                };

                if (RecipeInjector.InjectCookingRecipe(stationCard, spec, label))
                    Logger?.LogDebug($"[TendonDry] Injected Tendon drying recipe on {label}.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TendonDry] {label}: {ex}");
            }
        }

        private static object GetMember(object target, string name) => Reflect.GetMember(target, name);

        private static bool SetMember(object target, string name, object value) => Reflect.SetMember(target, name, value);

        // Cached (Type, fieldName) -> FieldInfo lookups for the load-time per-item loops below
        // (AddDigForTrufflesActions, ApplyForageDropDensityScale, AddFermentableTagToVanillaRoots,
        // AddTendonDryingRecipe, PatchVanillaPouchForPowderStorage), each of which walks the
        // ENTIRE card database once. Reflect.GetMember already caches by (Type, name) for
        // value-only reads, but callers here that need the raw FieldInfo itself (SetValue,
        // FieldType) can't use it — this mirrors the same Dictionary<(Type,string), FieldInfo>
        // pattern CLAUDE.md calls for instead of re-resolving the same field name per item.
        private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

        private static FieldInfo CachedField(Type type, string name)
        {
            // Native GetField, not AccessTools.Field: this is probed against every allData type
            // (SpiceTag, QuestLog, NPCDuty, ...), and AccessTools logs a HarmonyX warning per
            // miss — 26 warnings per load for fields most types legitimately lack.
            var key = (type, name);
            if (!_fieldCache.TryGetValue(key, out var fi))
                _fieldCache[key] = fi = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fi;
        }

        /// <summary>
        /// Patches the vanilla Pouch (description: "ideal for preserving powders") to actually
        /// function as a powder container. Sets MaxWeightCapacity and adds tag_Powder /
        /// tag_PowderLiquid to its InventoryFilter so H&amp;F ground herbs fit inside.
        /// </summary>
        static void PatchVanillaPouchForPowderStorage(IEnumerable allDataEnumerable, UnityEngine.Object[] allCardTags)
        {
            const string PouchGuid = "80fb7f8100618414d9abb10dec0e31a5";
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                // Locate the vanilla Pouch CardData
                object pouch = null;
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var uid = CachedField(item.GetType(), "UniqueID")?.GetValue(item) as string;
                    if (uid == PouchGuid) { pouch = item; break; }
                }

                if (pouch == null)
                {
                    Logger?.LogWarning("[PouchPatch] Vanilla Pouch not found in AllData.");
                    return;
                }

                // Give it enough weight capacity for ~10 powder items (each weighs 10)
                var weightField = CachedField(pouch.GetType(), "MaxWeightCapacity");
                if (weightField == null) { Logger?.LogError("[PouchPatch] MaxWeightCapacity field not found."); return; }
                weightField.SetValue(pouch, 100.0f);

                if (allCardTags == null || allCardTags.Length == 0)
                {
                    Logger?.LogWarning("[PouchPatch] CardTag scan was empty; powder filter not added.");
                    return;
                }

                UnityEngine.Object tagPowder = null, tagPowderLiquid = null;
                foreach (var tag in allCardTags)
                {
                    if (tag == null) continue;
                    if (tag.name == "tag_Powder")       tagPowder       = tag;
                    else if (tag.name == "tag_PowderLiquid") tagPowderLiquid = tag;
                    if (tagPowder != null && tagPowderLiquid != null) break;
                }

                if (tagPowder == null) { Logger?.LogWarning("[PouchPatch] tag_Powder not found; powder filter not added."); return; }

                // Access InventoryFilter on the Pouch CardData
                var filterField = AccessTools.Field(pouch.GetType(), "InventoryFilter");
                if (filterField == null) { Logger?.LogError("[PouchPatch] InventoryFilter field not found."); return; }
                var filterObj = filterField.GetValue(pouch);
                if (filterObj == null) { Logger?.LogError("[PouchPatch] InventoryFilter is null."); return; }

                // Access the TagFilters collection inside InventoryFilter
                var tagFiltersField = filterObj.GetType().GetField("TagFilters", Flags);
                if (tagFiltersField == null) { Logger?.LogError("[PouchPatch] TagFilters field not found."); return; }
                var tagFiltersObj = tagFiltersField.GetValue(filterObj);

                // Determine the TagFilter element type
                Type tagFilterType = null;
                if (tagFiltersObj is Array ta) tagFilterType = ta.GetType().GetElementType();
                else if (tagFiltersObj != null)
                {
                    var ga = tagFiltersObj.GetType().GetGenericArguments();
                    if (ga.Length > 0) tagFilterType = ga[0];
                }
                if (tagFilterType == null) { Logger?.LogError("[PouchPatch] Cannot resolve TagFilter element type."); return; }

                // Resolve TagFilter.Tag once: a renamed field would otherwise append tagless entries
                // (a null CardTag in vanilla data) while still logging the patch as applied.
                var tagField = tagFilterType.GetField("Tag", Flags);
                if (tagField == null) { Logger?.LogError($"[PouchPatch] {tagFilterType.Name}.Tag field not found; powder filter not added."); return; }

                // Build the new TagFilter entries for tag_Powder and (optionally) tag_PowderLiquid
                var newFilters = new List<object>();
                foreach (var tag in new[] { tagPowder, tagPowderLiquid })
                {
                    if (tag == null) continue;
                    var tf = Activator.CreateInstance(tagFilterType);
                    tagField.SetValue(tf, tag);
                    // NOT = false, OnlyWithLiquid = false  (value-type defaults are already correct)
                    newFilters.Add(tf);
                }

                // Append the new entries – handles both Array and List<T>
                if (tagFiltersObj is Array existingArr)
                {
                    int old = existingArr.Length;
                    var newArr = Array.CreateInstance(tagFilterType, old + newFilters.Count);
                    Array.Copy(existingArr, newArr, old);
                    for (int i = 0; i < newFilters.Count; i++) newArr.SetValue(newFilters[i], old + i);
                    tagFiltersField.SetValue(filterObj, newArr);
                }
                else if (tagFiltersObj is IList list)
                {
                    foreach (var tf in newFilters) list.Add(tf);
                }
                else { Logger?.LogError("[PouchPatch] TagFilters is neither Array nor IList."); return; }

                // Write the (potentially boxed) filter struct back onto the card
                filterField.SetValue(pouch, filterObj);

                Logger?.LogDebug("[PouchPatch] Vanilla Pouch patched: capacity=100, accepts tag_Powder + tag_PowderLiquid.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[PouchPatch] {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void AppendFermentableTag(object card, UnityEngine.Object fermentableTag, string label)
        {
            if (card == null)
            {
                Logger?.LogDebug($"[FermentableTag] {label} not loaded; skipping.");
                return;
            }

            var cardTagsField = AccessTools.Field(card.GetType(), "CardTags");
            if (cardTagsField == null)
            {
                Logger?.LogError($"[FermentableTag] {label}: CardTags field missing.");
                return;
            }

            var existing = cardTagsField.GetValue(card) as Array;
            if (existing != null)
            {
                foreach (var t in existing)
                {
                    if (t as UnityEngine.Object == fermentableTag) return; // already tagged
                }
            }

            var elemType = cardTagsField.FieldType.GetElementType();
            int oldLen = existing?.Length ?? 0;
            var newArr = Array.CreateInstance(elemType, oldLen + 1);
            if (existing != null) Array.Copy(existing, newArr, oldLen);
            newArr.SetValue(fermentableTag, oldLen);
            cardTagsField.SetValue(card, newArr);
            Logger?.LogDebug($"[FermentableTag] Added tag_Fermentable to {label}.");
        }

    }
}
