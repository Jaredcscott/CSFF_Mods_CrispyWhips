namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// NPC action sounds (eating, working, etc.) go through <c>SoundManager.PerformActionSound</c>
    /// — a single global, non-positional sound player with no environment check at all, called
    /// identically for player and NPC actions. This mutes it for NPC actions performed in an
    /// environment the player isn't currently standing in.
    ///
    /// <c>PerformActionSound</c> receives no context about which action/actor triggered it, and
    /// <c>GameManager.PerformActionAsEnumerator</c> only CONSTRUCTS the coroutine that eventually
    /// calls it (C# iterator methods don't run body code until MoveNext) — so a single static
    /// "last acting NPC" field would race if two actions are ever in flight at once. Instead this
    /// wraps the returned IEnumerator (root CLAUDE.md's documented safe pattern for coroutines)
    /// so the acting NPC's environment is save/restored around each MoveNext() of THIS SPECIFIC
    /// invocation — correct even with interleaved/nested actions, since Unity's coroutine
    /// scheduler never runs two coroutines' body code inside the same MoveNext() call.
    /// </summary>
    public static class NpcSoundEnvGatePatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        private static EnvID? _actingNpcEnv;

        public static void ApplyPatch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(GameManager), nameof(GameManager.PerformActionAsEnumerator)),
                postfix: new HarmonyMethod(typeof(NpcSoundEnvGatePatch), nameof(WrapWithNpcEnvContext)));

            harmony.Patch(
                AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PerformActionSound)),
                prefix: new HarmonyMethod(typeof(NpcSoundEnvGatePatch), nameof(GateNpcSound)));

            Logger.LogDebug("NpcSoundEnvGatePatch applied.");
        }

        private static void WrapWithNpcEnvContext(InGameNPCOrPlayer _User, ref IEnumerator __result)
        {
            try
            {
                if (_User.NPC != null && __result != null)
                    __result = WithNpcEnvContext(__result, _User.NPC.CurrentEnvironment);
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[NpcSoundEnvGatePatch] WrapWithNpcEnvContext failed: {ex}");
            }
        }

        private static IEnumerator WithNpcEnvContext(IEnumerator inner, EnvID npcEnv)
        {
            while (true)
            {
                bool moved;
                var previous = _actingNpcEnv;
                _actingNpcEnv = npcEnv;
                try { moved = inner.MoveNext(); }
                finally { _actingNpcEnv = previous; }
                if (!moved) yield break;
                yield return inner.Current;
            }
        }

        private static bool GateNpcSound()
        {
            try
            {
                if (_actingNpcEnv.HasValue && GameManager.Instance != null
                    && !_actingNpcEnv.Value.MatchesEnv(GameManager.Instance.CurrentEnvironment))
                {
                    return false; // skip original — the acting NPC isn't in the player's environment
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[NpcSoundEnvGatePatch] GateNpcSound failed: {ex}");
            }
            return true;
        }
    }
}
