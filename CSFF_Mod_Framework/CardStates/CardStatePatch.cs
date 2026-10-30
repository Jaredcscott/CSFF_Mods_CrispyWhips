using CSFFModFramework.Util;

namespace CSFFModFramework.CardStates;

/// <summary>
/// Harmony and event wiring of <see cref="Api.CardState"/> (framework 2.29.0, fleet plan 6.3).
///
/// <list type="bullet">
/// <item>Save: a prefix and postfix on private <c>GameLoad.SaveGameDataToFile(int, GameSaveDataByReference,
/// CheckpointTypes, int)</c> (<c>.decomp/GameLoad.cs</c> 565-581) bracket the write; a prefix and postfix on public
/// static <c>GameLoad.WriteAllTextWithBackup(string, string)</c> capture the exact JSON written and whether the write
/// succeeded. The game builds that JSON in a local, so this is the one place the written text can be read without
/// serializing the save a second time. The custom-checkpoint save writes twice (its own file and AutoSave), and each
/// write gets its own sidecar.</item>
/// <item>Blueprints: a prefix and postfix on the compiled <c>MoveNext</c> of <c>InGameCardBase.PerformBlueprintBuild</c>
/// (an iterator's <c>MoveNext</c> is reached through interface dispatch and never inlined: root CLAUDE.md "Harmony
/// Patching Pitfalls"). The prefix acts only on the first step (<c>&lt;&gt;1__state == 0</c>).</item>
/// <item>Events, subscribed once per process (pattern (b) of root CLAUDE.md "Per-Run Event Registration": the latch
/// returns before any subscription): <c>GameManager.OnCardDestroyed</c> (game transforms),
/// <c>SpawnService.CardSpawned</c> (blueprint results) and <c>GameManager.OnGMInitialized</c> (read the sidecar at
/// run start, warn once when it is missing). The handlers only read and reset per-run state.</item>
/// </list>
/// Every hook early-outs when nothing is declared or held, so a game with no mod using card values pays a field
/// read per save write and per blueprint step.
/// </summary>
internal static class CardStatePatch
{
    private static bool _subscribed;
    private static FieldInfo _stateField;
    private static FieldInfo _thisField;

    // The save write in flight (main thread only).
    private static bool _capturing;
    private static string _captured;
    private static bool _writeOk;

    internal static void ApplyPatch(Harmony harmony)
    {
        PatchSave(harmony);
        PatchBlueprintBuild(harmony);
        Subscribe();
    }

    private static void PatchSave(Harmony harmony)
    {
        try
        {
            var save = AccessTools.Method(typeof(GameLoad), "SaveGameDataToFile",
                new[] { typeof(int), typeof(GameSaveDataByReference), typeof(CheckpointTypes), typeof(int) });
            var write = AccessTools.Method(typeof(GameLoad), nameof(GameLoad.WriteAllTextWithBackup), new[] { typeof(string), typeof(string) });
            if (save == null || write == null)
            {
                Log.Warn($"CardState: GameLoad.{(save == null ? "SaveGameDataToFile" : "WriteAllTextWithBackup")} not found on this game version, so card values are never written and every load starts empty.");
                return;
            }
            harmony.Patch(save,
                prefix: new HarmonyMethod(typeof(CardStatePatch), nameof(SaveToFile_Prefix)),
                postfix: new HarmonyMethod(typeof(CardStatePatch), nameof(SaveToFile_Postfix)),
                finalizer: new HarmonyMethod(typeof(CardStatePatch), nameof(SaveToFile_Finalizer)));
            harmony.Patch(write,
                prefix: new HarmonyMethod(typeof(CardStatePatch), nameof(Write_Prefix)),
                postfix: new HarmonyMethod(typeof(CardStatePatch), nameof(Write_Postfix)));
        }
        catch (Exception ex)
        {
            Log.Warn($"CardState: patching the save write failed, so card values are never written: {Log.ExceptionText(ex)}");
        }
    }

    private static void PatchBlueprintBuild(Harmony harmony)
    {
        try
        {
            var builder = AccessTools.Method(typeof(InGameCardBase), nameof(InGameCardBase.PerformBlueprintBuild));
            var moveNext = builder == null ? null : AccessTools.EnumeratorMoveNext(builder);
            if (moveNext == null)
            {
                Log.Warn("CardState: InGameCardBase.PerformBlueprintBuild's MoveNext not found on this game version, so card values are not carried through blueprints.");
                return;
            }
            _stateField = AccessTools.Field(moveNext.DeclaringType, "<>1__state");
            _thisField = AccessTools.Field(moveNext.DeclaringType, "<>4__this");
            if (_stateField == null || _thisField == null)
            {
                Log.Warn("CardState: PerformBlueprintBuild's state machine has no <>1__state / <>4__this field on this game version, so card values are not carried through blueprints.");
                return;
            }
            harmony.Patch(moveNext,
                prefix: new HarmonyMethod(typeof(CardStatePatch), nameof(BuildMoveNext_Prefix)),
                postfix: new HarmonyMethod(typeof(CardStatePatch), nameof(BuildMoveNext_Postfix)));
        }
        catch (Exception ex)
        {
            Log.Warn($"CardState: patching blueprint builds failed, so card values are not carried through blueprints: {Log.ExceptionText(ex)}");
        }
    }

    private static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        try
        {
            GameManager.OnCardDestroyed += CardStateCarry.OnCardDestroyed;
            GameManager.OnGMInitialized += CardStateStore.OnRunStarted;
            Api.SpawnService.CardSpawned += CardStateCarry.OnCardSpawned;
        }
        catch (Exception ex)
        {
            Log.Warn($"CardState: subscribing to the game's card events failed, so values are not carried through transforms or into blueprint results: {Log.ExceptionText(ex)}");
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private static void SaveToFile_Prefix()
    {
        _capturing = true;
        _captured = null;
        _writeOk = false;
    }

    private static void Write_Prefix(string contents)
    {
        if (_capturing) _captured = contents;
    }

    private static void Write_Postfix(bool __result)
    {
        if (_capturing) _writeOk = __result;
    }

    private static void SaveToFile_Postfix(int _Index, GameSaveDataByReference _Data)
    {
        var json = _captured;
        bool ok = _writeOk;
        _capturing = false;
        _captured = null;
        if (json == null || !ok || _Data == null) return;
        CardStateStore.OnSaveWritten(_Index, _Data.SaveFileName, json);
    }

    private static Exception SaveToFile_Finalizer(Exception __exception)
    {
        _capturing = false;
        _captured = null;
        return __exception;
    }

    // ── Blueprint build ──────────────────────────────────────────────────────

    private static void BuildMoveNext_Prefix(object __instance)
    {
        if (!CardStateStore.AnyBlueprintCarry) return;
        try
        {
            if ((int)_stateField.GetValue(__instance) != 0) return;
            if (_thisField.GetValue(__instance) is InGameCardBase site) CardStateCarry.BeginBuild(__instance, site);
        }
        catch (Exception ex) { CardStateStore.WarnOnce("bp-prefix:" + Log.CauseKey(ex), $"CardState: reading a blueprint build threw: {Log.ExceptionText(ex)}"); }
    }

    private static void BuildMoveNext_Postfix(object __instance, bool __result)
    {
        if (!CardStateCarry.HasOpenBuilds) return;
        CardStateCarry.StepBuild(__instance, __result);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    /// <summary>Plugin.Update.</summary>
    internal static void PollUpdate()
    {
        CardStateCarry.PollUpdate();
        CardStateCarry.ExpireStaleBuilds();
    }
}
