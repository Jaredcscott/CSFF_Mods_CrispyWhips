using System.Reflection;

namespace Utility_Belt;

/// <summary>
/// Utility Belt's side of the public seams in Repeat Action and Quick Transfer (plan section 2.2 R9): Repeat Action's
/// <c>ActionPatch.OnRequirementsUnmet</c> event (Auto-Pull Materials and Equip Spare Tool On Break each subscribe their
/// own handler), its iteration events (Store Products In), and Quick Transfer's <c>QuickTransferPatch.TryClickMoveOne(object)</c>
/// with its <c>LastRefusal</c>. Found by reflection on whichever copy BepInEx loaded, through
/// <c>Chainloader.PluginInfos[guid].Instance</c>'s assembly (plan 1.1), so Utility Belt takes no compile-time reference
/// to either module and declares no [BepInDependency] (a cycle would load neither). Resolution is lazy, from the first
/// Update, never Awake: a plugin that loads after this one is absent from PluginInfos during this one's Awake (W0.1d).
/// A module or member that is not there is cached as absent and said once per cause, and the feature that needs it
/// stays off. The mirror of the modules' own UtilityBeltLockBridge.
/// </summary>
internal static class ModuleSeams
{
    internal const string RepeatActionGuid = "crispywhips.repeat_action";
    internal const string QuickTransferGuid = "crispywhips.quick_transfer";
    private const string RunHooksTypeName = "Repeat_Action.Patcher.ActionPatch";
    private const string UnmetEventName = "OnRequirementsUnmet";
    private const string IterationStartingEventName = "OnIterationStarting";
    private const string IterationDoneEventName = "OnIterationDone";
    private const string QuickTransferPatchTypeName = "Quick_Transfer.Patcher.QuickTransferPatch";
    private const string MoveOneMethodName = "TryClickMoveOne";
    private const string LastRefusalPropertyName = "LastRefusal";

    private static readonly HashSet<string> _warned = new();

    private static bool _raResolved;
    private static EventInfo _unmetEvent;
    private static Delegate _unmetDelegate;
    private static bool _unmetSubscribed;
    private static Delegate _spareDelegate;
    private static bool _spareSubscribed;

    /// <summary>The args class Repeat Action raises with, and the fields Utility Belt reads or sets on it.</summary>
    internal static FieldInfo UnmetSite, UnmetIteration, UnmetMissingCards, UnmetMissingTags, UnmetAction, UnmetReceiving, UnmetGiven, UnmetRetry;

    private static bool _iterResolved;
    private static EventInfo _startingEvent;
    private static EventInfo _doneEvent;
    private static Delegate _startingDelegate;
    private static Delegate _doneDelegate;
    private static bool _iterSubscribed;

    private static bool _qtResolved;
    private static Func<object, bool> _moveOne;
    private static PropertyInfo _lastRefusal;

    internal static bool QuickTransferPresent
    {
        get
        {
            EnsureQuickTransfer();
            return _moveOne != null;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning("[UB] " + message);
        }
    }

