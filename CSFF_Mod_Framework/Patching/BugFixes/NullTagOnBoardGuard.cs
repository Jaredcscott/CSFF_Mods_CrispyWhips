namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Makes <c>GameManager.TagIsOnBoard</c> answer false for a null tag in an instanced environment,
/// as it already does everywhere else, so the vanilla Animal Enclosure and Chicken Coop no longer
/// wedge every action on entry.
///
/// <para><strong>The vanilla flaw</strong> (EA 0.68b, retro
/// <c>Documentation/Retrospectives/vanilla-pen-coop-null-tag-wedge.md</c>). The "Land Sickness Fix"
/// passive effect on <c>Env_Enclosure</c> and <c>Env_Coop</c> carries
/// <c>Conditions.RequiredTagsOnBoard = [null]</c>. <c>TagIsOnBoard</c> opens with
/// <c>!_InEnv.IsNull &amp;&amp; _InEnv.IsInstancedEnv &amp;&amp; !_Tag.DoesntShowOnBoardFromInstancedEnv</c>,
/// which dereferences the tag with no null check, but only in an instanced env; its other branches
/// (<c>EnvironmentHasTag</c>, <c>CardData.HasTag</c>) start with <c>!_Tag</c> and return false. Both
/// pens are instanced and the effect is only evaluated while the player is inside, so the effect
/// throws every time it is checked: the NRE kills the action coroutine with <c>RootAction</c> still
/// set, and every later action answers "I can't do two things at once...". Reproduced with no
/// framework and no content mod loaded.</para>
///
/// <para><strong>The fix.</strong> A prefix that returns false and skips the original when the tag is
/// null (Unity null, so a destroyed tag counts). Any non-null tag runs the original untouched. The
/// effect stays as inert as it has always been (it never applied: it threw first); the vanilla data
/// is not edited, because removing the null entry would switch the LandSickness effect on for the
/// first time, which is an owner decision. The method is hot, so the prefix is a bare null test plus
/// one warn-once log line.</para>
/// </summary>
internal static class NullTagOnBoardGuard
{
    private static bool _warned;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameManager), nameof(GameManager.TagIsOnBoard));
            if (original == null)
            {
                Util.Log.Warn("NullTagOnBoardGuard: GameManager.TagIsOnBoard not found; entering the vanilla Animal Enclosure or Chicken Coop can still lock every action.");
                return;
            }
            harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(typeof(NullTagOnBoardGuard), nameof(Prefix))));
            Util.Log.Debug("NullTagOnBoardGuard: patched GameManager.TagIsOnBoard.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"NullTagOnBoardGuard: patch failed; entering the vanilla Animal Enclosure or Chicken Coop can still lock every action: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static bool Prefix(CardTag _Tag, ref bool __result)
    {
        if (_Tag == null)
        {
            if (!_warned)
            {
                _warned = true;
                Util.Log.Warn("NullTagOnBoardGuard: a condition asked whether a null CardTag is on the board (vanilla \"Land Sickness Fix\" on Env_Enclosure/Env_Coop carries one); answering false instead of throwing. Logged once.");
            }
            __result = false;
            return false;
        }
        return true;
    }
}
