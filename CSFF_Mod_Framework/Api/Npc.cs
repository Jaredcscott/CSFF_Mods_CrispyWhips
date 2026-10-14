using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// NPC helpers (2.28.0, plan section 10). One member: <see cref="MoveTo"/>, which moves a live NPC to
/// an environment named by its UniqueID.
///
/// <para><b>Why it exists.</b> <c>GameManager.MoveNPC(npc, envId)</c> is public and one line, and it
/// fails SILENTLY: it moves nobody when the destination's <c>EnvID</c> reads as the NPC's current
/// environment, and an <c>EnvID</c> is only as good as the key it carries. That key is the environment
/// card's <c>UniqueIDScriptable.UniqueIDIndex</c>, which reads 0 for EVERY mod environment card at the
/// main menu until the framework's load numbers it (measured on EA 0.68b; framework 2.28.0+ assigns it
/// in <c>Loading/UniqueIdIndexRepair</c>, before that nothing did), so an
/// <c>EnvID</c> built from a card and CACHED in a field by a poll that starts at the main menu makes every
/// cached destination the same room. Community Mod Chest's
/// <c>CottageResidentSchedulePatch</c> did exactly that: the Miller and the Weaver never went to the Inn
/// again, with no log line (root CLAUDE.md "Runtime Card State Caching" item 5, memory
/// reference_envid_key_stale_uniqueidindex, fixed CMC 1.69.1). <see cref="MoveTo"/> takes the UniqueID,
/// not an <c>EnvID</c>, so a caller has nothing to cache: the <c>EnvID</c> is built inside the call,
/// from the card, at the moment of use, and is never stored.</para>
///
/// <para><b>Thin on purpose (plan Open question 7).</b> No reentrancy guard, no scheduling, no retry,
/// no instanced environments. The move is the game's own synchronous <c>MoveNPC</c>, so it is safe from
/// a tick or an <c>OnGMInitialized</c> handler, and the move-before-drop race and the env-entry retry
/// rules stay the caller's (root CLAUDE.md "Runtime Card Spawning"; the Animals lifecycle ticker's
/// header). An instanced environment (a cabin, a cellar) needs a parent <c>EnvID</c> the caller must
/// supply, which a bare UniqueID cannot carry, so it is refused rather than guessed.</para>
///
/// <para>Every failure exit warns once per cause and subject, because the failure this replaces is a
/// non-throwing no-op that no catch ever sees. The shape gate
/// <c>Development_Tools/Tests/Framework-NpcMoveTo.Tests.ps1</c> reads this file for exactly that, and for
/// the rule that no <c>EnvID</c> is ever held in a field.</para>
/// </summary>
public static class Npc
{
    // One warning per (cause, subject) for the life of the process: a schedule that calls this on a
    // poll must not write a line per call.
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string cause, string subject, string message)
    {
        if (_warned.Add(cause + ":" + subject)) Log.Warn(message);
    }

    /// <summary>
    /// Moves a live NPC to the environment whose UniqueID is <paramref name="envUid"/>, through the game's
    /// own <c>GameManager.MoveNPC</c>, and returns true when the NPC is in that environment afterwards
    /// (including when it already was: the game treats that as nothing to do). <paramref name="npc"/> is
    /// the live <c>InGameNPC</c>, taken as <c>object</c>.
    ///
    /// <para>The destination <c>EnvID</c> is built here, from the environment card, on every call. Never
    /// build one yourself and keep it: see the class remarks.</para>
    ///
    /// <para>Returns false, with one warning per cause and subject, when the UniqueID is empty, no run is
    /// in progress (no <c>GameManager</c>: the main menu, or a run still loading), nothing is loaded under
    /// the UniqueID, it names something that is not a <c>CardData</c> of the Environment type (the
    /// card type that has an <c>EnvID</c>), it names an instanced environment (out of scope), the NPC is
    /// null, destroyed or not an <c>InGameNPC</c>, the game refused to build the <c>EnvID</c>, the call
    /// threw, or <c>MoveNPC</c> returned and the NPC's <c>CurrentEnvironment</c> still does not match
    /// (something intercepted the move). Never throws. The destination is checked before the NPC, so a
    /// bad UniqueID is reported even when the NPC is also wrong.</para>
    /// </summary>
    public static bool MoveTo(object npc, string envUid)
    {
        if (string.IsNullOrEmpty(envUid))
        {
            WarnOnce("empty-uid", "", "Npc.MoveTo was called with an empty environment UniqueID, so it moved nothing and returned false.");
            return false;
        }
        try
        {
            // Unity's == on the MonoBehaviour static type: a destroyed manager, and no manager at all
            // (the main menu), both read null.
            var gm = CardUtil.GetGameManagerInstance() as GameManager;
            if (gm == null)
            {
                WarnOnce("no-run", envUid, $"Npc.MoveTo('{envUid}'): no run is in progress (no live GameManager), so it moved nothing and returned false. Logged once per environment.");
                return false;
            }

            var found = Data.GameRegistry.GetByUid(envUid);
            if (found == null)
            {
                WarnOnce("unknown-uid", envUid, $"Npc.MoveTo: no object with UniqueID '{envUid}' is loaded, so it moved nothing and returned false. Check the UID against the mod's environment card JSON.");
                return false;
            }
            if (found is not CardData card)
            {
                WarnOnce("not-a-card", envUid, $"Npc.MoveTo: UniqueID '{envUid}' is a {found.GetType().Name}, not a CardData, so it moved nothing and returned false.");
                return false;
            }
            // The EnvID constructor logs a Unity error and nulls itself for a card that is not an
            // Environment, which MoveNPC then ignores without a word, so refuse here and say why.
            if (card.CardType != CardTypes.Environment)
            {
                WarnOnce("not-an-environment", envUid, $"Npc.MoveTo: '{envUid}' has CardType {card.CardType}, not Environment (an EnvID can only be built from an Environment card), so it moved nothing and returned false. The Environment card is the CardType 4 one, not a location or explorable card that leads to it.");
                return false;
            }
            if (card.InstancedEnvironment)
            {
                WarnOnce("instanced", envUid, $"Npc.MoveTo: '{envUid}' is an instanced environment, which needs a parent EnvID that a UniqueID cannot carry, so it moved nothing and returned false. Instanced environments are out of scope for this call.");
                return false;
            }

            var live = npc as InGameNPC;
            if (live == null)
            {
                string what = npc == null ? "null" : npc is InGameNPC ? "a destroyed InGameNPC" : npc.GetType().Name;
                WarnOnce("not-an-npc", what, $"Npc.MoveTo('{envUid}'): the NPC argument is {what}, not a live InGameNPC, so it moved nothing and returned false.");
                return false;
            }

            // Built HERE, at the moment of use, from the card, and never stored: an EnvID's key is the
            // card's UniqueIDIndex, which reads 0 for a mod environment at the main menu (class remarks).
            var dest = new EnvID(card, false);
            if (dest.IsNull)
            {
                WarnOnce("env-id-null", envUid, $"Npc.MoveTo('{envUid}'): the game built a null EnvID for this environment card even though it is a non-instanced Environment, so it moved nothing and returned false. A game update probably changed the EnvID constructor: re-read .decomp/EnvID.cs.");
                return false;
            }

            gm.MoveNPC(live, dest);

            if (!dest.MatchesEnv(live.CurrentEnvironment))
            {
                WarnOnce("not-landed", envUid, $"Npc.MoveTo('{envUid}'): GameManager.MoveNPC returned but NPC '{live.name}' is not in that environment (its CurrentEnvironment is '{live.CurrentEnvironment.SimpleEnvName}'), so something intercepted the move. It returned false.");
                return false;
            }
            Log.Debug($"Npc.MoveTo: NPC '{live.name}' is in '{envUid}'.");
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("threw", envUid, $"Npc.MoveTo('{envUid}') threw {ex.GetType().Name}: {Log.ExceptionText(ex)}, so it returned false.");
            return false;
        }
    }
}
