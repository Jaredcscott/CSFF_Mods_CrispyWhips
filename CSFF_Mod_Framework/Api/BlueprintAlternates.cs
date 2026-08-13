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
/// </summary>
public static class BlueprintAlternates
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Makes every blueprint/improvement element that requires <paramref name="primaryUid"/>
    /// also accept <paramref name="alternateUid"/>. Both UIDs must resolve in
    /// <paramref name="allData"/> — this is what makes the call a no-op (returns 0) when the
    /// mod shipping the alternate isn't installed. Calling this repeatedly for the same
    /// <paramref name="primaryUid"/> with different alternates accumulates — each call appends
    /// to the same shared <c>CardTabGroup</c> instead of replacing it, so one primary can end up
    /// accepting several alternates (e.g. a nail slot accepting iron nails, copper rivets, AND
    /// iron rivets). Returns the number of RequiredElements patched.
    /// </summary>
    public static int AddAlternateIngredient(IEnumerable allData, string primaryUid, string alternateUid, string label = null)
    {
        label ??= $"{primaryUid}->{alternateUid}";
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
            // nail slot that accepts iron nails AND copper/iron rivets). Every group this method
            // creates is named "alt_tab_{primaryUid}" — a later call for the SAME primary reuses
            // that exact group (appending to IncludedCards) instead of allocating a fresh one and
            // clobbering the RequiredTabGroup reference an earlier call already set.
            var groupName = $"alt_tab_{primaryUid}";
            UnityEngine.Object sharedGroup = null;

            int patched = 0;
            foreach (var item in allData)
            {
                if (item == null) continue;
                var ctField = item.GetType().GetField("CardType", Flags);
                if (ctField == null) continue;
                int ct = Convert.ToInt32(ctField.GetValue(item));
                if (ct != 7 && ct != 10) continue;

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

                        var existingGroup = fiRequiredTabGroup.GetValue(elem) as UnityEngine.Object;
                        IList includedCards;
                        if (existingGroup != null && existingGroup.name == groupName)
                        {
                            includedCards = fiIncludedCards.GetValue(existingGroup) as IList;
                        }
                        else if (existingGroup == null)
                        {
                            if (sharedGroup == null)
                            {
                                sharedGroup = ScriptableObject.CreateInstance(tabGroupType) as UnityEngine.Object;
                                if (sharedGroup == null) { Log.Warn($"[BlueprintAlternates] {label}: failed to create CardTabGroup."); continue; }
                                sharedGroup.name = groupName;
                            }
                            fiRequiredTabGroup.SetValue(elem, sharedGroup);
                            elements.SetValue(elem, i);
                            includedCards = fiIncludedCards.GetValue(sharedGroup) as IList;
                        }
                        else
                        {
                            // Slot already carries a foreign, unrelated RequiredTabGroup from JSON
                            // authoring — never clobber it.
                            Log.Warn($"[BlueprintAlternates] {label}: element already has an unrelated RequiredTabGroup ('{existingGroup.name}'); skipped to avoid clobbering it.");
                            continue;
                        }

                        if (includedCards == null) { Log.Warn($"[BlueprintAlternates] {label}: CardTabGroup.IncludedCards not accessible."); continue; }
                        if (!includedCards.Contains(alternateData)) includedCards.Add(alternateData);
                        patched++;
                    }
                }
            }

            Log.Debug($"[BlueprintAlternates] {label}: {patched} blueprint slot(s) now accept the alternate ingredient.");
            return patched;
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintAlternates] {label}: {Log.ExceptionText(ex)}");
            return 0;
        }
    }
}
