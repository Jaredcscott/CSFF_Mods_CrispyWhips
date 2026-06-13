using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Phase-3 NPCAgent support — load-time validation and run-start injection.
///
/// <para>NPCAgent, NPCDuty, NPCHidingGroup, and NPCStat instances are loaded into AllData
/// by JsonDataLoader. At OnGMInitialized, this service checks whether mod agents appear in
/// GameManager's agent list; if absent, it injects them via <see cref="InjectMissingAgents"/>.
/// A verbose survey of GameManager NPC fields is logged at Debug level and is visible only
/// when VerboseLogging is enabled in the BepInEx config — it serves as a diagnostic aid
/// when the NPC system needs investigation, not as routine output.</para>
///
/// <para>Validated at load time: null-array normalization (prevents NRE during GameManager
/// agent initialization), empty AgentName warning, empty AgentStats warning (agent will
/// have no internal stats).</para>
/// </summary>
internal static class NPCAgentActivationService
{
    private static readonly List<NPCAgent> _modAgents = new();
    private static bool _subscribed;
    private static bool _firstRunConfirmed;
    private static Action _gmInitializedHandler;

    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags SBF =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // ---------------------------------------------------------------- load ---

    public static void ValidateAll(IEnumerable allData)
    {
        _modAgents.Clear();
        var modUniqueIds = Loading.JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        int warnings = 0;
        foreach (var item in allData)
        {
            if (item is not NPCAgent agent) continue;
            if (string.IsNullOrEmpty(agent.UniqueID) || !modUniqueIds.Contains(agent.UniqueID)) continue;

            _modAgents.Add(agent);
            warnings += ValidateAgent(agent);
        }

        if (_modAgents.Count == 0)
        {
            Log.Debug("NPCAgentActivation: no mod NPCAgents loaded");
            return;
        }

        Log.Debug($"NPCAgentActivation: {_modAgents.Count} mod NPCAgent(s) validated, {warnings} warning(s) — run-start diagnostics armed");
        TrySubscribeToGmInitialized();
    }

    private static int ValidateAgent(NPCAgent agent)
    {
        int warnings = 0;

        // AgentName — empty name shows as blank in UI buttons.
        var nameField = typeof(NPCAgent).GetField("AgentName", BF);
        var agentName = nameField?.GetValue(agent) as LocalizedString?;
        if (agentName == null || string.IsNullOrEmpty(agentName.Value.DefaultText))
        {
            Log.Warn($"NPCAgentActivation: {agent.name} has no AgentName.DefaultText — will appear unnamed in-game");
            warnings++;
        }

        // AgentStats — null array NREs during NPC initialization.
        NormalizeNullArray(agent, "AgentStats", ref warnings, $"NPCAgentActivation: {agent.name}");

        // AgentDuties — null NREs during duty scheduling.
        NormalizeNullArray(agent, "AgentDuties", ref warnings, $"NPCAgentActivation: {agent.name}");

        // Interactions — null NREs when game builds the interaction UI.
        NormalizeNullArray(agent, "Interactions", ref warnings, $"NPCAgentActivation: {agent.name}");

        // AgentActions — null NREs during action resolution.
        NormalizeNullArray(agent, "AgentActions", ref warnings, $"NPCAgentActivation: {agent.name}");

        return warnings;
    }

    private static void NormalizeNullArray(NPCAgent agent, string fieldName, ref int warnings, string prefix)
    {
        var field = AccessTools.Field(typeof(NPCAgent), fieldName);
        if (field == null) return;
        var value = field.GetValue(agent);
        if (value != null) return;

        // Null array — create empty array of the correct element type.
        var elemType = field.FieldType.IsArray ? field.FieldType.GetElementType() : null;
        if (elemType != null)
        {
            field.SetValue(agent, Array.CreateInstance(elemType, 0));
            Log.Warn($"{prefix}.{fieldName} was null — normalized to empty array (prevents NRE during game initialization)");
            warnings++;
        }
    }

    // -------------------------------------------------------- subscription ---

