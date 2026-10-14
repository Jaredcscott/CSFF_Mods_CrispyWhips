using UnityEngine;

namespace HarshWilds.Patcher;

/// <summary>
/// Fail-closed check on the crop blight's damage, <c>HarshWilds/SelfTriggeredAction/Tgr_HW_BlightSpread.json</c>
/// (Documentation/Research/HarshWilds/events_stats.md section 5b, the crop blight DECISION), the same class
/// of risk as the bandit's theft that <see cref="BanditTheftGuard"/> covers.
///
/// <para>The spread is two vanilla <c>ExtraDurabilityChange</c>s: one adds Fungal Infection to the planted
/// crops of the place the player stands, one takes Health from the ones already past vanilla's own infection
/// threshold, both restricted to an allowlist of <see cref="CropCount"/> vanilla planted-crop cards. The data
/// is safe only while the allowlist RESOLVED: <c>ExtraDurabilityChange.AppliesToAllCards</c>
/// (<c>.decomp/ExtraDurabilityChange.cs</c>) reads an empty <c>AffectedCards</c> with no <c>AffectedTags</c> as
/// "every card", so a list whose entries all failed to resolve would turn a crop blight into a durability
/// edit of every card on the board: chests and their contents, tools, the food in the player's hands. The
/// static gate (<c>Development_Tools/Tests/HarshWilds-Events.Tests.ps1</c>) pins the JSON and recomputes the
/// crop list from the export; this guard pins what the game actually loaded. If anything about either
/// resolved change differs from the contract, the action's changes are REMOVED (an action with no
/// <c>ExtraDurabilityModifications</c>, no stat write and no produced card fails
/// <c>CardAction.WillHaveAnEffect</c> and never plays), so a broken build damages nothing, and a warning names
/// the cause once.</para>
///
/// <para>It runs as a postfix on <c>GameManager.InitializeStatsAndActions</c>, like
/// <see cref="Switchboard"/> and <see cref="BanditTheftGuard"/>: after the framework has loaded and resolved
/// every mod object (its <c>LoadMainGameData</c> postfix, before the main menu) and before any self-triggered
/// action of the run can fire. A Harmony postfix, not an event handler, so a save reload re-runs the check
/// without stacking anything; once disarmed the changes stay removed for the rest of the process, since the
/// object is loaded once. Any fault leaves the blight's roll and its timer running (they write only our own
/// stats): the player still sees the alert and nothing is damaged.</para>
/// </summary>
internal static class BlightSpreadGuard
{
    internal const string SpreadUid = "Tgr_HW_BlightSpread";

    /// <summary>The planted-crop allowlist size: 22 families times Growing and Mature. The static gate checks
    /// the JSON list, this set and the export all agree.</summary>
    internal const int CropCount = 44;

    /// <summary>Vanilla's crop stat caps: Special4 Fungal Infection and Special2 Health. A resolved card whose
    /// stats differ is not one of the crops this change was written for.</summary>
    internal const float FungalMax = 288f;
    internal const float HealthMax = 96f;

    /// <summary>The Fungal Infection range at which vanilla's own 'Fungal Infestation (Health -0.05)' effect
    /// starts on every one of the crops; the health change may only touch crops in it.</summary>
    internal const float HealthThreshold = 144f;

    /// <summary>Sanity bounds on one spread (HW_BLIGHT_FUNGAL, HW_BLIGHT_HEALTH are tunables, these are not):
    /// one spread adds at most this much fungus and takes at most a quarter of a crop's Health.</summary>
    internal const float MaxFungalPerSpread = 100f;
    internal const float MaxHealthPerSpread = 24f;

