using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Adds alternate-ingredient acceptance to blueprint/improvement RequiredElements
/// (Centralization Tier 3). Walks CT7/CT10 <c>BlueprintStages[].RequiredElements[]</c> for
/// any element whose <c>RequiredCard</c> resolves to <paramref name="primaryUid"/>, and
/// attaches a <c>CardTabGroup</c> listing the alternate card so
/// <c>BlueprintElement.CompatibleCard</c>'s two-branch check (RequiredCard OR
/// RequiredTabGroup) accepts either. Uses the same in-memory
/// <c>ScriptableObject.CreateInstance</c> technique as WarpResolver — never writes a
/// <c>ScriptableObject/CardTabGroup/*.json</c> file (root CLAUDE.md §Blueprints).
/// Replaces the near-identical reflection block independently written by
/// AdvancedCopperTools (<c>GameLoadPatch.PatchNailInterchangeability</c>).
/// <para>
/// Two entry points, one engine: <see cref="AddAlternateIngredient"/> patches EVERY blueprint
/// that requires the primary; <see cref="AddAlternateIngredientIn"/> patches only the blueprints
/// it is told to. The unfiltered form reaches vanilla blueprints a mod never meant to touch (an
/// alternate for vanilla <c>Fat</c> would also reach <c>Bp_Dynamite</c> and <c>Bp_Fatchunk</c>),
/// which is what the allow-list form exists to prevent.
/// </para>
/// <para>
/// Group naming, which both entry points share: the unfiltered form keeps ONE group per
/// primary, <c>alt_tab_{primaryUid}</c>. The allow-list form gives each allowed blueprint its OWN
/// group, <c>alt_tab_{primaryUid}@{blueprintUid}</c>, seeded from the shared group when the slot
/// already carried it. A private group is never shared with a blueprint outside the list, so a
/// later unfiltered call for the same primary cannot leak the allow-list's alternate into other
/// blueprints; the unfiltered form recognizes the <c>@</c> groups as its own and appends to them,
/// so an allowed blueprint still accepts what the unfiltered callers add.
/// </para>
/// </summary>
public static class BlueprintAlternates
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // ENGINE SEAMS. The two operations that need the Unity runtime (create a CardTabGroup, read a Unity
    // object's name with Unity's own destroyed-is-null rule) sit behind these fields so the walk and the
    // group bookkeeping run under Development_Tools/Tests/Framework-BlueprintAlternates.Tests.ps1, which has
    // no engine. Nothing in the game or in a content mod replaces them.
    internal static Func<Type, string, object> CreateGroup = CreateTabGroup;
    internal static Func<object, string> GroupNameOf = ReadGroupName;

    private static object CreateTabGroup(Type tabGroupType, string name)
    {
        var group = ScriptableObject.CreateInstance(tabGroupType) as UnityEngine.Object;
        if (group != null) group.name = name;
        return group;
    }

    // The group's name, or null when the slot has no group (null, or a destroyed Unity object, which Unity's
    // == treats as null). Any other object is reported by type, so it is never mistaken for one of ours.
    private static string ReadGroupName(object group)
    {
        if (group == null) return null;
        if (group is UnityEngine.Object unityObject) return unityObject == null ? null : unityObject.name;
        return "<" + group.GetType().Name + ">";
    }

    internal enum SlotPlan
    {
        /// <summary>The slot already carries a group this call may append to.</summary>
        Reuse,
        /// <summary>Unfiltered call, slot has no group: attach the call's shared group.</summary>
        AttachShared,
        /// <summary>Allow-list call, slot has no group: attach the blueprint's private group.</summary>
        AttachPrivate,
        /// <summary>Allow-list call, slot carries the shared group: swap in the private group, seeded from it.</summary>
        SwapSharedForPrivate,
        /// <summary>The slot carries an unrelated group: never clobber it.</summary>
        SkipForeign,
    }

    /// <summary>
    /// What to do with one slot, from the name of the group it carries (null when none). Pure string logic,
    /// so the whole decision table is checkable without the game. <paramref name="privateName"/> is null for
    /// the unfiltered form and <c>alt_tab_{primaryUid}@{blueprintUid}</c> for the allow-list form.
    /// </summary>
    internal static SlotPlan PlanSlot(string existingName, string sharedName, string privatePrefix, string privateName)
    {
        if (existingName == null) return privateName == null ? SlotPlan.AttachShared : SlotPlan.AttachPrivate;

        if (privateName == null)
        {
            // Unfiltered: its own shared group, or ANY private group this primary's allow-list calls made.
            return existingName == sharedName || existingName.StartsWith(privatePrefix, StringComparison.Ordinal)
                ? SlotPlan.Reuse
                : SlotPlan.SkipForeign;
        }

        // Allow-list: this blueprint's own private group, or the shared group it gets swapped out of. Another
        // blueprint's private group cannot sit on this slot, so it falls through to foreign with the rest.
        if (existingName == privateName) return SlotPlan.Reuse;
        if (existingName == sharedName) return SlotPlan.SwapSharedForPrivate;
        return SlotPlan.SkipForeign;
    }

    /// <summary>
    /// Makes every blueprint/improvement element that requires <paramref name="primaryUid"/>
    /// also accept <paramref name="alternateUid"/>. Both UIDs must resolve in
    /// <paramref name="allData"/> — this is what makes the call a no-op (returns 0) when the
    /// mod shipping the alternate isn't installed. Calling this repeatedly for the same
    /// <paramref name="primaryUid"/> with different alternates accumulates — each call appends
    /// to the same shared <c>CardTabGroup</c> instead of replacing it, so one primary can end up
    /// accepting several alternates (e.g. a nail slot accepting iron nails, copper rivets, AND
    /// iron rivets). Returns the number of RequiredElements patched.
    /// <para>
    /// This reaches EVERY blueprint that requires the primary, vanilla ones included. To limit it
    /// to blueprints you name, call <see cref="AddAlternateIngredientIn"/> instead.
    /// </para>
    /// </summary>
    public static int AddAlternateIngredient(IEnumerable allData, string primaryUid, string alternateUid, string label = null)
    {
        label ??= $"{primaryUid}->{alternateUid}";
        return Apply(allData, primaryUid, alternateUid, null, null, label);
    }

    /// <summary>
    /// Like <see cref="AddAlternateIngredient"/>, but only the blueprints/improvements whose
    /// <c>UniqueID</c> is in <paramref name="blueprintUids"/> are touched; every other blueprint that
    /// requires <paramref name="primaryUid"/> is left exactly as it was. A vanilla blueprint is named by
    /// its GUID, like any other vanilla reference. A named blueprint with no element requiring the primary
    /// is a quiet no-op for that blueprint.
    /// Each allowed element gets a PRIVATE group named <c>alt_tab_{primaryUid}@{blueprintUid}</c>, seeded
    /// from the shared <c>alt_tab_{primaryUid}</c> group when the slot already carried it, so a later
    /// unfiltered call for the same primary does not leak this alternate into blueprints outside the list.
    /// Repeated calls accumulate in the same private groups. A null or empty list patches NOTHING and warns,
    /// never every blueprint. Returns the number of RequiredElements patched; one Debug line names each
    /// blueprint and the slots it now has. A slot that already carries an unrelated tab group is skipped
    /// with a warning, as in the unfiltered form.
    /// </summary>
    public static int AddAlternateIngredientIn(IEnumerable allData, string primaryUid, string alternateUid, string[] blueprintUids, string label = null)
    {
        label ??= $"{primaryUid}->{alternateUid}";
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        if (blueprintUids != null)
        {
            foreach (var uid in blueprintUids)
                if (!string.IsNullOrEmpty(uid) && allowed.Add(uid)) order.Add(uid);
        }
        if (allowed.Count == 0)
        {
            // Falling back to "every blueprint" here would be the exact leak this overload exists to prevent.
            Log.Warn($"[BlueprintAlternates] {label}: no blueprint UIDs were given, so nothing was patched (an empty allow-list never means every blueprint).");
            return 0;
        }
        return Apply(allData, primaryUid, alternateUid, allowed, order, label);
    }

    // allowed == null: the unfiltered walk. Otherwise only blueprints whose UniqueID is in the set are visited,
    // and `order` is the caller's UIDs, de-duplicated, for the per-blueprint log lines.
    private static int Apply(IEnumerable allData, string primaryUid, string alternateUid, HashSet<string> allowed, List<string> order, string label)
    {
        try
        {
            if (allData == null || string.IsNullOrEmpty(primaryUid) || string.IsNullOrEmpty(alternateUid)) return 0;

            object primaryData = null, alternateData = null;
            foreach (var item in allData)
            {
                if (item == null) continue;
                var uid = item.GetType().GetField("UniqueID", Flags)?.GetValue(item) as string;
                if (uid == primaryUid) primaryData = item;
                else if (uid == alternateUid) alternateData = item;
                if (primaryData != null && alternateData != null) break;
            }
            if (primaryData == null) { Log.Debug($"[BlueprintAlternates] {label}: primary UID not found; skipped."); return 0; }
            if (alternateData == null) { Log.Debug($"[BlueprintAlternates] {label}: alternate UID not found (companion mod not installed?); skipped."); return 0; }

            var tabGroupType = AccessTools.TypeByName("CardTabGroup");
            if (tabGroupType == null) { Log.Warn($"[BlueprintAlternates] {label}: CardTabGroup type not found."); return 0; }

            var fiIncludedCards = tabGroupType.GetField("IncludedCards", BindingFlags.Instance | BindingFlags.Public);
            if (fiIncludedCards == null) { Log.Warn($"[BlueprintAlternates] {label}: CardTabGroup.IncludedCards not accessible."); return 0; }

            var elemType = AccessTools.TypeByName("BlueprintElement");
            if (elemType == null) { Log.Warn($"[BlueprintAlternates] {label}: BlueprintElement type not found."); return 0; }
            var fiRequiredCard = elemType.GetField("RequiredCard", Flags);
            var fiRequiredTabGroup = elemType.GetField("RequiredTabGroup", Flags);
            if (fiRequiredCard == null || fiRequiredTabGroup == null)
            {
                Log.Warn($"[BlueprintAlternates] {label}: BlueprintElement field reflection failed.");
                return 0;
            }

            // A primary can accumulate more than one alternate across separate calls (e.g. a
            // nail slot that accepts iron nails AND copper/iron rivets). Every group the unfiltered form
            // creates is named "alt_tab_{primaryUid}" — a later call for the SAME primary reuses
            // that exact group (appending to IncludedCards) instead of allocating a fresh one and
            // clobbering the RequiredTabGroup reference an earlier call already set. The allow-list form's
            // private groups are "alt_tab_{primaryUid}@{blueprintUid}", one per allowed blueprint.
            var sharedName = $"alt_tab_{primaryUid}";
            var privatePrefix = sharedName + "@";
            object sharedGroup = null;
            var privateGroups = allowed != null ? new Dictionary<string, object>(StringComparer.Ordinal) : null;
            var slotsIn = allowed != null ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
            var seenBlueprints = allowed != null ? new HashSet<string>(StringComparer.Ordinal) : null;

            int patched = 0;
            foreach (var item in allData)
            {
                if (item == null) continue;

                string blueprintUid = null;
                if (allowed != null)
                {
                    blueprintUid = item.GetType().GetField("UniqueID", Flags)?.GetValue(item) as string;
                    if (blueprintUid == null || !allowed.Contains(blueprintUid)) continue;
                }

                var ctField = item.GetType().GetField("CardType", Flags);
                if (ctField == null) continue;
                int ct = Convert.ToInt32(ctField.GetValue(item));
                if (ct != 7 && ct != 10) continue;
                seenBlueprints?.Add(blueprintUid);

                var stagesField = item.GetType().GetField("BlueprintStages", Flags);
                var stages = stagesField?.GetValue(item) as Array;
                if (stages == null) continue;

                for (int s = 0; s < stages.Length; s++)
                {
                    var stage = stages.GetValue(s);
                    if (stage == null) continue;

                    var fiElements = stage.GetType().GetField("RequiredElements", BindingFlags.Instance | BindingFlags.Public);
                    var elements = fiElements?.GetValue(stage) as Array;
                    if (elements == null) continue;

                    for (int i = 0; i < elements.Length; i++)
                    {
                        // Box the struct so FieldInfo.SetValue can modify it in place.
                        object elem = elements.GetValue(i);
                        if (fiRequiredCard.GetValue(elem) != primaryData) continue;

                        var existingGroup = fiRequiredTabGroup.GetValue(elem);
                        var privateName = allowed != null ? privatePrefix + blueprintUid : null;
                        var plan = PlanSlot(GroupNameOf(existingGroup), sharedName, privatePrefix, privateName);

                        IList includedCards;
                        switch (plan)
                        {
                            case SlotPlan.Reuse:
                                includedCards = fiIncludedCards.GetValue(existingGroup) as IList;
                                break;

                            case SlotPlan.AttachShared:
                                if (sharedGroup == null)
                                {
                                    sharedGroup = CreateGroup(tabGroupType, sharedName);
                                    if (sharedGroup == null) { Log.Warn($"[BlueprintAlternates] {label}: failed to create CardTabGroup."); continue; }
                                }
                                fiRequiredTabGroup.SetValue(elem, sharedGroup);
                                elements.SetValue(elem, i);
                                includedCards = fiIncludedCards.GetValue(sharedGroup) as IList;
                                break;

                            case SlotPlan.AttachPrivate:
                            case SlotPlan.SwapSharedForPrivate:
                            {
                                // One private group per blueprint, shared by that blueprint's own slots.
                                if (!privateGroups.TryGetValue(blueprintUid, out var privateGroup))
                                {
                                    privateGroup = CreateGroup(tabGroupType, privateName);
                                    if (privateGroup == null) { Log.Warn($"[BlueprintAlternates] {label}: failed to create CardTabGroup for '{blueprintUid}'."); continue; }
                                    privateGroups[blueprintUid] = privateGroup;
                                }
                                includedCards = fiIncludedCards.GetValue(privateGroup) as IList;
                                if (includedCards == null) { Log.Warn($"[BlueprintAlternates] {label}: CardTabGroup.IncludedCards not accessible."); continue; }

                                // The slot already accepted whatever the shared group lists (earlier unfiltered
                                // calls); carry that over so swapping the group never takes an alternate away.
                                if (plan == SlotPlan.SwapSharedForPrivate && fiIncludedCards.GetValue(existingGroup) is IList seed)
                                {
                                    foreach (var card in seed)
                                        if (!includedCards.Contains(card)) includedCards.Add(card);
                                }

                                fiRequiredTabGroup.SetValue(elem, privateGroup);
                                elements.SetValue(elem, i);
                                break;
                            }

                            default:
                                // Slot already carries a foreign, unrelated RequiredTabGroup from JSON
                                // authoring — never clobber it.
                                Log.Warn($"[BlueprintAlternates] {label}: element already has an unrelated RequiredTabGroup ('{GroupNameOf(existingGroup)}'); skipped to avoid clobbering it.");
                                continue;
                        }

                        if (includedCards == null) { Log.Warn($"[BlueprintAlternates] {label}: CardTabGroup.IncludedCards not accessible."); continue; }
                        if (!includedCards.Contains(alternateData)) includedCards.Add(alternateData);
                        patched++;
                        if (slotsIn != null) slotsIn[blueprintUid] = slotsIn.TryGetValue(blueprintUid, out var n) ? n + 1 : 1;
                    }
                }
            }

            if (allowed == null)
            {
                Log.Debug($"[BlueprintAlternates] {label}: {patched} blueprint slot(s) now accept the alternate ingredient.");
                return patched;
            }

            // One line per named blueprint, in the caller's order: what a caller most needs when a name is
            // wrong (a typo, a vanilla name where a GUID is wanted) is to see WHICH one patched nothing.
            int blueprintsPatched = 0;
            foreach (var blueprintUid in order)
            {
                if (!seenBlueprints.Contains(blueprintUid))
                    Log.Debug($"[BlueprintAlternates] {label}: blueprint '{blueprintUid}' not found among the blueprints/improvements; skipped.");
                else if (!slotsIn.TryGetValue(blueprintUid, out var slots))
                    Log.Debug($"[BlueprintAlternates] {label}: blueprint '{blueprintUid}' has no slot the alternate could be added to (nothing requires '{primaryUid}' there, or its tab group is unrelated); skipped.");
                else
                {
                    blueprintsPatched++;
                    Log.Debug($"[BlueprintAlternates] {label}: blueprint '{blueprintUid}' now accepts the alternate ingredient in {slots} slot(s).");
                }
            }
            Log.Debug($"[BlueprintAlternates] {label}: {patched} blueprint slot(s) in {blueprintsPatched} of {order.Count} named blueprint(s) now accept the alternate ingredient.");
            return patched;
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintAlternates] {label}: {Log.ExceptionText(ex)}");
            return 0;
        }
    }
}