    private static Type FindModuleType(string guid, string displayName, string typeName, string feature)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null)
        {
            // Unusual (Utility Belt bundles both modules) but not an error: the feature simply has nothing to work with.
            Plugin.Logger.LogDebug($"[UB] {displayName} ({guid}) is not loaded; {feature} stays off.");
            return null;
        }

        if (info.Instance == null)
        {
            WarnOnce(guid + ":instance", $"{displayName} is listed but has no plugin instance (it failed to load?); {feature} stays off.");
            return null;
        }

        var type = info.Instance.GetType().Assembly.GetType(typeName, false);
        if (type == null)
        {
            WarnOnce(guid + ":type", $"{typeName} was not found in the loaded {displayName} {info.Metadata.Version} (an older build?); {feature} stays off.");
        }

        return type;
    }

    /// <summary>Resolve Repeat Action's requirements-unmet event once. Called from Plugin.Update; a no-op after the first call.</summary>
    private static void EnsureRepeatAction()
    {
        if (_raResolved)
        {
            return;
        }

        _raResolved = true;
        const string feature = "Auto-Pull Materials and Equip Spare Tool On Break";
        try
        {
            var hooks = FindModuleType(RepeatActionGuid, "Repeat Action", RunHooksTypeName, feature);
            if (hooks == null)
            {
                return;
            }

            var evt = hooks.GetEvent(UnmetEventName, BindingFlags.Public | BindingFlags.Static);
            var handlerType = evt?.EventHandlerType;
            var argsType = handlerType != null && handlerType.IsGenericType && handlerType.GetGenericArguments().Length == 1
                ? handlerType.GetGenericArguments()[0]
                : null;
            if (argsType == null)
            {
                WarnOnce("ra:event", $"Repeat Action has no {RunHooksTypeName}.{UnmetEventName} event of one argument (an older build?); {feature} stays off.");
                return;
            }

            FieldInfo Field(string name, Type expected)
            {
                var f = argsType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                return f != null && f.FieldType == expected ? f : null;
            }

            UnmetSite = Field("Site", typeof(string));
            UnmetIteration = Field("Iteration", typeof(int));
            UnmetMissingCards = Field("MissingCards", typeof(List<CardData>));
            UnmetMissingTags = Field("MissingTags", typeof(List<CardTag>));
            UnmetAction = Field("Action", typeof(CardAction));
            UnmetReceiving = Field("ReceivingCard", typeof(InGameCardBase));
            UnmetGiven = Field("GivenCard", typeof(InGameCardBase));
            UnmetRetry = Field("RetryRequested", typeof(bool));
            if (UnmetSite == null || UnmetIteration == null || UnmetMissingCards == null || UnmetMissingTags == null
                || UnmetAction == null || UnmetReceiving == null || UnmetGiven == null || UnmetRetry == null)
            {
                WarnOnce("ra:args", $"{argsType.FullName} lacks a field Utility Belt reads (renamed?); {feature} stays off.");
                return;
            }

            var handler = typeof(AutoPull).GetMethod(nameof(AutoPull.OnRequirementsUnmet), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            // A method taking object binds to Action<RequirementsUnmetArgs> (parameter contravariance), so no type of
            // Repeat Action's is named here.
            _unmetDelegate = Delegate.CreateDelegate(handlerType, handler);
            var spareHandler = typeof(SpareTool).GetMethod(nameof(SpareTool.OnRequirementsUnmet), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            _spareDelegate = Delegate.CreateDelegate(handlerType, spareHandler);
            _unmetEvent = evt;
            Plugin.Logger.LogDebug("[UB] Repeat Action's requirements-unmet event found.");
        }
        catch (Exception ex)
        {
            _unmetEvent = null;
            _unmetDelegate = null;
            _spareDelegate = null;
            WarnOnce("ra:lookup", $"Looking up Repeat Action's requirements-unmet event threw ({ex.GetType().Name}: {ex.Message}); {feature} stays off.");
        }
    }

    /// <summary>Resolve Repeat Action's iteration-window events once (plan C2).</summary>
    private static void EnsureIterationEvents()
    {
        if (_iterResolved)
        {
            return;
        }

        _iterResolved = true;
        const string feature = "Store Products In";
        try
        {
            var hooks = FindModuleType(RepeatActionGuid, "Repeat Action", RunHooksTypeName, feature);
            if (hooks == null)
            {
                return;
            }

            var starting = hooks.GetEvent(IterationStartingEventName, BindingFlags.Public | BindingFlags.Static);
            var done = hooks.GetEvent(IterationDoneEventName, BindingFlags.Public | BindingFlags.Static);
            if (starting == null || done == null)
            {
                WarnOnce("ra:iteration", $"Repeat Action has no {RunHooksTypeName}.{IterationStartingEventName}/{IterationDoneEventName} events (an older build?); {feature} stays off.");
                return;
            }

            var startingHandler = typeof(StoreProducts).GetMethod(nameof(StoreProducts.OnIterationStarting), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var doneHandler = typeof(StoreProducts).GetMethod(nameof(StoreProducts.OnIterationDone), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            // Both events carry plain int, bool and string parameters, so no type of Repeat Action's is named here. A
            // signature that differs makes CreateDelegate throw, which lands in the catch below as "stays off".
            _startingDelegate = Delegate.CreateDelegate(starting.EventHandlerType, startingHandler);
            _doneDelegate = Delegate.CreateDelegate(done.EventHandlerType, doneHandler);
            _startingEvent = starting;
            _doneEvent = done;
            Plugin.Logger.LogDebug("[UB] Repeat Action's iteration events found.");
        }
        catch (Exception ex)
        {
            _startingEvent = null;
            _doneEvent = null;
            WarnOnce("ra:iteration-lookup", $"Looking up Repeat Action's iteration events threw ({ex.GetType().Name}: {ex.Message}); {feature} stays off.");
        }
    }

    /// <summary>Keeps the iteration-event subscription equal to <paramref name="wanted"/> (the Store Products In option), so
    /// with it off nothing is subscribed and Repeat Action runs exactly as its standalone release.</summary>
    internal static void TickIteration(bool wanted)
    {
        if (wanted)
        {
            EnsureIterationEvents();
        }

        if (wanted == _iterSubscribed || _startingEvent == null || _doneEvent == null)
        {
            return;
        }

        try
        {
            if (wanted)
            {
                _startingEvent.AddEventHandler(null, _startingDelegate);
                _doneEvent.AddEventHandler(null, _doneDelegate);
            }
            else
            {
                _startingEvent.RemoveEventHandler(null, _startingDelegate);
                _doneEvent.RemoveEventHandler(null, _doneDelegate);
            }

            _iterSubscribed = wanted;
            Plugin.Logger.LogDebug($"[UB] Store Products In {(wanted ? "subscribed to" : "unsubscribed from")} Repeat Action's iteration events.");
        }
        catch (Exception ex)
        {
            _startingEvent = null;
            _doneEvent = null;
            WarnOnce("ra:iteration-subscribe", $"Subscribing to Repeat Action's iteration events threw ({ex.GetType().Name}: {ex.Message}); Store Products In stays off.");
        }
    }

    /// <summary>Resolve Quick Transfer's one-card click replay once.</summary>
    private static void EnsureQuickTransfer()
    {
        if (_qtResolved)
        {
            return;
        }

        _qtResolved = true;
        const string feature = "Auto-Pull Materials' click replay (cards are moved by the game's own MoveCardToSlot instead)";
        try
        {
            var patch = FindModuleType(QuickTransferGuid, "Quick Transfer", QuickTransferPatchTypeName, feature);
            if (patch == null)
            {
                return;
            }

            var method = patch.GetMethod(MoveOneMethodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object) }, null);
            if (method == null || method.ReturnType != typeof(bool))
            {
                WarnOnce("qt:method", $"{QuickTransferPatchTypeName}.{MoveOneMethodName}(object) was not found (an older build?); {feature}.");
                return;
            }

            _lastRefusal = patch.GetProperty(LastRefusalPropertyName, BindingFlags.Public | BindingFlags.Static);
            _moveOne = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>), method);
            Plugin.Logger.LogDebug("[UB] Quick Transfer's one-card click replay found.");
        }
        catch (Exception ex)
        {
            _moveOne = null;
            WarnOnce("qt:lookup", $"Looking up Quick Transfer's click replay threw ({ex.GetType().Name}: {ex.Message}); {feature}.");
        }
    }

    /// <summary>
    /// Called every Update: resolves both modules on the first call, then keeps the Repeat Action subscription equal to
    /// <paramref name="wanted"/> (the Auto-Pull Materials option), so with the option off nothing is subscribed and Repeat
    /// Action runs exactly as its standalone release.
    /// </summary>
    internal static void Tick(bool wanted)
    {
        EnsureRepeatAction();
        EnsureQuickTransfer();
        if (wanted == _unmetSubscribed || _unmetEvent == null || _unmetDelegate == null)
        {
            return;
        }

        try
        {
            if (wanted)
            {
                _unmetEvent.AddEventHandler(null, _unmetDelegate);
            }
            else
            {
                _unmetEvent.RemoveEventHandler(null, _unmetDelegate);
            }

            _unmetSubscribed = wanted;
            Plugin.Logger.LogDebug($"[UB] Auto-Pull Materials {(wanted ? "subscribed to" : "unsubscribed from")} Repeat Action's requirements-unmet event.");
        }
        catch (Exception ex)
        {
            // Not retried every frame: drop the event, warn once, and leave the feature off.
            _unmetEvent = null;
            WarnOnce("ra:subscribe", $"Subscribing to Repeat Action's requirements-unmet event threw ({ex.GetType().Name}: {ex.Message}); Auto-Pull Materials stays off.");
        }
    }

    /// <summary>Keeps Equip Spare Tool On Break's requirements-unmet subscription equal to <paramref name="wanted"/>, so with
    /// the option off its handler is not subscribed and Repeat Action runs exactly as its standalone release.</summary>
    internal static void TickSpareTool(bool wanted)
    {
        EnsureRepeatAction();
        if (wanted == _spareSubscribed || _unmetEvent == null || _spareDelegate == null)
        {
            return;
        }

        try
        {
            if (wanted)
            {
                _unmetEvent.AddEventHandler(null, _spareDelegate);
            }
            else
            {
                _unmetEvent.RemoveEventHandler(null, _spareDelegate);
            }

            _spareSubscribed = wanted;
            Plugin.Logger.LogDebug($"[UB] Equip Spare Tool On Break {(wanted ? "subscribed to" : "unsubscribed from")} Repeat Action's requirements-unmet event.");
        }
        catch (Exception ex)
        {
            // Not retried every frame: drop this feature's handler, warn once, and leave it off.
            _spareDelegate = null;
            WarnOnce("ra:subscribe-spare", $"Subscribing Equip Spare Tool On Break to Repeat Action's requirements-unmet event threw ({ex.GetType().Name}: {ex.Message}); Equip Spare Tool On Break stays off.");
        }
    }

    /// <summary>Drop the subscriptions (Plugin.OnDestroy).</summary>
    internal static void Unsubscribe()
    {
        TickIteration(false);
        TickSpareTool(false);

        if (!_unmetSubscribed || _unmetEvent == null || _unmetDelegate == null)
        {
            return;
        }

        try
        {
            _unmetEvent.RemoveEventHandler(null, _unmetDelegate);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[UB] Unsubscribing from Repeat Action's requirements-unmet event threw ({ex.GetType().Name}); ignored at shutdown.");
        }

        _unmetSubscribed = false;
    }

    /// <summary>Quick Transfer's TryClickMoveOne on <paramref name="card"/>; false with Quick Transfer's reason when nothing moved.</summary>
    internal static bool TryClickMoveOne(InGameCardBase card, out string refusal)
    {
        refusal = null;
        EnsureQuickTransfer();
        var call = _moveOne;
        if (call == null)
        {
            refusal = "Quick Transfer's click replay is not available";
            return false;
        }

        try
        {
            if (call(card))
            {
                return true;
            }

            refusal = _lastRefusal?.GetValue(null, null) as string ?? "Quick Transfer gave no reason";
            return false;
        }
        catch (Exception ex)
        {
            WarnOnce("qt:call:" + ex.GetType().Name, $"Quick Transfer's TryClickMoveOne threw ({ex.GetType().Name}: {ex.Message}); the card is moved by MoveCardToSlot instead.");
            refusal = "Quick Transfer's click replay threw";
            return false;
        }
    }
}