    /// <summary>The 44 vanilla planted-crop UniqueIDs (GUID hashes) of the Growing and Mature cards, from
    /// Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/CardData.json. Dormant cards and the
    /// field cards are left out on purpose (plan section 6, events_stats.md section 5b).</summary>
    internal static readonly HashSet<string> CropUids = new(StringComparer.Ordinal)
    {
        "b0ad42ad249a7ed4abc9bcf62d37b6fe",
        "94e66cc435647744c87c28871bf629d0",
        "94ed3e2ecd2c28848ad36daded02f7db",
        "e6023cd8a593fad4083b930cb6fc5717",
        "9d59a5783a6ad8e4794ede97c5db44b9",
        "0e63f1ef5195f834dbc5b18ed174e93b",
        "51d7675f1e9d2824b9c741341258cccd",
        "e4216f4c6d8a4534dbf5002fbfbb5cd4",
        "88140a952096e3c4b8945120ec51d032",
        "39325c2f62c6c8a45b1c328f89f618ff",
        "06b45ab477dd2164eb8fe8b0f5cfa42d",
        "2a4724e8e6d5d1f4fa4aa96775469cf7",
        "b090218bbe026654ab389b03b1c7122b",
        "c82babd4e62fd2c4f9300fd9843370e6",
        "8ceeb5cd8ea00894b8b8a96b1afd1b80",
        "e6ba8805e22ee3e478225c360dd073e2",
        "6411f72c73ec4b94f99386b4f81f0985",
        "b5fa4f8f5dc6c4e41b08a3c2afb508d4",
        "984ba64dad826584b8f15a59b7a98192",
        "f697da43533639444b84cd5f25fae96f",
        "e834347a32cd5dc46a65f5711df5ab8f",
        "abaf4bf79a6b15143a574feb492e8939",
        "fbabe33851dc08d43b710964cfb73e32",
        "f4254172ea45850409adb0731697eaac",
        "0e7da4472cfd6854794781ee6d5c3f6b",
        "f1c4f527304922b46beeee6755acdfd0",
        "4c6a1a1388bc79743a1e3fd9d33f8963",
        "8101d2c6e9b9cb841850db24c932fc45",
        "0c748e69b4f34894a90cbdd04aff9fc0",
        "e66b1e62282fe734099d9f83600485a5",
        "792c1d44a5aa9e046ad962209d6501e8",
        "cfd2a20901732264c9f9dd33f5bf1428",
        "c609f5777061d62458cd027440a908f7",
        "cdc67f36ec6a67e4cb41a9f730353ed6",
        "d6164215fa8ad974ab7416b827f895c9",
        "88575cff8dc308f4ba70392b8a3bfcb6",
        "b8cc7312804b6004da25309bbde45e71",
        "a600da11fd395584c98486eeefb766df",
        "55d509c50e7ec584db988ff0c60d6c4a",
        "b508423d37fd01346ae998f8fa7acf4b",
        "03260dbaac8613342aa0e482c70222ce",
        "bbf139dd8a04a39478d77d9fe26e42ee",
        "333c2694d0d34594ea0845b899aa753d",
        "2b358ed6f80e0b04fbad4d660fc3f301",
    };

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    internal static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
        if (target == null)
        {
            Plugin.Logger.LogWarning("BlightSpreadGuard: GameManager.InitializeStatsAndActions not found; the crop blight could not be verified.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(BlightSpreadGuard), nameof(InitializeStatsAndActions_Postfix)));

        // The engine scales a self-triggered action's remote durability edit by CancelledAtTick / max(1, the
        // action's own cost) (.decomp/GameManager.cs, ActionRoutine and ApplyExtraDurabilitiesChanges), and
        // CancelledAtTick is 0 from the moment a root action starts until it ends, so a spread that fires from
        // inside the player's own timed action (the per-tick stat check) was multiplied by 0 and landed
        // nothing, while one fired after the action was multiplied by that action's cost. The prefix pins the
        // multiplier of THIS action's change list (matched by array identity) to 1.
        var apply = AccessTools.Method(typeof(GameManager), "ApplyExtraDurabilitiesChanges");
        if (apply == null)
        {
            Plugin.Logger.LogWarning("BlightSpreadGuard: GameManager.ApplyExtraDurabilitiesChanges not found; the crop blight's later spreads may land nothing.");
            return;
        }
        harmony.Patch(apply, prefix: new HarmonyMethod(typeof(BlightSpreadGuard), nameof(ApplyExtra_Prefix)));
    }

    /// <summary>The spread action's loaded change list, set by the load check; null while it is absent or disarmed.</summary>
    private static ExtraDurabilityChange[] _spreadChanges;

    private static void ApplyExtra_Prefix(ExtraDurabilityChange[] _Changes, ref float _Multiplier)
    {
        if (_spreadChanges != null && ReferenceEquals(_Changes, _spreadChanges)) _Multiplier = 1f;
    }

    private static void InitializeStatsAndActions_Postfix()
    {
        var sta = UniqueIDScriptable.GetFromID<SelfTriggeredAction>(SpreadUid);
        if (sta == null)
        {
            // Nothing to disarm: with the object absent no blight damage can run at all.
            WarnOnce("missing", $"BlightSpreadGuard: '{SpreadUid}' is not loaded; the crop blight damages nothing.");
            return;
        }
        if (sta.Actions == null || sta.Actions.Length == 0)
        {
            WarnOnce("no-actions", $"BlightSpreadGuard: '{SpreadUid}' has no actions; the crop blight damages nothing.");
            return;
        }

        for (int i = 0; i < sta.Actions.Length; i++)
        {
            var action = sta.Actions[i];
            if (action == null) continue;
            var changes = action.ExtraDurabilityModifications;
            if (changes == null || changes.Length == 0) continue; // already disarmed, or never armed

            string fault = sta.Actions.Length != 1 ? $"{sta.Actions.Length} actions, expected 1" : Fault(changes);
            if (fault == null)
            {
                _spreadChanges = changes;
                continue;
            }

            _spreadChanges = null;
            action.ExtraDurabilityModifications = Array.Empty<ExtraDurabilityChange>();
            WarnOnce(fault, $"BlightSpreadGuard: '{SpreadUid}' action {i} does not match its safety contract ({fault}); " +
                            "the blight's damage is disabled for this session and it damages nothing.");
        }
    }

    /// <summary>The first way the loaded changes break the contract, or null when it holds. Change 0 adds
    /// Fungal Infection, change 1 takes Health from crops past <see cref="HealthThreshold"/>.</summary>
    internal static string Fault(ExtraDurabilityChange[] changes)
    {
        if (changes.Length != 2) return $"{changes.Length} changes, expected 2";
        for (int i = 0; i < changes.Length; i++)
        {
            string fault = ChangeFault(changes[i], i);
            if (fault != null) return $"change {i}: {fault}";
        }
        return null;
    }

    private static string ChangeFault(ExtraDurabilityChange c, int index)
    {
        if (c == null) return "the change is null";
        if (c.AppliesTo != RemoteDurabilityChanges.AllCardsInLocation) return $"AppliesTo {c.AppliesTo}, expected AllCardsInLocation";
        if (!c.LookInInventories) return "LookInInventories is false, so it would never reach a crop inside a plot";
        if (c.IncludeEquippedCards) return "IncludeEquippedCards is true";
        if (c.CardChanges != RemoteCardStateChanges.ChangeDurabilities) return $"CardChanges {c.CardChanges}, expected ChangeDurabilities: the blight never destroys a card";
        if (c.DropOnDestroyList) return "DropOnDestroyList is true";
        if (c.CanSendToEnvironment) return "it sends cards to another environment";
        if (c.AddedFlavours != null && c.AddedFlavours.Length > 0) return "it adds flavours";
        if (c.AffectedTags != null && c.AffectedTags.Length > 0) return $"{c.AffectedTags.Length} AffectedTags, expected none";
        if (c.NOTAffectedThings != null && c.NOTAffectedThings.Length > 0) return $"{c.NOTAffectedThings.Length} NOTAffectedThings, expected none";
        if (c.RandomlyAffectedCards < 0f || c.RandomlyAffectedCards > 1f) return $"RandomlyAffectedCards {c.RandomlyAffectedCards}, expected 0 (every crop) or a fraction up to 1";
        if (c.RandomlyAffectedCards > 0f && c.RandomAmountType != RandomAffectCardsTypes.Percentage) return $"RandomAmountType {c.RandomAmountType}, expected Percentage";

        // The one that matters: an unresolved allowlist is "every card".
        if (c.AffectedCards == null) return "the allowlist is null";
        if (c.AffectedCards.Count != CropCount) return $"the allowlist resolved {c.AffectedCards.Count} cards, expected {CropCount}";
        if (c.AppliesToAllCards(true) || c.AppliesToAllCards(false)) return "the change reads as applying to every card";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in c.AffectedCards)
        {
            if (card == null) return "an allowlist entry did not resolve";
            string name = card.name;
            if (!CropUids.Contains(card.UniqueID)) return $"allowlist card {name} ({card.UniqueID}) is not one of the {CropCount} planted crops";
            if (!seen.Add(card.UniqueID)) return $"allowlist card {name} is listed twice";
            if (card.CardType != CardTypes.Item) return $"allowlist card {name} is {card.CardType}, expected Item";
            if (!(bool)card.SpecialDurability2 || card.SpecialDurability2.MaxValue != HealthMax) return $"allowlist card {name} has no Special2 Health of {HealthMax}";
            if (!(bool)card.SpecialDurability3) return $"allowlist card {name} has no Special3 Mite Infestation";
            if (!(bool)card.SpecialDurability4 || card.SpecialDurability4.MaxValue != FungalMax) return $"allowlist card {name} has no Special4 Fungal Infection of {FungalMax}";
        }

        // Which durability it moves, and by how much.
        if (!IsZero(c.SpoilageChange) || !IsZero(c.UsageChange) || !IsZero(c.FuelChange) || !IsZero(c.ChargesChange)
            || !IsZero(c.LiquidChange) || !IsZero(c.Special1Change) || !IsZero(c.Special3Change))
            return "it changes a durability other than the one it is for";

        if (index == 0)
        {
            if (!IsZero(c.Special2Change)) return "the fungus change also writes Special2 Health";
            if (!IsFixedBetween(c.Special4Change, 0f, MaxFungalPerSpread))
                return $"Special4Change {c.Special4Change.x}..{c.Special4Change.y}, expected a fixed value above 0 and at most {MaxFungalPerSpread}";
            if (!c.RequiredDurabilities.IsEmpty) return "the fungus change carries a durability condition: it must reach every crop";
        }
        else
        {
            if (!IsZero(c.Special4Change)) return "the health change also writes Special4 Fungal Infection";
            if (!IsFixedBetween(c.Special2Change, -MaxHealthPerSpread, 0f))
                return $"Special2Change {c.Special2Change.x}..{c.Special2Change.y}, expected a fixed loss above 0 and at most {MaxHealthPerSpread}";
            var range = c.RequiredDurabilities.Special4Range;
            if (range.x != HealthThreshold || range.y != FungalMax)
                return $"RequiredDurabilities.Special4Range {range.x}..{range.y}, expected {HealthThreshold}..{FungalMax}: health may only be taken from crops already past the infection threshold";
            if (c.RequiredDurabilities.SpoilageRange != Vector2.zero || c.RequiredDurabilities.UsageRange != Vector2.zero
                || c.RequiredDurabilities.FuelRange != Vector2.zero || c.RequiredDurabilities.ProgressRange != Vector2.zero
                || c.RequiredDurabilities.Special1Range != Vector2.zero || c.RequiredDurabilities.Special2Range != Vector2.zero
                || c.RequiredDurabilities.Special3Range != Vector2.zero || c.RequiredDurabilities.LiquidQuantityRange != Vector2.zero)
                return "the health change carries a durability condition other than the infection threshold";
        }
        return null;
    }

    private static bool IsZero(Vector2 v) => v.x == 0f && v.y == 0f;

    /// <summary>True for a change with no random spread (x equals y) that is not zero and lies between the two
    /// bounds: (0, max] for a gain, [-max, 0) for a loss.</summary>
    private static bool IsFixedBetween(Vector2 v, float a, float b)
    {
        if (v.x != v.y) return false;
        float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        return v.x != 0f && v.x >= lo && v.x <= hi;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}
