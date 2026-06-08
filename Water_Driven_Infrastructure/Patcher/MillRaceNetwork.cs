using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace WaterDrivenInfrastructure.Patcher
{
    public static class MillRaceNetwork
    {
        private const string OutletPlacedID = "water_sawmill_mill_race_outlet_placed";
        private const string OutletFrozenID = "water_sawmill_mill_race_outlet_frozen";
        private const string OutletKitID = "water_sawmill_mill_race_outlet_kit";
        private const string OutletBlueprintID = "water_sawmill_bp_mill_race_outlet";
        private const string LitBrazierID = "advanced_copper_tools_copper_brazier_placed_lit";

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
            "water_sawmill_grinding_mill_placed",
            "water_sawmill_forge_placed",
            "water_sawmill_workshop_placed",
            "water_sawmill_ore_sluice_placed",
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
            "water_sawmill_forge_kit",
            "water_sawmill_workshop_kit",
            "water_sawmill_ore_sluice_kit",
            "water_sawmill_fishpond_kit",
            "water_sawmill_bp_water_driven_sawmill",
            "water_sawmill_bp_grinding_mill",
            "water_sawmill_bp_water_driven_forge",
            "water_sawmill_bp_water_driven_workshop_kit",
            "water_sawmill_bp_ore_sluice_empty",
            "water_sawmill_bp_ore_sluice",
            "water_sawmill_bp_fishpond"
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
                if (!string.IsNullOrEmpty(locationUid) && HasLitBrazierAt(locationUid))
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

                if (IsWinter() && !HasLitBrazierAt(locationUid))
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
            return !string.IsNullOrEmpty(locationUid) && WaterSourceLocationUids.Contains(locationUid);
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

            foreach (var card in GetGameCards("ImprovementCards"))
            {
                var improvementUid = GetCardUniqueId(card);
                if (!EdgesByCloneUid.ContainsKey(improvementUid))
                    continue;
                if (!GetBoolMember(card, "BlueprintComplete"))
                    continue;
                var locationUid = GetCardLocationUid(card);
                if (!string.IsNullOrEmpty(locationUid))
                    AddBuiltImprovement(completedByLocation, locationUid, improvementUid);
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

        private static bool IsImprovementBuilt(string locationUid, string improvementUid)
        {
            locationUid = ToLocationUid(locationUid);
            var data = FindEnvironmentSaveData(locationUid);
            var built = GetBuiltList(data);
            if (built != null && built.Contains(improvementUid))
                return true;

            foreach (var card in GetGameCards("ImprovementCards"))
            {
                if (!string.Equals(GetCardUniqueId(card), improvementUid, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(GetCardLocationUid(card), locationUid, StringComparison.Ordinal))
                    continue;
                if (GetBoolMember(card, "BlueprintComplete"))
                    return true;
            }

            return false;
        }

        private static void MarkImprovementBuilt(string locationUid, string improvementUid)
        {
            locationUid = ToLocationUid(locationUid);
            var data = FindOrCreateEnvironmentSaveData(locationUid);
            var built = GetOrCreateBuiltList(data);
            if (built != null && !built.Contains(improvementUid))
            {
                built.Add(improvementUid);
                InvalidateConnectivityCache();
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
            // A frozen outlet heated by a nearby lit Copper Brazier counts as an active outlet.
            if (HasLitBrazierAt(locationUid))
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

        private static bool HasLitBrazierAt(string locationUid)
        {
            if (string.IsNullOrEmpty(locationUid))
                return false;
            if (!EnvironmentUidByLocationUid.TryGetValue(locationUid, out var targetEnvUid) || string.IsNullOrEmpty(targetEnvUid))
                return false;
            foreach (var card in GetGameCards("AllCards"))
            {
                if (!string.Equals(GetCardUniqueId(card), LitBrazierID, StringComparison.Ordinal))
                    continue;
                if (IsDestroyed(card))
                    continue;
                if (string.Equals(GetCardEnvironmentUid(card), targetEnvUid, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static object FindEnvironmentSaveData(string locationUid)
        {
            var gm = GetGameManager();
            var envData = GetMemberValue(gm, "EnvironmentsData") as IDictionary;
            if (envData == null)
                return null;

            var key = EnvironmentKeyForLocation(locationUid);
            var environmentUid = ToEnvironmentUid(locationUid);
            if (!string.IsNullOrEmpty(key) && envData.Contains(key))
                return envData[key];

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                var dictionaryKey = GetMemberValue(value, "DictionaryKey") as string;
                var environmentId = LoadSavedUniqueId(GetMemberValue(value, "EnvironmentID") as string);
                if (string.Equals(dictionaryKey, key, StringComparison.Ordinal) ||
                    string.Equals(dictionaryKey, environmentUid, StringComparison.Ordinal) ||
                    string.Equals(environmentId, environmentUid, StringComparison.Ordinal))
                    return value;
            }

            return null;
        }

        private static object FindOrCreateEnvironmentSaveData(string locationUid)
        {
            var existing = FindEnvironmentSaveData(locationUid);
            if (existing != null)
                return existing;

            var normalizedLocationUid = ToLocationUid(locationUid);
            if (!LocationsByUid.TryGetValue(normalizedLocationUid, out var location) || location.EnvironmentCard == null)
                return null;

            var gm = GetGameManager();
            var envData = GetMemberValue(gm, "EnvironmentsData") as IDictionary;
            if (envData == null)
                return null;

            var key = EnvironmentKeyForLocation(normalizedLocationUid);
            var saveKey = AddNamesToEnvKey(key);
            var data = new EnvironmentSaveData(location.EnvironmentCard, GetCurrentTick(gm), saveKey);
            var fillCounters = data.GetType().GetMethod("FillCounters", Flags);
            fillCounters?.Invoke(data, new[] { GetMemberValue(gm, "AllCounters") });
            envData[key] = data;
            return data;
        }

        private static List<string> GetBuiltList(object environmentSaveData)
        {
            return GetMemberValue(environmentSaveData, "CurrentlyBuiltImprovements") as List<string>;
        }

        private static List<string> GetOrCreateBuiltList(object environmentSaveData)
        {
            if (environmentSaveData == null)
                return null;
            var built = GetBuiltList(environmentSaveData);
            if (built != null)
                return built;
            built = new List<string>();
            SetMemberValue(environmentSaveData, "CurrentlyBuiltImprovements", built);
            return built;
        }

        private static IEnumerable GetGameCards(string listName)
        {
            return GetMemberValue(GetGameManager(), listName) as IEnumerable ?? Array.Empty<object>();
        }

        private static object GetGameManager()
        {
            var gmType = AccessTools.TypeByName("GameManager");
            if (gmType == null)
                return null;
            return gmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null)
                ?? UnityEngine.Object.FindObjectOfType(gmType);
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

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;
            var type = instance.GetType();
            var field = type.GetField(memberName, Flags);
            if (field != null)
                return field.GetValue(instance);
            var property = type.GetProperty(memberName, Flags);
            if (property != null && property.CanRead)
                return property.GetValue(instance, null);
            return null;
        }

        private static bool SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;
            var type = instance.GetType();
            var field = type.GetField(memberName, Flags);
            if (field != null)
            {
                field.SetValue(instance, value);
                return true;
            }
            var property = type.GetProperty(memberName, Flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return true;
            }
            return false;
        }

        private static string EnvironmentKeyForLocation(string locationUid)
        {
            var normalizedLocationUid = ToLocationUid(locationUid);
            if (normalizedLocationUid != null && LocationsByUid.TryGetValue(normalizedLocationUid, out var location) && !string.IsNullOrEmpty(location.EnvironmentUid))
                return location.EnvironmentUid;
            return ToEnvironmentUid(locationUid);
        }

        private static int GetCurrentTick(object gm)
        {
            var tickInfo = GetMemberValue(gm, "CurrentTickInfo");
            var z = GetMemberValue(tickInfo, "z");
            if (z is int intValue)
                return intValue;
            return 0;
        }

        private static string AddNamesToEnvKey(string key)
        {
            try
            {
                var method = typeof(UniqueIDScriptable).GetMethod("AddNamesToEnvKey", BindingFlags.Public | BindingFlags.Static);
                return method?.Invoke(null, new object[] { key }) as string ?? key;
            }
            catch
            {
                return key;
            }
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
            catch
            {
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
                && !string.Equals(cardId, "water_sawmill_workshop_placed", StringComparison.Ordinal))
                return false;

            return actionName.IndexOf("Smelt", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("Heat Metal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWinter()
        {
            try
            {
                var season = GetMemberValue(GetGameManager(), "CurrentSeason");
                if (season == null)
                    return false;

                var uniqueId = (season as UniqueIDScriptable)?.UniqueID
                    ?? GetMemberValue(season, "UniqueID") as string
                    ?? (season as UnityEngine.Object)?.name;
                return string.Equals(uniqueId, "Winter", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
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