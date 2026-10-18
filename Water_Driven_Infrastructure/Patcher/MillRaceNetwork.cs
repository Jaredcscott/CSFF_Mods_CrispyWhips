using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    public static class MillRaceNetwork
    {
        private const string OutletPlacedID = "water_sawmill_mill_race_outlet_placed";
        private const string OutletFrozenID = "water_sawmill_mill_race_outlet_frozen";
        private const string OutletKitID = "water_sawmill_mill_race_outlet_kit";
        private const string OutletBlueprintID = "water_sawmill_bp_mill_race_outlet";

        // Winter heat sources (plan section 10, Open question 7's defaults): HasHeatSourceAt counts a card
        // whose UniqueID is in this set, standing in the location's environment, as heat. Each vanilla fire
        // below is a LIT card with a separate Extinguished card (read in the export), so presence means lit.
        // The ACT brazier's lit UID lives in ActCompat; it never matches when ACT is absent. These are
        // tunable placeholders, not balanced: delete a line to take a source away.
        private static readonly HashSet<string> HeatSourceCardUids = new HashSet<string>(StringComparer.Ordinal)
        {
            ActCompat.CopperBrazierLitUid,
            "63e4efe772bf6f649b30c893a0257ef0", // Campfire
            "f16aa437d91e2434ab7a4b7a8827a826", // Fire Pit
            "bbbd576f3cb8e434a85032487085cf57", // Forge
            "984bad0c8931f3545bef58171f9bf252", // Furnace
            "7c01ff23d5697e84d9fa149d1c9e1961", // Bloomery
            "d7d2831f33ccf184e9b09f8411339948", // Kiln
            "eeef909ec09637145a5ff38003b48d8c", // Oven
            "e50543ef8a7e7d543a42e199adeee963"  // Fireplace
        };

        // A WDI Forge or Workshop is a heat source while its Temperature (SpoilageTime, max 1800 on both) is at
        // least this fraction of its maximum. Tunable placeholder (Open question 7), not balanced.
        private static readonly HashSet<string> HeatSourceStationIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "water_sawmill_forge_placed",
            "water_sawmill_workshop_placed",
            "water_sawmill_workshop_placed_expanded"
        };
        private const float HeatSourceMinTemperatureFraction = 0.5f;
        private const string TemperatureStat = "SpoilageTime";

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, LocationRecord> LocationsByUid = new Dictionary<string, LocationRecord>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LocationUidByEnvironmentUid = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> EnvironmentUidByLocationUid = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, EdgeRecord> EdgesByCloneUid = new Dictionary<string, EdgeRecord>(StringComparer.Ordinal);
        private static readonly Dictionary<string, EdgeRecord> EdgesBySourceAndDirection = new Dictionary<string, EdgeRecord>(StringComparer.Ordinal);
        private static readonly HashSet<string> WaterSourceLocationUids = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> WdiStationIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "water_sawmill_placed",
            "water_sawmill_placed_expanded",
            "water_sawmill_grinding_mill_placed",
            "water_sawmill_stamp_mill_placed",
            "water_sawmill_forge_placed",
            "water_sawmill_workshop_placed",
            "water_sawmill_workshop_placed_expanded",
            "water_sawmill_ore_sluice_placed",
            "water_sawmill_fulling_mill_placed",
            "water_sawmill_fishpond_filled",
            "water_sawmill_fishpond_stocked"
        };
        private static readonly HashSet<string> WdiOutletBuildSourceIds = new HashSet<string>(StringComparer.Ordinal)
        {
            OutletKitID,
            OutletBlueprintID
        };
        private static readonly HashSet<string> WdiStationBuildSourceIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "water_sawmill_frame",
            "water_sawmill_grinding_mill_kit",
            "water_sawmill_fulling_mill_kit",
            "water_sawmill_stamp_mill_kit",
            "water_sawmill_forge_kit",
            "water_sawmill_workshop_kit",
            "water_sawmill_ore_sluice_kit",
            "water_sawmill_fishpond_kit",
            "water_sawmill_bp_water_driven_sawmill",
            "water_sawmill_bp_grinding_mill",
            "water_sawmill_bp_fulling_mill",
            "water_sawmill_bp_stamp_mill",
            "water_sawmill_bp_water_driven_forge",
            "water_sawmill_bp_water_driven_workshop_kit",
            "water_sawmill_bp_ore_sluice_empty",
            "water_sawmill_bp_ore_sluice",
            "water_sawmill_bp_fishpond",
            "water_sawmill_bp_reed_bed",
            "water_sawmill_bp_cattail_bed",
            "water_sawmill_bp_water_basin"
        };

        private enum MillRaceGateResult
        {
            Allowed,
            UnknownLocation,
            MissingWaterConnection,
            MissingOutlet,
            FrozenOutlet
        }

        private static ManualLogSource Logger => Plugin.Logger;
        private static bool _completingReciprocal;
        private static bool _networkInitialized;
        private static int _adjacencyCacheFrame = -1;
        private static Dictionary<string, HashSet<string>> _adjacencyCache;

        public sealed class EdgeRecord
        {
            public string CloneUid;
            public CardData CloneCard;
            public string SourceLocationUid;
            public string SourceEnvironmentKey;
            public string DestinationLocationUid;
            public string DestinationEnvironmentKey;
            public int Direction;
            public string OppositeCloneUid;
        }

        private sealed class LocationRecord
        {
            public string LocationUid;
            public string EnvironmentUid;
            public CardData LocationCard;
            public CardData EnvironmentCard;
            public bool IsWaterSource;
        }

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var completeMethod = typeof(InGameCardBase).GetMethod("CompleteImprovement", Flags);
                var postfix = typeof(MillRaceNetwork).GetMethod(nameof(CompleteImprovement_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                if (completeMethod == null || postfix == null)
                {
                    Logger?.LogError("[MillRaceNetwork] CompleteImprovement patch target missing");
                    return;
                }
                harmony.Patch(completeMethod, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[MillRaceNetwork] Patch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        public static void Reset()
        {
            LocationsByUid.Clear();
            LocationUidByEnvironmentUid.Clear();
            EnvironmentUidByLocationUid.Clear();
            EdgesByCloneUid.Clear();
            EdgesBySourceAndDirection.Clear();
            WaterSourceLocationUids.Clear();
            _networkInitialized = false;
            InvalidateConnectivityCache();
        }

        public static void RegisterLocation(CardData location, string environmentUid, CardData environmentCard = null)
        {
            if (location == null || string.IsNullOrEmpty(location.UniqueID))
                return;

            var envUid = string.IsNullOrEmpty(environmentUid) ? location.UniqueID : environmentUid;
            var isWaterSource = IsWaterSourceLocation(location, environmentCard);
            LocationsByUid[location.UniqueID] = new LocationRecord
            {
                LocationUid = location.UniqueID,
                EnvironmentUid = envUid,
                LocationCard = location,
                EnvironmentCard = environmentCard,
                IsWaterSource = isWaterSource
            };
            if (!string.IsNullOrEmpty(envUid))
            {
                LocationUidByEnvironmentUid[envUid] = location.UniqueID;
                EnvironmentUidByLocationUid[location.UniqueID] = envUid;
            }
            if (isWaterSource)
                WaterSourceLocationUids.Add(location.UniqueID);
        }

        public static void RegisterEdge(EdgeRecord edge)
        {
            if (edge == null || edge.CloneCard == null || string.IsNullOrEmpty(edge.CloneUid))
                return;

            EdgesByCloneUid[edge.CloneUid] = edge;
            EdgesBySourceAndDirection[SourceDirectionKey(edge.SourceLocationUid, edge.Direction)] = edge;
            _networkInitialized = true;
            InvalidateConnectivityCache();
        }

        public static EdgeRecord FindOppositeEdge(string sourceLocationUid, string destinationLocationUid, int direction)
        {
            var oppositeDirection = OppositeDirection(direction);
            if (!EdgesBySourceAndDirection.TryGetValue(SourceDirectionKey(destinationLocationUid, oppositeDirection), out var opposite))
                return null;
            return string.Equals(opposite.DestinationLocationUid, sourceLocationUid, StringComparison.Ordinal) ? opposite : null;
        }

        public static bool IsMillRaceClone(string uid)
        {
            return !string.IsNullOrEmpty(uid) && EdgesByCloneUid.ContainsKey(uid);
        }

        public static void SetOpposite(string cloneUid, string oppositeCloneUid)
        {
            if (string.IsNullOrEmpty(cloneUid) || string.IsNullOrEmpty(oppositeCloneUid))
                return;
            if (EdgesByCloneUid.TryGetValue(cloneUid, out var edge))
                edge.OppositeCloneUid = oppositeCloneUid;
        }

        public static string ToLocationUid(string maybeEnvironmentOrLocationUid)
        {
            if (string.IsNullOrEmpty(maybeEnvironmentOrLocationUid))
                return maybeEnvironmentOrLocationUid;
            if (LocationsByUid.ContainsKey(maybeEnvironmentOrLocationUid))
                return maybeEnvironmentOrLocationUid;
            return LocationUidByEnvironmentUid.TryGetValue(maybeEnvironmentOrLocationUid, out var locationUid)
                ? locationUid
                : maybeEnvironmentOrLocationUid;
        }

        public static string ToEnvironmentUid(string maybeEnvironmentOrLocationUid)
        {
            if (string.IsNullOrEmpty(maybeEnvironmentOrLocationUid))
                return maybeEnvironmentOrLocationUid;
            if (EnvironmentUidByLocationUid.TryGetValue(maybeEnvironmentOrLocationUid, out var environmentUid))
                return environmentUid;
            if (LocationUidByEnvironmentUid.ContainsKey(maybeEnvironmentOrLocationUid))
                return maybeEnvironmentOrLocationUid;
            return maybeEnvironmentOrLocationUid;
        }

        public static void LogRegistrySummary()
        {
            int opposites = 0;
            foreach (var edge in EdgesByCloneUid.Values)
            {
                if (!string.IsNullOrEmpty(edge.OppositeCloneUid))
                    opposites++;
            }
            Logger?.LogDebug($"[MillRaceNetwork] Registry: locations={LocationsByUid.Count}, edges={EdgesByCloneUid.Count}, waterSources={WaterSourceLocationUids.Count}, opposites={opposites}");
        }

        public static bool ShouldBlockAction(string cardId, string actionName, object cardInstance)
        {
            var result = GetGateResult(cardId, actionName, cardInstance, out var locationUid);
            if (result == MillRaceGateResult.Allowed)
                return false;

            Logger?.LogDebug($"[MillRaceGate] Blocked '{actionName}' on {cardId}: {result} location={locationUid ?? "<unknown>"}");
            return true;
        }

        private static MillRaceGateResult GetGateResult(string cardId, string actionName, object cardInstance, out string locationUid)
        {
            locationUid = null;
            if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(actionName))
                return MillRaceGateResult.Allowed;

            // If the mill race network never loaded (0 valid edges in static map), allow all actions.
            if (!_networkInitialized)
                return MillRaceGateResult.Allowed;

            if (string.Equals(cardId, OutletFrozenID, StringComparison.Ordinal) && IsDrawWaterAction(actionName))
            {
                locationUid = GetCardLocationUid(cardInstance);
                if (!string.IsNullOrEmpty(locationUid) && HasHeatSourceAt(locationUid))
                    return MillRaceGateResult.Allowed;
                return MillRaceGateResult.FrozenOutlet;
            }

            if (string.Equals(cardId, OutletPlacedID, StringComparison.Ordinal) && IsDrawWaterAction(actionName))
            {
                locationUid = GetCardLocationUid(cardInstance);
                if (string.IsNullOrEmpty(locationUid))
                    return MillRaceGateResult.UnknownLocation;
                return GetOutletDrawResult(locationUid, isFrozenOutlet: false);
            }

            if (IsOutletBuildSource(cardId) && IsPlaceAction(actionName))
            {
                locationUid = GetCurrentLocationUid();
                if (string.IsNullOrEmpty(locationUid))
                    return MillRaceGateResult.UnknownLocation;
                return GetOutletPlacementResult(locationUid);
            }

            if (IsStationBuildSource(cardId) && IsPlaceAction(actionName))
            {
                locationUid = GetCurrentLocationUid();
                if (string.IsNullOrEmpty(locationUid))
                    return MillRaceGateResult.UnknownLocation;
                return GetStationWaterAccessResult(locationUid);
            }

            if (WdiStationIds.Contains(cardId))
            {
                if (IsMaintenanceAction(actionName) || IsStationPreparationAction(actionName))
                    return MillRaceGateResult.Allowed;

                if (IsForgeFireAction(cardId, actionName))
                    return MillRaceGateResult.Allowed;

                locationUid = GetCardLocationUid(cardInstance);
                if (string.IsNullOrEmpty(locationUid))
                    return MillRaceGateResult.UnknownLocation;

                // Optional winter lock: the "Winter Station Lock" setting (default false) must be on
                // before the season is even read, so a default install never takes this branch.
                // A station's own seasonal transform (the fishpond's "Freeze") is never cancelled
                // by the lock, or the pond could not freeze. A heat source at the location exempts it.
                if (WinterStationLockEnabled() && !IsSeasonalTransformAction(actionName)
                    && IsWinter() && !HasHeatSourceAt(locationUid))
                    return MillRaceGateResult.FrozenOutlet;

                return GetStationWaterAccessResult(locationUid);
            }

            return MillRaceGateResult.Allowed;
        }

        private static MillRaceGateResult GetOutletPlacementResult(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return MillRaceGateResult.UnknownLocation;
            if (HasDirectWater(locationUid) || IsLocationConnectedToWater(locationUid))
                return MillRaceGateResult.Allowed;
            return MillRaceGateResult.MissingWaterConnection;
        }

        private static MillRaceGateResult GetOutletDrawResult(string locationUid, bool isFrozenOutlet)
        {
            locationUid = ToLocationUid(locationUid);
            if (isFrozenOutlet)
                return MillRaceGateResult.FrozenOutlet;
            if (string.IsNullOrEmpty(locationUid))
                return MillRaceGateResult.UnknownLocation;
            if (HasDirectWater(locationUid))
                return MillRaceGateResult.Allowed;

            bool connected = IsLocationConnectedToWater(locationUid);
            if (!connected)
                return MillRaceGateResult.MissingWaterConnection;
            return HasPlacedOutletAt(locationUid) ? MillRaceGateResult.Allowed : MillRaceGateResult.MissingOutlet;
        }

        private static MillRaceGateResult GetStationWaterAccessResult(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return MillRaceGateResult.UnknownLocation;
            if (HasDirectWater(locationUid))
                return MillRaceGateResult.Allowed;

            bool connected = IsLocationConnectedToWater(locationUid);
            if (!connected)
                return MillRaceGateResult.MissingWaterConnection;
            return HasPlacedOutletAt(locationUid) ? MillRaceGateResult.Allowed : MillRaceGateResult.MissingOutlet;
        }

        public static bool IsCurrentLocationConnectedToWater()
        {
            return IsLocationConnectedToWater(GetCurrentLocationUid());
        }

        public static bool CurrentLocationHasDirectWaterOrConnectedOutlet()
        {
            return LocationHasDirectWaterOrConnectedOutlet(GetCurrentLocationUid());
        }

        public static bool LocationHasDirectWaterOrConnectedOutlet(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return false;
            if (HasDirectWater(locationUid))
                return true;
            return HasConnectedOutletAt(locationUid);
        }

        private static bool CanPlaceOutletAt(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            return HasDirectWater(locationUid) || IsLocationConnectedToWater(locationUid);
        }

        private static bool CanDrawFromOutletAt(string locationUid, bool isFrozenOutlet)
        {
            locationUid = ToLocationUid(locationUid);
            if (isFrozenOutlet || string.IsNullOrEmpty(locationUid))
                return false;
            if (HasDirectWater(locationUid))
                return true;
            return HasConnectedOutletAt(locationUid);
        }

        private static bool CanPlaceStationAt(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            return HasDirectWater(locationUid) || HasConnectedOutletAt(locationUid);
        }

        private static bool CanUseStationAt(string locationUid)
        {
            return CanPlaceStationAt(locationUid);
        }

        private static bool HasDirectWater(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return false;
            return WaterSourceLocationUids.Contains(locationUid) || HasTaggedWaterSourceCardAt(locationUid);
        }

        // Interop tag (plan feature 16a): any mod's card whose tag runtime .name is WaterSourceInteropTag, standing in
        // the location's environment, makes it a placement-only water source. Compared by NAME, never by SO instance
        // (each mod's WarpResolver mints its own tag object), and the AllCards scan is paired with the environment
        // test (ChangeEnvironment keeps carried cards in AllCards). Draw and cistern feed stay outlet-only.
        public const string WaterSourceInteropTag = "tag_x_WaterSource";

        private static bool HasTaggedWaterSourceCardAt(string locationUid)
        {
            if (!EnvironmentUidByLocationUid.TryGetValue(locationUid, out var targetEnvUid) || string.IsNullOrEmpty(targetEnvUid))
                return false;
            foreach (var card in GetGameCards("AllCards"))
            {
                if (IsDestroyed(card))
                    continue;
                if (!(GetMemberValue(card, "CardModel") is CardData model))
                    continue;
                if (!HasTagName(model, WaterSourceInteropTag))
                    continue;
                if (string.Equals(GetCardEnvironmentUid(card), targetEnvUid, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool HasConnectedOutletAt(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            return !string.IsNullOrEmpty(locationUid) && IsLocationConnectedToWater(locationUid) && HasPlacedOutletAt(locationUid);
        }

        private static bool IsOutletBuildSource(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && WdiOutletBuildSourceIds.Contains(cardId);
        }

        private static bool IsStationBuildSource(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && WdiStationBuildSourceIds.Contains(cardId);
        }

        private static bool IsDrawWaterAction(string actionName)
        {
            return !string.IsNullOrEmpty(actionName) && actionName.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsLocationConnectedToWater(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return false;
            if (WaterSourceLocationUids.Contains(locationUid))
                return true;

            var adjacency = GetCompletedAdjacency();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            visited.Add(locationUid);
            pending.Enqueue(locationUid);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (WaterSourceLocationUids.Contains(current))
                    return true;
                if (!adjacency.TryGetValue(current, out var nextLocations))
                    continue;
                foreach (var next in nextLocations)
                {
                    if (visited.Add(next))
                        pending.Enqueue(next);
                }
            }

            return false;
        }

        private static void CompleteImprovement_Postfix(InGameCardBase __instance)
        {
            if (_completingReciprocal || __instance == null || __instance.CardModel == null)
                return;
            var completedUid = __instance.CardModel.UniqueID;
            if (!EdgesByCloneUid.TryGetValue(completedUid, out var edge))
                return;
            if (string.IsNullOrEmpty(edge.OppositeCloneUid) || !EdgesByCloneUid.TryGetValue(edge.OppositeCloneUid, out var opposite))
                return;

            try
            {
                _completingReciprocal = true;
                MarkImprovementBuilt(edge.SourceLocationUid, edge.CloneUid);
                MarkImprovementBuilt(opposite.SourceLocationUid, opposite.CloneUid);
                CompleteActiveImprovementInstance(opposite.SourceLocationUid, opposite.CloneUid);
                Logger?.LogDebug($"[MillRaceNetwork] Completed reciprocal {opposite.CloneUid} for {edge.CloneUid}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[MillRaceNetwork] Reciprocal completion failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            finally
            {
                _completingReciprocal = false;
            }
        }

        private static Dictionary<string, HashSet<string>> BuildCompletedAdjacency()
        {
            var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var completedByLocation = BuildCompletedImprovementsByLocation();
            foreach (var edge in EdgesByCloneUid.Values)
            {
                if (!completedByLocation.TryGetValue(ToLocationUid(edge.SourceLocationUid), out var built) || !built.Contains(edge.CloneUid))
                    continue;

                // Both directions of a hop must be built before the link contributes to connectivity.
                // A single directional race (e.g. East2 without West2) leaves the channel incomplete.
                if (!string.IsNullOrEmpty(edge.OppositeCloneUid))
                {
                    if (!EdgesByCloneUid.TryGetValue(edge.OppositeCloneUid, out var oppositeEdge))
                        continue;
                    if (!completedByLocation.TryGetValue(ToLocationUid(oppositeEdge.SourceLocationUid), out var oppositeBuilt) || !oppositeBuilt.Contains(edge.OppositeCloneUid))
                        continue;
                }

                AddAdjacency(adjacency, edge.SourceLocationUid, edge.DestinationLocationUid);
                AddAdjacency(adjacency, edge.DestinationLocationUid, edge.SourceLocationUid);
            }
            return adjacency;
        }

        private static Dictionary<string, HashSet<string>> GetCompletedAdjacency()
        {
            var frame = UnityEngine.Time.frameCount;
            if (_adjacencyCache != null && _adjacencyCacheFrame == frame)
                return _adjacencyCache;

            _adjacencyCache = BuildCompletedAdjacency();
            _adjacencyCacheFrame = frame;
            return _adjacencyCache;
        }

        private static void InvalidateConnectivityCache()
        {
            _adjacencyCacheFrame = -1;
            _adjacencyCache = null;
        }

        private static Dictionary<string, HashSet<string>> BuildCompletedImprovementsByLocation()
        {
            var completedByLocation = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var gm = GetGameManager();
            var envData = GetMemberValue(gm, "EnvironmentsData") as IDictionary;
            if (envData != null)
            {
                foreach (DictionaryEntry entry in envData)
                {
                    var locationUid = LocationUidForSaveEntry(entry);
                    if (string.IsNullOrEmpty(locationUid))
                        continue;
                    AddBuiltImprovements(completedByLocation, locationUid, GetBuiltEntries(entry.Value));
                }
            }

            // CurrentlyBuiltImprovements means "present or being built": vanilla adds an improvement to it when
            // construction STARTS (GameManager.StartBuildingImprovement), so the persisted list alone counts a
            // Mill Race that is only half built. A live card on the board is the authority on the player's
            // current location (the framework's CardUtil.IsImprovementBuilt reads it the same way): a complete
            // one counts, and one that is not complete overrules its list entry. Every matching instance is
            // read, never the first, because a UniqueOnBoard card can have two.
            var liveComplete = new HashSet<string>(StringComparer.Ordinal);
            var liveIncomplete = new List<KeyValuePair<string, string>>();
            foreach (var card in GetGameCards("ImprovementCards"))
            {
                var improvementUid = GetCardUniqueId(card);
                if (string.IsNullOrEmpty(improvementUid) || !EdgesByCloneUid.ContainsKey(improvementUid) || IsDestroyed(card))
                    continue;
                var locationUid = GetCardLocationUid(card);
                if (string.IsNullOrEmpty(locationUid))
                    continue;
                if (GetBoolMember(card, "BlueprintComplete"))
                {
                    AddBuiltImprovement(completedByLocation, locationUid, improvementUid);
                    liveComplete.Add(locationUid + "|" + improvementUid);
                }
                else
                {
                    liveIncomplete.Add(new KeyValuePair<string, string>(locationUid, improvementUid));
                }
            }
            foreach (var pending in liveIncomplete)
            {
                if (liveComplete.Contains(pending.Key + "|" + pending.Value))
                    continue;
                if (completedByLocation.TryGetValue(pending.Key, out var builtHere))
                    builtHere.Remove(pending.Value);
            }

            return completedByLocation;
        }

        private static IEnumerable<string> GetBuiltEntries(object environmentSaveData)
        {
            var built = GetMemberValue(environmentSaveData, "CurrentlyBuiltImprovements") as IEnumerable;
            if (built == null)
                yield break;
            foreach (var entry in built)
            {
                if (entry is string uid)
                    yield return uid;
            }
        }

        private static void AddBuiltImprovements(Dictionary<string, HashSet<string>> completedByLocation, string locationUid, IEnumerable<string> improvementUids)
        {
            if (improvementUids == null)
                return;
            foreach (var improvementUid in improvementUids)
            {
                if (EdgesByCloneUid.ContainsKey(improvementUid))
                    AddBuiltImprovement(completedByLocation, locationUid, improvementUid);
            }
        }

        private static void AddBuiltImprovement(Dictionary<string, HashSet<string>> completedByLocation, string locationUid, string improvementUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid) || string.IsNullOrEmpty(improvementUid))
                return;
            if (!completedByLocation.TryGetValue(locationUid, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                completedByLocation[locationUid] = set;
            }
            set.Add(improvementUid);
        }

        private static string LocationUidForSaveEntry(DictionaryEntry entry)
        {
            var value = entry.Value;
            var dictionaryKey = GetMemberValue(value, "DictionaryKey") as string;
            var environmentId = LoadSavedUniqueId(GetMemberValue(value, "EnvironmentID") as string);
            var entryKey = entry.Key as string;

            return LocationUidForSavedEnvironmentKey(dictionaryKey)
                ?? LocationUidForSavedEnvironmentKey(environmentId)
                ?? LocationUidForSavedEnvironmentKey(entryKey);
        }

        private static string LocationUidForSavedEnvironmentKey(string savedKey)
        {
            if (string.IsNullOrEmpty(savedKey))
                return null;

            var loaded = LoadSavedUniqueId(savedKey);
            var direct = ToLocationUid(loaded);
            if (!string.IsNullOrEmpty(direct) && LocationsByUid.ContainsKey(direct))
                return direct;

            foreach (var location in LocationsByUid.Values)
            {
                if (!string.IsNullOrEmpty(location.EnvironmentUid) && loaded.IndexOf(location.EnvironmentUid, StringComparison.Ordinal) >= 0)
                    return location.LocationUid;
                if (!string.IsNullOrEmpty(location.LocationUid) && loaded.IndexOf(location.LocationUid, StringComparison.Ordinal) >= 0)
                    return location.LocationUid;
            }

            return null;
        }

        private static void AddAdjacency(Dictionary<string, HashSet<string>> adjacency, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return;
            if (!adjacency.TryGetValue(from, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                adjacency[from] = set;
            }
            set.Add(to);
        }

        // Marks one end of a hop built in ITS OWN environment's saved data, for a location the player has
        // never visited and for one they have left. GameManager.EnvironmentsData is a
        // Dictionary<EnvDictKey, EnvironmentSaveDataByReference> (EnvDictKey is the env card's
        // UniqueIDIndex), so the entry is found or created through the game's own
        // GetEnvSaveData(EnvID, _CreateIfNull: true), which CardUtil.MarkImprovementBuilt calls; a left
        // location's DictionaryKey reads "uid(Name)" and never matched a bare id by string, and this code
        // used to build an EnvironmentSaveData under a string key, which a dictionary of that key type
        // refuses (plan defect D: "Reciprocal completion failed").
        private static void MarkImprovementBuilt(string locationUid, string improvementUid)
        {
            var environmentUid = EnvironmentKeyForLocation(locationUid);
            bool marked = CardUtil.MarkImprovementBuilt(environmentUid, improvementUid);
            if (!marked)
                WarnOnce("mark-improvement-built",
                    $"Could not mark '{improvementUid}' built at '{environmentUid}' (CardUtil.MarkImprovementBuilt returned false: the game may have renamed EnvironmentsData, GetEnvSaveData or CurrentlyBuiltImprovements); a Mill Race completed at the other end will not connect this location");
            else
                MarkImprovementFinished(environmentUid, improvementUid);
            InvalidateConnectivityCache();
        }

        // Vanilla completes an improvement card that SPAWNS in an environment whose saved data lists it in
        // FinishedImprovements (GameManager, the spawn path after Init: ImprovementWasFinished). A hop's far
        // end may already exist as an unfinished card in a location the player has left; without this entry
        // it would come back under construction while the hop is marked built. The entry is read only
        // there, so writing it for a location with no such card is inert.
        private static void MarkImprovementFinished(string environmentUid, string improvementUid)
        {
            try
            {
                var gm = GetGameManager();
                var envIdType = CardUtil.FindGameType("EnvID");
                MethodInfo getEnvSaveData = null;
                if (gm != null)
                    foreach (var method in gm.GetType().GetMethods(Flags))
                    {
                        var parameters = method.GetParameters();
                        if (method.Name == "GetEnvSaveData" && parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                        {
                            getEnvSaveData = method;
                            break;
                        }
                    }
                if (envIdType == null || getEnvSaveData == null)
                {
                    WarnOnce("finished-improvements-reflection",
                        "GameManager.GetEnvSaveData or the EnvID type was not found, so a far-end Mill Race that already exists as an unfinished card is not marked finished (the hop still connects); the game may have renamed them");
                    return;
                }

                var envId = Activator.CreateInstance(envIdType, new object[] { environmentUid });
                var data = getEnvSaveData.Invoke(gm, new object[] { envId, false });
                var finishedField = data?.GetType().GetField("FinishedImprovements", Flags);
                if (finishedField == null)
                {
                    WarnOnce("finished-improvements-field",
                        $"EnvironmentSaveDataByReference.FinishedImprovements was not found for '{environmentUid}' (no saved data, or the game renamed it), so a far-end Mill Race that already exists as an unfinished card is not marked finished");
                    return;
                }
                if (!(finishedField.GetValue(data) is IList finished))
                {
                    finished = new List<string>();
                    finishedField.SetValue(data, finished);
                }
                if (!finished.Contains(improvementUid))
                    finished.Add(improvementUid);
            }
            catch (Exception ex)
            {
                WarnOnce("finished-improvements-threw", $"Marking '{improvementUid}' finished at '{environmentUid}' threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static void CompleteActiveImprovementInstance(string locationUid, string improvementUid)
        {
            locationUid = ToLocationUid(locationUid);
            foreach (var card in GetGameCards("ImprovementCards"))
            {
                if (!string.Equals(GetCardUniqueId(card), improvementUid, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(GetCardLocationUid(card), locationUid, StringComparison.Ordinal))
                    continue;
                if (GetBoolMember(card, "BlueprintComplete"))
                    return;
                var method = card.GetType().GetMethod("CompleteImprovement", Flags);
                method?.Invoke(card, null);
                return;
            }
        }

        /// <summary>True when a RUNNING (Placed, not Frozen, not Closed) outlet stands at the location (feature 16b: only a running
        /// outlet fills an ACT bathtub).</summary>
        public static bool HasRunningOutletAt(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return false;
            foreach (var card in GetGameCards("AllCards"))
            {
                if (!string.Equals(GetCardUniqueId(card), OutletPlacedID, StringComparison.Ordinal) || IsDestroyed(card))
                    continue;
                if (string.Equals(GetCardLocationUid(card), locationUid, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool HasPlacedOutletAt(string locationUid)
        {
            locationUid = ToLocationUid(locationUid);
            if (string.IsNullOrEmpty(locationUid))
                return false;
            foreach (var card in GetGameCards("AllCards"))
            {
                var uid = GetCardUniqueId(card);
                if (!string.Equals(uid, OutletPlacedID, StringComparison.Ordinal))
                    continue;
                if (IsDestroyed(card))
                    continue;
                if (string.Equals(GetCardLocationUid(card), locationUid, StringComparison.Ordinal))
                    return true;
            }
            // A frozen outlet at a location with a heat source counts as an active outlet (it carries no
            // cistern feed, so heat never restarts the Rain Cistern: only the Placed outlet has that effect).
            // A Closed outlet (the Control Valve, MillRaceOutlet_Closed.json, plan feature 22) is never
            // counted, heat or no heat: a shut valve is the player's choice, not the season's. Its UID is
            // deliberately named nowhere in this class (a WDI contract asserts that). The network itself
            // keeps no per-outlet registration: locations and edges come from the static map, and an
            // outlet is found by this AllCards scan at query time, so closing one unregisters nothing.
            if (HasHeatSourceAt(locationUid))
            {
                foreach (var card in GetGameCards("AllCards"))
                {
                    var uid = GetCardUniqueId(card);
                    if (!string.Equals(uid, OutletFrozenID, StringComparison.Ordinal))
                        continue;
                    if (IsDestroyed(card))
                        continue;
                    if (string.Equals(GetCardLocationUid(card), locationUid, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        // True when a heat source stands at the location (plan section 10): a lit ACT Copper Brazier (through
        // ActCompat), one of the eight vanilla fire cards, or a WDI Forge or Workshop whose Temperature is at
        // or above HeatSourceMinTemperatureFraction of its maximum. An AllCards UID scan is NOT the cards in
        // this room (ChangeEnvironment keeps carried and independent cards in the list), so every candidate is
        // paired with the location's environment UID. Reached only from the frozen-outlet draw branch of
        // GetGateResult, the Winter Station Lock exemption (lock on, winter) and HasPlacedOutletAt.
        private static bool HasHeatSourceAt(string locationUid)
        {
            if (string.IsNullOrEmpty(locationUid))
                return false;
            if (!EnvironmentUidByLocationUid.TryGetValue(locationUid, out var targetEnvUid) || string.IsNullOrEmpty(targetEnvUid))
                return false;
            foreach (var card in GetGameCards("AllCards"))
            {
                var uid = GetCardUniqueId(card);
                if (string.IsNullOrEmpty(uid))
                    continue;
                bool isFire = HeatSourceCardUids.Contains(uid);
                bool isStation = !isFire && HeatSourceStationIds.Contains(uid);
                if (!isFire && !isStation)
                    continue;
                if (IsDestroyed(card))
                    continue;
                if (!string.Equals(GetCardEnvironmentUid(card), targetEnvUid, StringComparison.Ordinal))
                    continue;
                if (isFire || IsStationHot(card))
                    return true;
            }
            return false;
        }

        private static bool IsStationHot(object card)
        {
            float temperature = CardUtil.GetDurability(card, TemperatureStat);
            float max = CardUtil.GetDurabilityMax(card, TemperatureStat);
            if (float.IsNaN(temperature) || float.IsNaN(max) || max <= 0f)
            {
                WarnOnce("heat-source-temperature-unreadable",
                    "A Water-Driven Forge or Workshop's Temperature (SpoilageTime) could not be read, so it is not counted as a winter heat source; CardUtil.GetDurability or GetDurabilityMax returned NaN or the game renamed the stat");
                return false;
            }
            return temperature >= max * HeatSourceMinTemperatureFraction;
        }

        private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (!_warnedCauses.Add(cause))
                return;
            Logger?.LogWarning($"[MillRaceNetwork] {message} [cause '{cause}', logged once per session]");
        }

        private static IEnumerable GetGameCards(string listName)
        {
            return GetMemberValue(GetGameManager(), listName) as IEnumerable ?? Array.Empty<object>();
        }

        private static object GetGameManager()
        {
            return CardUtil.GetGameManagerInstance();
        }

        private static string GetCurrentEnvironmentUid()
        {
            var currentEnvironment = GetMemberValue(GetGameManager(), "CurrentEnvironment");
            var envCard = GetMemberValue(currentEnvironment, "EnvCard") as UniqueIDScriptable;
            return envCard?.UniqueID;
        }

        private static string GetCurrentLocationUid()
        {
            return ToLocationUid(GetCurrentEnvironmentUid());
        }

        private static string GetCardEnvironmentUid(object card)
        {
            var environment = GetMemberValue(card, "CardEnvironment");
            var envCard = GetMemberValue(environment, "EnvCard") as UniqueIDScriptable;
            return envCard?.UniqueID;
        }

        private static string GetCardLocationUid(object card)
        {
            var locationUid = ToLocationUid(GetCardEnvironmentUid(card));
            if (!string.IsNullOrEmpty(locationUid) && LocationsByUid.ContainsKey(locationUid))
                return locationUid;

            var currentLocationUid = GetCurrentLocationUid();
            return string.IsNullOrEmpty(currentLocationUid) ? locationUid : currentLocationUid;
        }

        private static string GetCardUniqueId(object card)
        {
            var model = GetMemberValue(card, "CardModel") as UniqueIDScriptable;
            return model?.UniqueID;
        }

        private static bool IsDestroyed(object card)
        {
            return GetBoolMember(card, "Destroyed");
        }

        private static bool GetBoolMember(object instance, string memberName)
        {
            var value = GetMemberValue(instance, memberName);
            return value is bool boolValue && boolValue;
        }

        // (Type, member) -> the resolved field or property, so the per-card AllCards scans
        // (HasHeatSourceAt, HasPlacedOutletAt, HasRunningOutletAt, HasTaggedWaterSourceCardAt)
        // resolve each MemberInfo once per type instead of once per card, matching the caching
        // ActionInterceptPatch uses for the same reads. A miss is cached too (null MemberInfo).
        private static readonly Dictionary<(Type, string), MemberInfo> _memberLookups = new Dictionary<(Type, string), MemberInfo>();

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;
            var type = instance.GetType();
            var key = (type, memberName);
            if (!_memberLookups.TryGetValue(key, out var member))
            {
                member = (MemberInfo)type.GetField(memberName, Flags) ?? type.GetProperty(memberName, Flags);
                _memberLookups[key] = member;
            }
            if (member is FieldInfo field)
                return field.GetValue(instance);
            if (member is PropertyInfo property && property.CanRead)
                return property.GetValue(instance, null);
            return null;
        }

        private static string EnvironmentKeyForLocation(string locationUid)
        {
            var normalizedLocationUid = ToLocationUid(locationUid);
            if (normalizedLocationUid != null && LocationsByUid.TryGetValue(normalizedLocationUid, out var location) && !string.IsNullOrEmpty(location.EnvironmentUid))
                return location.EnvironmentUid;
            return ToEnvironmentUid(locationUid);
        }

        private static string LoadSavedUniqueId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;
            try
            {
                var method = typeof(UniqueIDScriptable).GetMethod("LoadID", BindingFlags.Public | BindingFlags.Static);
                return method?.Invoke(null, new object[] { id }) as string ?? id;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug($"[MillRaceNetwork] LoadSavedUniqueId reflection failed: {ex.InnerException?.Message ?? ex.Message}");
                return id;
            }
        }

        private static bool IsWaterSourceLocation(CardData location, CardData environmentCard)
        {
            if (location == null)
                return false;
            if (HasTagName(location, "tag_River"))
                return true;

            var combinedName = (location.name ?? string.Empty) + " " + GetLocalizedDefaultText(GetMemberValue(location, "CardName"));
            if (combinedName.IndexOf("River", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedName.IndexOf("Lake", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var envName = (environmentCard?.name ?? string.Empty) + " " + GetLocalizedDefaultText(GetMemberValue(environmentCard, "CardName"));
            return envName.IndexOf("River", StringComparison.OrdinalIgnoreCase) >= 0
                || envName.IndexOf("Lake", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasTagName(CardData card, string tagName)
        {
            var tags = GetMemberValue(card, "CardTags") as IEnumerable;
            if (tags == null)
                return false;
            foreach (var tag in tags)
            {
                if (tag == null)
                    continue;
                var unique = tag as UniqueIDScriptable;
                if (string.Equals(unique?.UniqueID, tagName, StringComparison.Ordinal) ||
                    string.Equals((tag as UnityEngine.Object)?.name, tagName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string GetLocalizedDefaultText(object localizedString)
        {
            return GetMemberValue(localizedString, "DefaultText") as string ?? string.Empty;
        }

        private static bool IsPlaceAction(string actionName)
        {
            return string.Equals(actionName, "Place", StringComparison.OrdinalIgnoreCase)
                || actionName.IndexOf("Build", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMaintenanceAction(string actionName)
        {
            return actionName.IndexOf("Pack", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Dismantle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsStationPreparationAction(string actionName)
        {
            return actionName.IndexOf("Feed", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Increase Temperature", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Warm Up", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Wait by Fire", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsForgeFireAction(string cardId, string actionName)
        {
            if (!string.Equals(cardId, "water_sawmill_forge_placed", StringComparison.Ordinal)
                && !string.Equals(cardId, "water_sawmill_workshop_placed", StringComparison.Ordinal)
                && !string.Equals(cardId, "water_sawmill_workshop_placed_expanded", StringComparison.Ordinal))
                return false;

            return actionName.IndexOf("Smelt", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Heat Metal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool WinterStationLockEnabled()
        {
            return Plugin.WinterStationLock != null && Plugin.WinterStationLock.Value;
        }

        private static bool IsSeasonalTransformAction(string actionName)
        {
            return actionName.IndexOf("Freeze", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // GameManager.CurrentSeason is a plain `Season` class keyed by SeasonID (.decomp/Season.cs),
        // not a UniqueIDScriptable or a UnityEngine.Object, so the UniqueID/.name reads this method
        // used to do always came back empty and the lock never fired. The framework's GameQuery
        // already resolves SeasonID (its CurrentSeason fix, 2026-06-27) and logs its own read failures.
        private static bool IsWinter()
        {
            try
            {
                return CSFFModFramework.Api.GameQuery.IsWinter;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug($"[MillRaceNetwork] IsWinter season read failed: {ex.Message}");
                return false;
            }
        }

        private static string SourceDirectionKey(string sourceLocationUid, int direction)
        {
            return $"{sourceLocationUid}:{direction}";
        }

        private static int OppositeDirection(int direction)
        {
            switch (direction)
            {
                case 0: return 1;
                case 1: return 0;
                case 2: return 3;
                case 3: return 2;
                default: return -1;
            }
        }
    }
}