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
///         world's injected DA (<see cref="RegisterHubTravelHandlers"/>); spawn
///         <c>csffmfw_hub_exit</c> ("Return to Portal") on arrival in a registered mod world, but
///         ONLY when that arrival was the result of an actual outbound Portal Hub trip this
///         session (<see cref="EnsureHubExitOnArrival"/>) — the exit is a temporary, session-
///         scoped card, not a permanent fixture of every registered world.</item>
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

    // csffmfw_hub_exit.json declares SpoilageTime (24h despawn timer, HasActionOnZero → destroy)
    // at FloatValue 96 — 1 in-game day = 96 daytime points (CLAUDE.md time-scale convention).
    // Keep this in sync with the JSON if the lifetime ever changes.
    private const float ExitLifetimeDtp = 96f;

    // ─── Exit card spawning ──────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="WorldMapInjector"/>'s mid-game env-arrival watch on every environment
    /// change. Spawns <c>csffmfw_hub_exit</c> ("Return to Portal") on the board — but ONLY when the
    /// current environment is one the player actually reached via an outbound Portal Hub trip THIS
    /// SESSION (i.e. <see cref="_returnEnvKeyByArrival"/> — populated by <see cref="StartEnvironmentTravel"/>
    /// the instant the player clicks a "Travel to [World]" button, keyed by the destination env's
    /// UID — already has an entry for <paramref name="envUid"/>). This deliberately mirrors the
    /// gate <see cref="StartReturnTravel"/> itself uses to decide where "Return to Portal" goes: the
    /// exit card only ever exists when clicking it would actually work.
    ///
    /// <para>Earlier versions of this method unconditionally auto-injected the exit into every
    /// registered mod world's <c>DefaultEnvCardDrops</c> (baked into the board on first-ever
    /// generation) and re-spawned it on ANY visit to a registered world, regardless of how the
    /// player got there. That meant the exit appeared even in saves that never touched a Portal Hub
    /// at all (e.g. simply walking into a mod's home-base world for the first time) — reported
    /// 2026-08-24 as "the exit appeared in a save where I never had or used a portal." The exit is
    /// meant to be a temporary, portal-trip-scoped card (like a tracks card), not a permanent
    /// fixture — see <see cref="ExitLifetimeDtp"/>'s 24h despawn timer, which already existed but
    /// only mattered for cards that should never have spawned in the first place.</para>
    ///
    /// Idempotent: no-ops if the card is already on the board (a legitimate portal-arrived exit
    /// that hasn't decayed yet, or a stale exit baked in by a pre-fix save — either way, left alone
    /// to decay naturally via its own SpoilageTime timer rather than being force-removed here).
    /// </summary>
    internal static void EnsureHubExitOnArrival(string envUid)
    {
        if (string.IsNullOrEmpty(envUid)) return;

        bool isRegisteredWorld = false;
        foreach (var world in PortalRegistry.Worlds)
        {
            if (world.Index == 0) continue;
            if (string.Equals(world.EnvironmentUID, envUid, StringComparison.OrdinalIgnoreCase))
            { isRegisteredWorld = true; break; }
        }
        if (!isRegisteredWorld) return;

        foreach (var c in GameQuery.CardsInPlayerEnv())
        {
            if (!string.Equals(CardUtil.GetCardUniqueId(c), HubExitUid, StringComparison.OrdinalIgnoreCase))
                continue;
            Log.Info($"[PortalService] hub_exit already present at '{envUid}' — SpoilageTime={CardUtil.GetDurability(c, "SpoilageTime"):0.#}/{ExitLifetimeDtp:0.#}");
            return; // already present — nothing to do
        }

        if (!_returnEnvKeyByArrival.ContainsKey(envUid))
        {
            Log.Debug($"[PortalService] hub_exit NOT spawned at '{envUid}' — no outbound Portal Hub trip recorded this session for this arrival (player didn't reach this world via a portal)");
            return;
        }

        Log.Info($"[PortalService] hub_exit missing on arrival at '{envUid}' — spawning (portal arrival confirmed this session)");
        SpawnService.Spawn(HubExitUid);

        // GameManager.GiveCard returns void in this game version, so SpawnService.Spawn cannot
        // hand back the new instance to apply overrides directly — and GiveCard-spawned cards are
        // NOT guaranteed to carry their CardData JSON's FloatValue (confirmed pattern: companion/
        // perk-AddedCards spawns observed starting at 0 for ALL durability stats regardless of
        // JSON default — see reference_givecard_postfix_stat_init). csffmfw_hub_exit's 24h despawn
        // timer (SpoilageTime, HasActionOnZero → destroy) would self-destruct on the very next
        // decay tick if it spawned at 0. Fix synchronously, in the same call — GiveCard is a plain
        // void method (not a coroutine), so the spawned card is already in AllCards by the time it
        // returns, and this all runs before any DTP tick can evaluate the zero-check (see
        // reference_onzero_destroy_races_dtp_tick — a REACTIVE tick-hooked fix loses that race;
        // this synchronous one does not).
        foreach (var c in GameQuery.CardsInPlayerEnv())
        {
            if (!string.Equals(CardUtil.GetCardUniqueId(c), HubExitUid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CardUtil.SetDurability(c, "SpoilageTime", ExitLifetimeDtp))
                Log.Info($"[PortalService] hub_exit spawned at '{envUid}' — SpoilageTime initialized to {ExitLifetimeDtp:0.#} (24h despawn timer armed)");
            else
                Log.Warn($"[PortalService] hub_exit spawned at '{envUid}' but SetDurability(SpoilageTime) failed — despawn timer may be stuck at 0 (risk of instant self-destruct)");
            return;
        }
        Log.Warn($"[PortalService] hub_exit spawn requested at '{envUid}' but the new instance was not found in CardsInPlayerEnv() immediately after — despawn timer could not be initialized");
    }

    // ─── Environment travel helper ──────────────────────────────────────────

    private static FieldInfo _nextEnvironmentField;

    /// <summary>
    /// Vanilla invariant (confirmed via decompile, <c>GameManager.cs</c>): every travel call site
    /// that adds a CardType.Environment card wholesale-reassigns <c>NextEnvironment = travel</c>
    /// (a freshly built <c>EnvID</c>) IMMEDIATELY before calling <c>AddCard</c> — e.g.
    /// <c>ProduceCards</c>'s <c>NextEnvironment = travel; AddCard(NextEnvironment.EnvCard, ...)</c>.
    /// <c>AddCard</c>'s own CardTypes.Environment branch only does a PARTIAL update
    /// (<c>NextEnvironment.SetMainEnvCard(_Data)</c>, which mutates <c>MainEnvCard</c> in place but
    /// leaves <c>NextEnvironment.ParentEnvs</c> — and its cached <c>EnvDictKey</c> — untouched).
    /// <c>EnvDictKey.Generate</c> factors <c>ParentEnvs</c> into the int key REGARDLESS of whether
    /// the destination is instanced (unlike the string key, which short-circuits for non-instanced
    /// envs) — so if the player's last real environment carried a parent-env chain (e.g. they were
    /// in an instanced interior room), that stale chain rides along into the new
    /// <c>NextEnvironment</c>'s key, producing a malformed <c>EnvironmentsData</c> lookup key that
    /// doesn't match the destination's real persisted board. Both of this class's reflection-based
    /// travel paths (<see cref="TryStartAddCardFromSource"/> and <see cref="GiveCardViaReflection"/>)
    /// call <c>AddCard</c>/<c>GiveCard</c> directly and skip the wholesale reassignment vanilla
    /// always does first — root cause of the "two maps overlap" / board-corruption report
    /// (2026-08-24 player report: portal arrival showed duplicate map content, later save-reload
    /// lost unrelated board state). Fix: replicate the vanilla wholesale reassignment here, for any
    /// CardType.Environment destination, before invoking either reflected method.
    ///
    /// <para><paramref name="presetEnvId"/> covers the residual instanced-interior return case: a
    /// plain <c>new EnvID(cardData)</c> nulls itself (ctor guard in <c>EnvID.cs</c>) when
    /// <c>cardData</c> is itself an instanced env (Cabin/Cellar/Coop/Enclosure/mine/attic/...) with
    /// no accompanying parent chain. When the caller already has a correctly-reconstructed
    /// <c>EnvID</c> (built from <c>new EnvID(recordedStringDictionnaryKey)</c> — see
    /// <see cref="StartReturnTravel"/>), it's passed here and used verbatim instead of being rebuilt
    /// from the bare card.</para>
    /// </summary>
    private static void PrepareNextEnvironment(object gmInstance, Type gmType, CardData cardData, EnvID? presetEnvId = null)
    {
        if (cardData == null || cardData.CardType != CardTypes.Environment)
            return; // legacy CT8 SacredSiteUID fallback — not a real env transition, leave untouched

        try
        {
            _nextEnvironmentField ??= AccessTools.Field(gmType, "NextEnvironment");
            if (_nextEnvironmentField == null)
            {
                Log.Warn("[PortalService] GameManager.NextEnvironment field not found — env transition may carry a stale parent-environment chain from the player's last real transition (board-overlap risk).");
                return;
            }

            EnvID envId;
            if (presetEnvId.HasValue && !presetEnvId.Value.IsNull)
            {
                envId = presetEnvId.Value;
                Log.Info($"[PortalService] reset GameManager.NextEnvironment to preset EnvID for '{cardData.UniqueID}' ({envId.ParentEnvCount} parent env(s) restored from recorded return key) before environment-card AddCard/GiveCard");
            }
            else
            {
                envId = new EnvID(cardData);
                Log.Info($"[PortalService] reset GameManager.NextEnvironment to '{cardData.UniqueID}' (fresh EnvID, no stale ParentEnvs) before environment-card AddCard/GiveCard");
            }

            _nextEnvironmentField.SetValue(gmInstance, envId);
        }
        catch (Exception ex)
        {
            Log.Warn($"[PortalService] PrepareNextEnvironment failed for '{cardData.UniqueID}': {Log.ExceptionText(ex)}");
        }
    }

    private static MethodInfo _addCardFromSourceMethod;
    private static bool _addCardLookupDone;

    // The source-card AddCard overload: (CardData _Data, InGameCardBase _FromCard, bool _InCurrentEnv,
    // SpecialDrop, 4 x reference, bool _UseDefaultInventory, SpawningLiquid, Vector2Int _Tick,
    // EnvDictKey _TravelTargetKey, ...). Every position TryStartAddCardFromSource writes is checked,
    // so a reorder fails the match (and warns once) instead of invoking with mistyped arguments.
    private static bool HasAddCardFromSourceShape(ParameterInfo[] ps) =>
        ps.Length >= 12
        && ps[0].ParameterType == typeof(CardData)
        && ps[1].ParameterType.Name == "InGameCardBase"
        && ps[2].ParameterType == typeof(bool)
        && ps[3].ParameterType.IsEnum
        && ps.Skip(4).Take(4).All(p => !p.ParameterType.IsValueType)
        && ps[8].ParameterType == typeof(bool)
        && ps[9].ParameterType == typeof(SpawningLiquid)
        && ps[10].ParameterType == typeof(Vector2Int)
        && ps[11].ParameterType == typeof(EnvDictKey);

    // Session-scoped: for each mod-hub environment the player has arrived at via an outbound
    // Portal Hub trip, the FULL StringDictionnaryKey of the environment they departed from (NOT
    // the bare UID — a departure from an instanced interior, e.g. Cabin/Cellar/Coop/Enclosure,
    // encodes its ParentEnvs chain in this key; the bare UID form loses that chain, and
    // rebuilding via `new EnvID(CardData)` on return then nulls itself for an instanced target —
    // see GameQuery.CurrentEnvironmentStringDictionaryKey). Keyed by arrival env UID (NOT a
    // single shared field) — a player who visits two different mod hubs in the same session
    // must get routed back to EACH hub's own departure point, not whichever was visited most
    // recently. Backs the "Return to Portal" button on csffmfw_hub_exit — see StartReturnTravel.
    // Cleared at every run start (RegisterHubExitHandler): before 2.26.9 it lived for the whole
    // process, so a trip in save A spawned an exit in save B that routed to save A's env key. After
    // a reload or restart the return point comes from the save instead (TryFindSavedPortalEnv).
    private static readonly Dictionary<string, string> _returnEnvKeyByArrival = new();

    private static void StartEnvironmentTravel(CardData cardData, object sourceCard, string worldName, bool recordReturn = false, EnvID? presetEnvId = null)
    {
        if (cardData == null)
            return;

        if (recordReturn)
        {
            var currentUid = GameQuery.CurrentEnvironmentUniqueId;
            if (!string.IsNullOrEmpty(currentUid) && currentUid != cardData.UniqueID)
            {
                // Record the FULL StringDictionnaryKey, not the bare UID — a departure from an
                // instanced interior (Cabin/Cellar/Coop/Enclosure/...) encodes its ParentEnvs chain
                // in this key; the bare UID form loses it, which then nulls the rebuilt EnvID on
                // return (new EnvID(CardData)'s ctor guard for instanced targets). Identical to the
                // UID for a non-instanced departure — zero behavior change there.
                var currentKey = GameQuery.CurrentEnvironmentStringDictionaryKey;
                if (!string.IsNullOrEmpty(currentKey))
                {
                    _returnEnvKeyByArrival[cardData.UniqueID] = currentKey;
                    // Info until T2.63/T2.77 are diagnosed — confirms the dictionary write fires
                    // per-hub-visit in a normal player log. Click-frequency, not a hot path.
                    Log.Info($"[PortalService] recorded return env key '{currentKey}' for arrival '{cardData.UniqueID}' before traveling to '{worldName}'");
                }
                else
                {
                    Log.Warn($"[PortalService] could not read current environment's StringDictionnaryKey (uid='{currentUid}') — 'Return to Portal' will not work for arrival '{cardData.UniqueID}'");
                }
            }
        }

        Log.Info($"[PortalService] hub travel button clicked: '{worldName}' -> '{cardData.UniqueID}'");

        // GameManager.GiveCard(card, false) uses the cheat/no-source AddCard path. For CT4
        // environment travel that can leave CurrentEnvironment unset when the action started from
        // a portal card, which then nullrefs in CalculateEnvironmentWeight. Starting the private
        // AddCard overload with the clicked portal as _FromCard mirrors the path used by vanilla
        // travel actions and keeps the environment transition state coherent.
        if (TryStartAddCardFromSource(cardData, sourceCard, presetEnvId))
        {
            Log.Info($"[PortalService] hub travel to '{worldName}' started via source-card AddCard path");
            return;
        }

        if (GiveCardViaReflection(cardData, presetEnvId))
            Log.Info($"[PortalService] hub travel to '{worldName}' started via GiveCard reflection fallback");
        else
            Log.Error($"[PortalService] hub travel to '{worldName}' FAILED — both AddCard-from-source and GiveCard reflection paths failed");
    }

    private static bool TryStartAddCardFromSource(CardData cardData, object sourceCard, EnvID? presetEnvId = null)
    {
        if (sourceCard == null)
            return false;

        try
        {
            var gmInstance = CardUtil.GetGameManagerInstance();
            if (gmInstance == null)
                return false;

            var gmType = gmInstance.GetType();
            PrepareNextEnvironment(gmInstance, gmType, cardData, presetEnvId);
            if (!_addCardLookupDone)
            {
                _addCardLookupDone = true;
                // Match on the twelve leading parameters, which have been stable across game versions;
                // the tail is filled per parameter below. An exact-arity match broke on EA 0.68b, which
                // inserted InGameNPCOrPlayer _User after _TravelTargetKey (16 -> 17 parameters), and
                // sent every portal click down the GiveCard fallback.
                _addCardFromSourceMethod = gmType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "AddCard" && HasAddCardFromSourceShape(m.GetParameters()));
                if (_addCardFromSourceMethod == null)
                {
                    var seen = gmType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                        .Where(m => m.Name == "AddCard")
                        .Select(m => "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
                    Log.Warn("[PortalService] GameManager.AddCard(CardData, InGameCardBase, bool, ...) not found; "
                           + "portal travel will use the GiveCard fallback. AddCard overloads seen: " + string.Join(" | ", seen));
                }
            }

            if (_addCardFromSourceMethod == null)
                return false;

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

            var args = new object[parameters.Length];
            args[0] = cardData;
            args[1] = sourceCard;
            args[2] = true;                           // _InCurrentEnv
            args[3] = specialDropNone;                // _DropInSpecialPlace
            // 4-7: transferred durabilities, inherited ingredients, flavours, spices -> null
            args[8] = true;                           // _UseDefaultInventory
            args[9] = SpawningLiquid.DefaultLiquid;   // _WithLiquid
            args[10] = new Vector2Int(currentTick, 0); // _Tick
            args[11] = default(EnvDictKey);           // _TravelTargetKey
            for (int i = 12; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(InGameNPCOrPlayer))
                    args[i] = InGameNPCOrPlayer.Null; // what vanilla's own GiveCard passes
                else if (p.HasDefaultValue)
                    args[i] = p.DefaultValue;         // _WithSpecialSlotInfo null, _MoveView true, ...
                else
                    args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            }

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

    private static bool GiveCardViaReflection(CardData cardData, EnvID? presetEnvId = null)
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
            // Static GiveCard funnels into the same AddCard overload as the source-card path
            // (confirmed via decompile) — it needs the same NextEnvironment prep for a CT4 target.
            var gmInstance = CardUtil.GetGameManagerInstance();
            if (gmInstance != null)
                PrepareNextEnvironment(gmInstance, gmInstance.GetType(), cardData, presetEnvId);

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

    // The Arcane Wayfinder perk and the Portal Kit blueprint lead nowhere unless some mod registers
    // a portal world (PortalRegistry always holds vanilla Fantasy Forest as world 0). Owner decision
    // 2026-09-25: keep both out of character creation and the journal in that case. PerkInjector and
    // BlueprintInjector ask this; both run after MapModLoader has filled the registry.
    private const string WayfinderPerkUid = "csffmfwperkwayfinder";
    private const string PortalKitBlueprintUid = "csffmfw_bp_portal_kit";

    internal static bool IsPortalContentHidden(string uid) =>
        PortalRegistry.Worlds.Count <= 1
        && (string.Equals(uid, WayfinderPerkUid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uid, PortalKitBlueprintUid, StringComparison.OrdinalIgnoreCase));

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

    // Handlers registered by the last RegisterHubTravelHandlers() call. OnGMInitialized fires on
    // EVERY run start within a process (not once — see SealableGateService.Initialize's identical
    // note), including, per player report 2026-08-27, apparently whenever the game reloads the
    // save while the player is inside an instanced construction interior (Env_*ConstructionStartingPoint*
    // cabin/cellar/etc — 3 AutoSave.json reloads observed for 3 OnGMInitialized fires in one
    // session). ActionRouter.Register is purely additive (no name-based dedup), so without this
    // deregister-before-register step, each re-fire piled a fresh duplicate ActionHandler onto
    // every Portal Hub button on top of the old one(s) — one click then invoked
    // StartEnvironmentTravel N times synchronously in the SAME dispatch, each call reassigning
    // GameManager.NextEnvironment and starting its own AddCard coroutine, racing the others. That's
    // the "two maps overlap" corruption PrepareNextEnvironment's doc comment already describes,
    // observed as: board shows the wrong environment's cards while the location card still reads
    // the clicked destination, and a followed NPC left stranded in the environment the player
    // departed from. Same fix shape as CompanionService.Reset() — unregister the previous batch,
    // not just skip re-registration, since a later run's resolved clone-env target cards can
    // legitimately differ from an earlier run's (see the removed comment this replaces).
    private static readonly List<ActionHandler> _travelHandlers = new();

    /// <summary>
    /// <strong>Run-start (<c>OnGMInitialized</c>).</strong> Registers one <see cref="ActionRouter"/>
    /// handler per mod world for the placed Portal Hub's injected travel DAs. Each handler resolves
    /// the world's CT4 env card (or CT8 loc card) and triggers environment travel.
    /// </summary>
    internal static void RegisterHubTravelHandlers()
    {
        foreach (var handler in _travelHandlers) ActionRouter.Unregister(handler);
        _travelHandlers.Clear();

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
            var handler = ActionRouter.Register(new ActionHandler
            {
                Name            = $"PortalHub_{world.Index}_{world.WorldName}",
                CardUid         = HubPlacedUid,
                ActionKeyPrefix = $"{DaKeyPrefix}{world.Index}",
                Timing          = ActionTiming.AfterWrapped,
                After           = ctx => StartEnvironmentTravel(captured, ctx.Card, capturedName, recordReturn: true),
            });
            if (handler != null) _travelHandlers.Add(handler);
            registered++;
            Log.Debug($"[PortalService] hub travel handler registered: world {world.Index} '{world.WorldName}' → '{targetUid}'");
        }
        if (registered > 0)
            Log.Info($"[PortalService] {registered} hub travel handler(s) registered");
    }

    // ─── "Return to Portal" handler ──────────────────────────────────────────

    private const string HubExitActionKey = "CSFFMFW_HubExit_DA_Exit";

    // Same duplicate-registration hazard as _travelHandlers above — one handler, so a plain
    // nullable field instead of a list.
    private static ActionHandler _hubExitHandler;

    /// <summary>
    /// <strong>Run-start (<c>OnGMInitialized</c>).</strong> Registers the <see cref="ActionRouter"/>
    /// handler for csffmfw_hub_exit's "Exit" DA. The card's JSON originally used vanilla
    /// <c>TravelToPreviousEnv</c>, but that only works when the CURRENT environment has a
    /// <c>ParentEnvs</c> chain (i.e. is an instanced env reached by an actual travel transition)
    /// — every registered mod hub (cmcEnvVillage, actTinCaveEnv, hfEnvForagingPath, ...) is a
    /// normal non-instanced WorldMap node, so <c>WillProduceCards()</c> always evaluated false
    /// there and the "Return to Portal" button never rendered at all (DaytimeCost is 0, so
    /// nothing else made WillHaveAnEffect() true either — see csffmfw_hub_exit.json's AlwaysShow).
    /// This handler drives the return trip explicitly via the env UID recorded by
    /// <see cref="StartEnvironmentTravel"/> on the way out.
    /// </summary>
    internal static void RegisterHubExitHandler()
    {
        // Runs once per run start, so this is where the previous run's recorded trips are dropped.
        _returnEnvKeyByArrival.Clear();

        if (_hubExitHandler != null) ActionRouter.Unregister(_hubExitHandler);

        _hubExitHandler = ActionRouter.Register(new ActionHandler
        {
            Name            = "PortalHub_Exit",
            CardUid         = HubExitUid,
            ActionKeyPrefix = HubExitActionKey,
            Timing          = ActionTiming.AfterWrapped,
            After           = ctx => StartReturnTravel(ctx.Card),
        });
        Log.Info("[PortalService] hub exit return handler registered");
    }

    private static void StartReturnTravel(object sourceCard)
    {
        var currentUid = GameQuery.CurrentEnvironmentUniqueId;
        EnvID returnEnvId;
        if (!string.IsNullOrEmpty(currentUid)
            && _returnEnvKeyByArrival.TryGetValue(currentUid, out var returnEnvKey)
            && !string.IsNullOrEmpty(returnEnvKey))
        {
            // Reconstruct via the string-key ctor (the exact round-trip vanilla uses at save/load for
            // CurrentEnvironmentKey, EnvID.cs) rather than looking the UID up and rebuilding with
            // `new EnvID(CardData)` — that ctor nulls itself for an instanced destination (no parent
            // chain available), which is exactly the corruption class this fix closes.
            try
            {
                returnEnvId = new EnvID(returnEnvKey);
            }
            catch (Exception ex)
            {
                Log.Warn($"[PortalService] 'Return to Portal': failed to reconstruct EnvID from recorded key '{returnEnvKey}': {Log.ExceptionText(ex)}");
                ShowNoReturnRecordedMessage();
                return;
            }

            if (returnEnvId.IsNull || returnEnvId.EnvCard == null)
            {
                Log.Warn($"[PortalService] 'Return to Portal': recorded env key '{returnEnvKey}' did not resolve to a valid card (renamed/removed since departure?) — no-op.");
                ShowNoReturnRecordedMessage();
                return;
            }
        }
        else if (TryFindSavedPortalEnv(out returnEnvId))
        {
            // No trip recorded this run (the save was loaded after the trip): the return point is
            // where the save says the placed Portal Hub stands.
            Log.Info($"[PortalService] 'Return to Portal': no trip recorded this run for '{currentUid ?? "(null)"}'; "
                + $"returning to the placed Portal Hub found in the save at '{returnEnvId.StringDictionnaryKey}'");
        }
        else
        {
            Log.Warn($"[PortalService] 'Return to Portal' clicked in '{currentUid ?? "(null)"}' with no trip recorded "
                + $"this run (recorded arrivals: [{string.Join(", ", _returnEnvKeyByArrival.Keys)}]) and no placed "
                + "Portal Hub found in the save — no-op.");
            ShowNoReturnRecordedMessage();
            return;
        }

        StartEnvironmentTravel(returnEnvId.EnvCard, sourceCard, "Portal", presetEnvId: returnEnvId);
    }

    /// <summary>
    /// Finds the environment where this save's placed Portal Hub stands, for a "Return to Portal"
    /// click after the save was reloaded (or the game restarted) since the outbound trip. Scans the
    /// game's saved environment data (<c>GameManager.EnvironmentsData</c>, which the save writes and
    /// loads) for <see cref="HubPlacedUid"/>, skipping the current environment. With several placed
    /// hubs it takes the environment the player left most recently (highest
    /// <c>LastUpdatedTick</c>), which is the one the trip started from.
    /// </summary>
    private static bool TryFindSavedPortalEnv(out EnvID envId)
    {
        envId = EnvID.Empty;
        var gm = MBSingleton<GameManager>.Instance;
        if (!gm || gm.EnvironmentsData == null) return false;

        var currentKey = gm.CurrentEnvironment.DictionnaryKey;
        int bestTick = int.MinValue;
        EnvDictKey bestKey = default;
        bool found = false;
        foreach (var kv in gm.EnvironmentsData)
        {
            if (kv.Value == null || kv.Key.Equals(currentKey)) continue;
            var cards = kv.Value.GetRegularCards;
            if (cards == null) continue;
            foreach (var card in cards)
            {
                var id = card?.CardID;
                if (string.IsNullOrEmpty(id)) continue;
                // SaveID writes "uid(Name)"; accept the bare UID too.
                if (!id.StartsWith(HubPlacedUid, StringComparison.Ordinal)) continue;
                if (id.Length != HubPlacedUid.Length && id[HubPlacedUid.Length] != '(') continue;
                if (!found || kv.Value.LastUpdatedTick > bestTick)
                {
                    bestTick = kv.Value.LastUpdatedTick;
                    bestKey = kv.Key;
                    found = true;
                }
                break;
            }
        }
        if (!found) return false;

        try
        {
            envId = new EnvID(bestKey);
        }
        catch (Exception ex)
        {
            Log.Warn($"[PortalService] saved Portal Hub environment could not be rebuilt: {Log.ExceptionText(ex)}");
            return false;
        }
        return !envId.IsNull && envId.EnvCard != null;
    }

    /// <summary>
    /// The no-op branches of <see cref="StartReturnTravel"/> previously only wrote a Log.Warn —
    /// invisible to the player, so the exit card felt silently broken (2026-08-24 player report:
    /// "used the portal exit but it won't work", clicking produced zero feedback). Surfaces the
    /// same explanation via vanilla's own "can't do that right now" popup
    /// (<c>GraphicsManager.Instance.MessagePopup</c> — same mechanism vanilla itself uses, e.g.
    /// <c>GameManager.cs</c>'s NPC-deletion confirmation).
    /// </summary>
    private static void ShowNoReturnRecordedMessage()
    {
        try
        {
            var popup = GraphicsManager.Instance?.MessagePopup;
            if (popup == null)
            {
                Log.Warn("[PortalService] GraphicsManager.Instance.MessagePopup unavailable — could not show 'Return to Portal' no-op feedback to player.");
                return;
            }
            popup.Setup(
                "Return to Portal",
                "This exit doesn't know where to send you back: no placed Portal Hub was found in this save. Place a Portal Hub and travel out through it, and this exit will bring you back to it.",
                null);
        }
        catch (Exception ex)
        {
            Log.Warn($"[PortalService] ShowNoReturnRecordedMessage failed: {Log.ExceptionText(ex)}");
        }
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
        catch (Exception ex) { Log.Debug($"[PortalService] SetIntOrFloat: set failed for field '{field.Name}' — leaving default: {ex.GetType().Name} {ex.Message}"); }
    }
}
