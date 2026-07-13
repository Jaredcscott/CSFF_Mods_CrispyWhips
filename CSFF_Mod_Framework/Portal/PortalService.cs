using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Injection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Portal;

/// <summary>
/// Runtime service for the Portal Hub System — the ONE supported portal mechanism (2026-07-02;
/// see <c>Documentation/Portal_Hub_System.md</c>). A player crafts a Portal Kit and places the
/// Portal Hub CT2 (<c>csffmfwportalplaced</c>) anywhere on the board; it shows one "Travel to
/// [WorldName]" button per mod that ships <c>MapMod.json</c>. Responsibilities:
/// <list type="bullet">
///   <item>Phase 5i-b (data-load): inject one travel DismantleAction per registered mod world
///         into the placed Portal Hub CT2 (<see cref="InjectHubTravelDAs"/>).</item>
///   <item>Run-start (OnGMInitialized): register one <see cref="ActionRouter"/> handler per mod
///         world's injected DA (<see cref="RegisterHubTravelHandlers"/>); auto-inject
///         <c>csffmfw_hub_exit</c> into each registered mod CT4 so players always have a
///         "Return to Portal" button (<see cref="InjectExitCardsIntoModHubs"/>).</item>
/// </list>
///
/// <para>A mod registers a destination purely by shipping <c>MapMod.json</c> with
/// <c>WorldName</c> + <c>EnvironmentUID</c> (preferred, a CT4) or <c>SacredSiteUID</c> (legacy
/// CT8 fallback) — zero mod C# required. An earlier, never-adopted fixed-location-per-mod
/// portal schema (<c>PortalAnchorEnvUID</c>/<c>LandingNodeUID</c>/<c>PortalCardUID</c>) was
/// removed 2026-07-02 in favor of keeping this one build-anywhere mechanism as the permanent
/// design — see <c>Documentation/Design/Unified_Map_Expansion_Design.md</c> §9's staleness
/// banner for the rationale.</para>
/// </summary>
internal static class PortalService
{
    private const string DaKeyPrefix = "CSFFMFW_Portal_DA_";

    // UID of the framework exit card, injected into each mod's CT4 DefaultEnvCardDrops
    // so the player always has a "Return to Portal" button inside any registered mod world.
    private const string HubExitUid = "653ab779572b47039c856911d02c9d51"; // csffmfw_hub_exit

    // ─── Exit card injection ─────────────────────────────────────────────────

    /// <summary>
    /// Injects <c>csffmfw_hub_exit</c> into each registered mod world's CT4 DefaultEnvCardDrops
    /// so players always have a "Return to Portal" button (TravelToPreviousEnv) inside the mod hub.
    /// Idempotent — skips if the exit card is already present.
    /// </summary>
    internal static void InjectExitCardsIntoModHubs()
    {
        int injected = 0;
        foreach (var world in PortalRegistry.Worlds)
        {
            if (world.Index == 0 || string.IsNullOrEmpty(world.EnvironmentUID)) continue;

            // Clone-env worlds (registered via MapNodes.json — e.g. ACT's mining caves) are reached
            // by world-map travel and carry their own injected travel DAs and a one-way map exit back
            // to the vanilla world. Injecting the portal-return "Exit" card (csffmfw_hub_exit) into
            // them clutters the mining board and duplicates the existing map exit, so skip them.
            // See Documentation/Retrospectives/act-cave-mining-drops.md.
            if (WorldMapInjector.IsCloneEnvNode(world.EnvironmentUID))
            {
                Log.Debug($"[PortalService] skipping hub_exit injection for clone-env world '{world.WorldName}' ('{world.EnvironmentUID}') — reached by map travel, has its own exit");
                continue;
            }

            if (HubPortalInjector.InjectIntoDefaultEnvCardDrops(world.EnvironmentUID, HubExitUid))
            {
                injected++;
                Log.Debug($"[PortalService] injected hub_exit into '{world.WorldName}' CT4 hub");
            }
        }
        if (injected > 0)
            Log.Info($"[PortalService] hub_exit auto-injected into {injected} mod CT4 hub(s)");
    }

    // ─── Environment travel helper ──────────────────────────────────────────

    private static MethodInfo _addCardFromSourceMethod;