    /// <summary>
    /// Subscribes to GameManager.OnGMInitialized so the diagnostics run once per run start,
    /// after GameManager has finished its own initialization (including any AllData iteration
    /// that would discover STAs or agents). Same pattern as StaActivationService.
    /// </summary>
    private static void TrySubscribeToGmInitialized()
    {
        if (_subscribed) return;
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            var field = gmType?.GetField("OnGMInitialized", SBF);
            if (field == null || field.FieldType != typeof(Action))
            {
                Log.Warn("NPCAgentActivation: GameManager.OnGMInitialized not found — run-start diagnostics disabled");
                return;
            }
            _gmInitializedHandler = OnGameManagerInitialized;
            field.SetValue(null, (Action)Delegate.Combine((Action)field.GetValue(null), _gmInitializedHandler));
            _subscribed = true;
            Log.Debug("NPCAgentActivation: subscribed to GameManager.OnGMInitialized");
        }
        catch (Exception ex)
        {
            Log.Warn($"NPCAgentActivation: failed to subscribe to OnGMInitialized: {Log.ExceptionText(ex)}");
        }
    }

    // --------------------------------------------------------- diagnostics ---

    private static void OnGameManagerInitialized()
    {
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            var gm = GetGameManagerInstance(gmType);
            if (gm == null)
            {
                Log.Warn("NPCAgentActivation: GameManager.Instance unavailable — diagnostics skipped");
                return;
            }

            // Survey GameManager for NPC agent collections — output at Debug level so it
            // only surfaces when VerboseLogging is enabled, not on every run start.
            Log.Debug("NPCAgentActivation: [DIAGNOSTICS] Surveying GameManager for NPC agent registries...");
            SurveyNpcFields(gmType, gm);

            // Also look for an NPCManager or WorldNPCManager component the game
            // might delegate agent management to.
            SurveyNpcManagerComponent();

            // Check if mod agents appear in any discovered list.
            CheckModAgentsInNpcLists(gmType, gm);
        }
        catch (Exception ex)
        {
            Log.Warn($"NPCAgentActivation: diagnostics failed: {Log.ExceptionText(ex)}");
        }
    }

    private static object GetGameManagerInstance(Type gmType)
    {
        if (gmType == null) return null;
        for (var t = gmType; t != null; t = t.BaseType)
        {
            var prop = t.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (prop?.CanRead == true)
            {
                try { return prop.GetValue(null); } catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// Walks all fields and properties on GameManager and its base types looking for
    /// anything typed as NPCAgent, NPCAgent[], or List&lt;NPCAgent&gt;.
    /// Each found member is logged with its count — this is the key research output.
    /// </summary>
    private static void SurveyNpcFields(Type gmType, object gm)
    {
        var npcAgentType = typeof(NPCAgent);
        var found = new List<string>();

        for (var t = gmType; t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BF))
            {
                if (!IsNpcCollectionType(field.FieldType, npcAgentType)) continue;
                string countStr = ReadCountStr(field.GetValue(gm));
                found.Add($"  field {t.Name}.{field.Name} ({field.FieldType.Name}) {countStr}");
            }
            foreach (var prop in t.GetProperties(BF))
            {
                if (!prop.CanRead || !IsNpcCollectionType(prop.PropertyType, npcAgentType)) continue;
                object val = null;
                try { val = prop.GetValue(gm, null); } catch { }
                found.Add($"  prop  {t.Name}.{prop.Name} ({prop.PropertyType.Name}) {ReadCountStr(val)}");
            }
        }

        if (found.Count == 0)
            Log.Debug("NPCAgentActivation: [DIAGNOSTICS] No NPCAgent-typed fields/props found on GameManager. Agents may be managed by a separate component or discovered differently.");
        else
            foreach (var line in found)
                Log.Debug($"NPCAgentActivation: [DIAGNOSTICS]{line}");
    }

    private static bool IsNpcCollectionType(Type t, Type npcAgentType)
    {
        if (t == npcAgentType) return true;
        if (t.IsArray && t.GetElementType() == npcAgentType) return true;
        if (t.IsGenericType)
        {
            var args = t.GetGenericArguments();
            if (args.Length == 1 && args[0] == npcAgentType) return true;
        }
        // Also catch "AllNPCAgents", "NPCAgentList" etc. by name when the type itself isn't NPCAgent
        var name = t.Name;
        return (name.Contains("Agent") || name.Contains("NPC"))
            && (t.IsArray || typeof(IList).IsAssignableFrom(t));
    }

    private static string ReadCountStr(object val)
    {
        if (val == null) return "= null";
        if (val is IList list) return $"count={list.Count}";
        return "= (non-list value)";
    }

    /// <summary>
    /// Looks for a MonoBehaviour or component named "NPCManager", "WorldNPCManager",
    /// "AgentManager" etc. — in case GameManager delegates to a separate component.
    /// </summary>
    private static void SurveyNpcManagerComponent()
    {
        foreach (var candidateName in new[] { "NPCManager", "WorldNPCManager", "AgentManager", "NPCScheduler" })
        {
            var t = AccessTools.TypeByName(candidateName);
            if (t == null) continue;
            try
            {
                var allComponents = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                Log.Debug($"NPCAgentActivation: [DIAGNOSTICS] Found {allComponents.Length} instance(s) of {candidateName}");
                if (allComponents.Length > 0)
                {
                    // Log fields on first instance that look like agent lists
                    var inst = allComponents[0];
                    foreach (var f in t.GetFields(BF))
                    {
                        if (!IsNpcCollectionType(f.FieldType, typeof(NPCAgent))) continue;
                        Log.Debug($"NPCAgentActivation: [DIAGNOSTICS]  {candidateName}.{f.Name} ({f.FieldType.Name}) {ReadCountStr(f.GetValue(inst))}");
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Checks whether mod NPCAgents appear in the most likely GameManager agent lists.
    /// If present: AllData registration is sufficient (no separate injector needed).
    /// If absent: an NPCAgentInjector must explicitly add mod agents to the found list.
    /// </summary>
    private static void CheckModAgentsInNpcLists(Type gmType, object gm)
    {
        IList agentList = null;
        string foundIn = null;

        foreach (var name in new[] { "AllAgents", "AllNPCAgents", "AgentList", "Agents", "NPCAgents", "SpawnedAgents" })
        {
            var field = gmType.GetField(name, BF);
            if (field != null) { agentList = field.GetValue(gm) as IList; if (agentList != null) { foundIn = $"GameManager.{name}"; break; } }
            var prop = gmType.GetProperty(name, BF);
            if (prop?.CanRead == true) { agentList = prop.GetValue(gm, null) as IList; if (agentList != null) { foundIn = $"GameManager.{name}"; break; } }
        }

        if (agentList == null)
        {
            Log.Debug("NPCAgentActivation: [DIAGNOSTICS] None of the standard agent list names found on GameManager.");
            return;
        }

        Log.Debug($"NPCAgentActivation: [DIAGNOSTICS] {foundIn} has {agentList.Count} total entries");

        int present = 0;
        var missing = new List<NPCAgent>();
        foreach (var agent in _modAgents)
        {
            bool found = false;
            foreach (var e in agentList) if (ReferenceEquals(e, agent)) { found = true; break; }
            if (found) present++; else missing.Add(agent);
        }

        if (!_firstRunConfirmed)
        {
            _firstRunConfirmed = true;
            if (missing.Count > 0)
            {
                Log.Info($"NPCAgentActivation: {present}/{_modAgents.Count} mod NPCAgent(s) in {foundIn}. Missing: {string.Join(", ", missing.Select(a => a.name))}");
                Log.Info("NPCAgentActivation: [DIAGNOSTICS] Mod agents NOT auto-discovered — injecting into " + foundIn);
                InjectMissingAgents(agentList, missing);
            }
            else
            {
                Log.Info($"NPCAgentActivation: {present}/{_modAgents.Count} mod NPCAgent(s) confirmed in {foundIn} — AllData auto-discovery sufficient");
            }
        }
        else
        {
            Log.Debug($"NPCAgentActivation: {present}/{_modAgents.Count} mod NPCAgent(s) in {foundIn}");
        }
    }

    private static void InjectMissingAgents(IList agentList, List<NPCAgent> missing)
    {
        int injected = 0;
        foreach (var agent in missing)
        {
            try
            {
                agentList.Add(agent);
                injected++;
                Log.Info($"NPCAgentActivation: injected {agent.name} ({agent.UniqueID}) into GameManager agent list");
            }
            catch (Exception ex)
            {
                Log.Error($"NPCAgentActivation: failed to inject {agent.name}: {Log.ExceptionText(ex)}");
            }
        }
        Log.Info($"NPCAgentActivation: {injected}/{missing.Count} mod NPCAgent(s) injected");
    }
}
