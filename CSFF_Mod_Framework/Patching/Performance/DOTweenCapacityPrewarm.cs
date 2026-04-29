using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// DOTween capacity pre-warm — speed boost for sessions with many cards.
//
// Symptom observed in LogOutput.log:
//   "DOTWEEN ► Max Tweens reached: capacity has automatically been increased from 200/50 to 500/50."
//
// When many cards animate simultaneously (e.g. inventory shuffle, blueprint screen opening,
// drag-and-drop with a full base) DOTween blows past its default 200 tweener pool. The runtime
// expansion allocates a fresh pool (GC spike) and logs a warning (more GC). With hundreds of
// cards on the board this happens repeatedly during normal play.
//
// Fix: pre-allocate a larger pool at framework init, once. Reflection-based so we don't add a
// hard dependency on DOTween — if the game ships without it, we no-op cleanly.
//
// Also downshifts logBehaviour to ErrorsOnly: DOTween's default "Default" level emits warnings
// and info messages for every target-null tween that fires; in a heavily-modded game that can
// be hundreds per second.
internal static class DOTweenCapacityPrewarm
{
    private const int TweenerCapacity = 1000;
    private const int SequenceCapacity = 200;

    public static void Configure(ConfigFile config)
    {
        var tweenersCfg = config.Bind(
            "Performance", "DOTweenTweenerCapacity", TweenerCapacity,
            "Pre-allocate DOTween tweener pool to this size. Prevents mid-session GC spike when many cards animate at once. Default 1000 (game default is 200).");
        var sequencesCfg = config.Bind(
            "Performance", "DOTweenSequenceCapacity", SequenceCapacity,
            "Pre-allocate DOTween sequence pool to this size. Default 200 (game default is 50).");
        var quietCfg = config.Bind(
            "Performance", "DOTweenQuietLogs", true,
            "Downshift DOTween log verbosity to ErrorsOnly. Reduces per-frame GC from tween warnings. Default true.");

        try
        {
            var dotweenType = AccessTools.TypeByName("DG.Tweening.DOTween");
            if (dotweenType == null)
            {
                Util.Log.Debug("DOTweenCapacityPrewarm: DOTween type not found, skipping.");
                return;
            }

            var setCapacity = AccessTools.Method(dotweenType, "SetTweensCapacity",
                new[] { typeof(int), typeof(int) });
            if (setCapacity == null)
            {
                Util.Log.Warn("DOTweenCapacityPrewarm: SetTweensCapacity(int,int) not found.");
                return;
            }

            setCapacity.Invoke(null, new object[] { tweenersCfg.Value, sequencesCfg.Value });

            if (quietCfg.Value)
            {
                var logEnumType = AccessTools.TypeByName("DG.Tweening.LogBehaviour");
                var logProp = AccessTools.Property(dotweenType, "logBehaviour");
                if (logEnumType != null && logProp != null && logProp.CanWrite)
                {
                    // LogBehaviour enum: Default=0, Verbose=1, ErrorsOnly=2
                    var errorsOnly = Enum.ToObject(logEnumType, 2);
                    logProp.SetValue(null, errorsOnly);
                }
            }

            Util.Log.Info($"DOTweenCapacityPrewarm: pool pre-warmed to {tweenersCfg.Value}/{sequencesCfg.Value} (tweeners/sequences).");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"DOTweenCapacityPrewarm: failed to pre-warm DOTween pool: {ex.Message}");
        }
    }
}