    private static void StartEnvironmentTravel(CardData cardData, object sourceCard, string worldName)
    {
        if (cardData == null)
            return;

        Log.Debug($"[PortalService] hub travel button clicked: '{worldName}' -> '{cardData.UniqueID}'");

        // GameManager.GiveCard(card, false) uses the cheat/no-source AddCard path. For CT4
        // environment travel that can leave CurrentEnvironment unset when the action started from
        // a portal card, which then nullrefs in CalculateEnvironmentWeight. Starting the private
        // AddCard overload with the clicked portal as _FromCard mirrors the path used by vanilla
        // travel actions and keeps the environment transition state coherent.
        if (TryStartAddCardFromSource(cardData, sourceCard))
        {
            Log.Debug($"[PortalService] hub travel to '{worldName}' started via source-card AddCard path");
            return;
        }

        if (GiveCardViaReflection(cardData))
            Log.Debug($"[PortalService] hub travel to '{worldName}' started via GiveCard reflection fallback");
        else
            Log.Error($"[PortalService] hub travel to '{worldName}' FAILED — both AddCard-from-source and GiveCard reflection paths failed");
    }

    private static bool TryStartAddCardFromSource(CardData cardData, object sourceCard)
    {
        if (sourceCard == null)
            return false;

        try
        {
            var gmInstance = CardUtil.GetGameManagerInstance();
            if (gmInstance == null)
                return false;

            var gmType = gmInstance.GetType();
            _addCardFromSourceMethod ??= gmType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "AddCard") return false;
                    var ps = m.GetParameters();
                    return ps.Length == 16
                        && ps[0].ParameterType == typeof(CardData)
                        && ps[1].ParameterType.Name == "InGameCardBase"
                        && ps[2].ParameterType == typeof(bool);
                });

            if (_addCardFromSourceMethod == null)
            {
                Log.Warn("[PortalService] GameManager.AddCard(CardData, InGameCardBase, ...) not found; falling back to GiveCard");
                return false;
            }

            var parameters = _addCardFromSourceMethod.GetParameters();
            if (!parameters[1].ParameterType.IsInstanceOfType(sourceCard))
            {
                Log.Warn($"[PortalService] portal source card type '{sourceCard.GetType().Name}' is not an InGameCardBase; falling back to GiveCard");
                return false;
            }

            var specialDropNone = Enum.ToObject(parameters[3].ParameterType, 0);
            int currentTick = 0;
            if (CardUtil.GetMemberValue(gmInstance, "CurrentTickInfo") is Vector3Int tickInfo)
                currentTick = tickInfo.z;

            var args = new object[]
            {
                cardData,
                sourceCard,
                true,
                specialDropNone,
                null,
                null,
                null,
                null,
                true,
                SpawningLiquid.DefaultLiquid,
                new Vector2Int(currentTick, 0),
                default(EnvDictKey),
                null,
                true,
                null,
                null
            };

            if (_addCardFromSourceMethod.Invoke(gmInstance, args) is not IEnumerator enumerator)
            {
                Log.Warn("[PortalService] GameManager.AddCard did not return an IEnumerator; falling back to GiveCard");
                return false;
            }

            if (gmInstance is not MonoBehaviour host)
            {
                Log.Warn("[PortalService] GameManager instance is not a MonoBehaviour; falling back to GiveCard");
                return false;
            }

            host.StartCoroutine(enumerator);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[PortalService] source-card environment travel failed for '{cardData.UniqueID}': {Log.ExceptionText(ex)}");
            return false;
        }
    }

    // ─── GiveCard reflection fallback ────────────────────────────────────────

    private static MethodInfo _giveCardMethod;

    private static bool GiveCardViaReflection(CardData cardData)
    {
        if (_giveCardMethod == null)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var gmType = asm?.GetType("GameManager");
            if (gmType == null)
            {
                Log.Warn("[PortalService] Assembly-CSharp GameManager type not found");
                return false;
            }
            _giveCardMethod = gmType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "GiveCard" && m.GetParameters().Length >= 2);
            if (_giveCardMethod == null)
            {
                Log.Warn("[PortalService] GameManager.GiveCard static method not found");
                return false;
            }
        }

        try
        {
            _giveCardMethod.Invoke(null, new object[] { cardData, false });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[PortalService] GiveCard failed for '{cardData?.UniqueID}': {Log.ExceptionText(ex)}");
            return false;
        }
    }

    // ─── Shared Portal Hub — DA injection + handler registration ────────────

    private const string HubPlacedUid = "csffmfwportalplaced";

    /// <summary>
    /// <strong>Phase 5i-b (data-load).</strong> Injects one travel <c>DismantleCardAction</c>
    /// per registered mod world into the placed Portal Hub CT2 (<c>csffmfwportalplaced</c>) so
    /// each mod's button appears when the player clicks the portal. Idempotent — detects existing
    /// injected DAs by <c>DaKeyPrefix</c> and skips if already present.
    /// </summary>
    internal static void InjectHubTravelDAs()
    {
        var worlds = PortalRegistry.Worlds
            .Where(w => w.Index > 0 && (!string.IsNullOrEmpty(w.EnvironmentUID) || !string.IsNullOrEmpty(w.SacredSiteUID)))
            .ToList();
        if (worlds.Count == 0) return;

        // Startup-time existence validation — the shared Portal Hub card is a hardcoded
        // framework dependency (not authored per-mod), so a missing/malformed card means the
        // ENTIRE portal system is inert for every mod this session. Fail loudly (Log.Error),
        // not a Log.Warn buried deep in a per-load method.
        var hubCard = GameRegistry.GetByUid(HubPlacedUid) as CardData;
        if (hubCard == null)
        {
            Log.Error($"[PortalService.InjectHubTravelDAs] '{HubPlacedUid}' not found in registry — the Portal Hub System is disabled this session (no mod's world will get a travel button). This card ships with CSFFModFramework itself; if this fires, the framework's own CardData/Hub/csffmfwportalplaced.json failed to load.");
            return;
        }

        var daField = AccessTools.Field(hubCard.GetType(), "DismantleActions");
        if (daField == null)
        {
            Log.Error("[PortalService.InjectHubTravelDAs] DismantleActions field not found on CardData — Portal Hub System disabled this session (unexpected game-version field-shape change).");
            return;
        }

        var existing = daField.GetValue(hubCard) as IList;
        if (existing == null || existing.Count == 0)
        {
            Log.Error("[PortalService.InjectHubTravelDAs] Portal Hub has no existing DAs to use as a structural template — Portal Hub System disabled this session (csffmfwportalplaced.json shipped with an empty DismantleActions array).");
            return;
        }

        // Idempotency: skip if travel DAs already injected (look for our key prefix).
        var anField = AccessTools.Field(existing[0].GetType(), "ActionName");
        if (anField != null)
        {
            var lkFieldCheck = anField.GetValue(existing[0]) is { } ls0 ? AccessTools.Field(ls0.GetType(), "LocalizationKey") : null;
            foreach (var item in existing)
            {
                var lkVal = lkFieldCheck?.GetValue(anField.GetValue(item)) as string;
                if (lkVal != null && lkVal.StartsWith(DaKeyPrefix, StringComparison.Ordinal)) return;
            }
        }

        // Find a template DA with the expected shape (non-null ActionName with a resolvable
        // LocalizedString) instead of blindly trusting existing[0] — the original code shallow-
        // copies EVERY field from whichever DA happens to be first, so a future edit to
        // csffmfwportalplaced.json that reorders or prepends an unrelated action would silently
        // produce malformed travel DAs. Search for the first structurally-valid candidate and
        // fail loudly if none qualify, rather than proceeding with a bad template.
        object template = null;
        Type daType = null, lsType = null;
        foreach (var candidate in existing)
        {
            if (candidate == null) continue;
            var ct = candidate.GetType();
            var af = AccessTools.Field(ct, "ActionName");
            var nameObj = af?.GetValue(candidate);
            if (nameObj == null) continue;
            var lt = nameObj.GetType();
            if (AccessTools.Field(lt, "LocalizationKey") == null || AccessTools.Field(lt, "DefaultText") == null) continue;
            template = candidate; daType = ct; lsType = lt; anField = af;
            break;
        }
        if (template == null)
        {
            Log.Error($"[PortalService.InjectHubTravelDAs] none of the {existing.Count} existing DA(s) on '{HubPlacedUid}' have a structurally-valid ActionName to use as a template — Portal Hub System disabled this session.");
            return;
        }

        // Clear ReceivingCardChanges (travel DAs must NOT destroy the portal).
        var rcField       = AccessTools.Field(daType, "ReceivingCardChanges");
        var rcDefault     = rcField != null ? Activator.CreateInstance(rcField.FieldType) : null;
        var pcField       = AccessTools.Field(daType, "ProducedCards");
        var fieldIsList   = typeof(IList).IsAssignableFrom(daField.FieldType) && !daField.FieldType.IsArray;

        var allDas = new List<object>();
        foreach (var item in existing) allDas.Add(item);

        var lkFieldLS   = AccessTools.Field(lsType, "LocalizationKey");
        var dtFieldLS   = AccessTools.Field(lsType, "DefaultText");
        var pidFieldLS  = AccessTools.Field(lsType, "ParentObjectID");
        var dayCostFI   = AccessTools.Field(daType, "DaytimeCost");
        var miniTicksFI = daType.GetField("UseMiniTicks",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var world in worlds)
        {
            var newDa   = Activator.CreateInstance(daType);

            // Shallow-copy from template so enums / bools / audio refs get sensible defaults.
            foreach (var fi in daType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                fi.SetValue(newDa, fi.GetValue(template));

            // Fresh ActionName (must not share the template's LocalizedString).
            var newName = Activator.CreateInstance(lsType);
            pidFieldLS?.SetValue(newName, HubPlacedUid);
            lkFieldLS?.SetValue(newName, $"{DaKeyPrefix}{world.Index}");
            dtFieldLS?.SetValue(newName, $"Travel to {world.WorldName}");
            anField?.SetValue(newDa, newName);

            // Travel cost = 2 DTP (30 min).
            dayCostFI?.SetValue(newDa, 2);

            // UseMiniTicks = 2 (same timing mode as Pack Up).
            if (miniTicksFI != null)
                SetIntOrFloat(miniTicksFI, newDa, 2);

            // Clear portal-destroying behavior from the cloned Pack-Up DA.
            if (rcField != null && rcDefault != null)
                rcField.SetValue(newDa, rcDefault);
            if (pcField != null)
                pcField.SetValue(newDa, null);

            allDas.Add(newDa);
        }

        // Write back with correct container type (List<> in EA 0.65).
        if (fieldIsList)
        {
            var newList   = Activator.CreateInstance(typeof(List<>).MakeGenericType(daType));
            var addMethod = newList.GetType().GetMethod("Add");
            foreach (var da in allDas) addMethod.Invoke(newList, new[] { da });
            daField.SetValue(hubCard, newList);
        }
        else
        {
            var arr = Array.CreateInstance(daType, allDas.Count);
            for (int i = 0; i < allDas.Count; i++) arr.SetValue(allDas[i], i);
            daField.SetValue(hubCard, arr);
        }

        Log.Info($"[PortalService] injected {worlds.Count} travel DA(s) into Portal Hub ({string.Join(", ", worlds.Select(w => w.WorldName))})");
    }

    /// <summary>
    /// <strong>Run-start (<c>OnGMInitialized</c>).</strong> Registers one <see cref="ActionRouter"/>
    /// handler per mod world for the placed Portal Hub's injected travel DAs. Each handler resolves
    /// the world's CT4 env card (or CT8 loc card) and triggers environment travel. Idempotent via
    /// named handler deregistration on successive runs (ActionRouter.Register is additive within a
    /// process — each game start re-registers, which is safe because clone CT4 envs may vary).
    /// </summary>
    internal static void RegisterHubTravelHandlers()
    {
        int registered = 0;
        foreach (var world in PortalRegistry.Worlds)
        {
            if (world.Index == 0) continue;

            // Resolve travel target: prefer CT4 EnvironmentUID, fall back to SacredSiteUID (CT8 loc card).
            var targetUid  = !string.IsNullOrEmpty(world.EnvironmentUID) ? world.EnvironmentUID : world.SacredSiteUID;
            if (string.IsNullOrEmpty(targetUid)) continue;

            var targetCard = GameRegistry.GetByUid(targetUid) as CardData;
            if (targetCard == null)
            {
                Log.Warn($"[PortalService.RegisterHubTravelHandlers] '{world.WorldName}': target UID '{targetUid}' not found in registry — hub travel DA will not fire");
                continue;
            }

            var captured = targetCard;
            var capturedName = world.WorldName;
            ActionRouter.Register(new ActionHandler
            {
                Name            = $"PortalHub_{world.Index}_{world.WorldName}",
                CardUid         = HubPlacedUid,
                ActionKeyPrefix = $"{DaKeyPrefix}{world.Index}",
                Timing          = ActionTiming.AfterWrapped,
                After           = ctx => StartEnvironmentTravel(captured, ctx.Card, capturedName),
            });
            registered++;
            Log.Debug($"[PortalService] hub travel handler registered: world {world.Index} '{world.WorldName}' → '{targetUid}'");
        }
        if (registered > 0)
            Log.Info($"[PortalService] {registered} hub travel handler(s) registered");
    }

    // ─── Misc helpers ────────────────────────────────────────────────────────

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f;
        }
        return null;
    }

    private static void SetIntOrFloat(FieldInfo field, object obj, int value)
    {
        if (field == null) return;
        try
        {
            if (field.FieldType == typeof(float)) field.SetValue(obj, (float)value);
            else field.SetValue(obj, value);
        }
        catch { /* leave default */ }
    }
}
