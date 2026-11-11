using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher
{
    /// <summary>
    /// All companion upkeep: auto-feed, auto-drink, starvation health drain, and
    /// morale-departure check, evaluated once per in-game DTP tick.
    ///
    /// Handles wolf, fox, and owl companions. Auto-drink fires when a companion's
    /// thirst drops below 20% and a container holding WATER is on the board: it drains
    /// 100 liquid from the nearest one and restores the companion's thirst to full.
    /// A container qualifies when it carries vanilla tag_WaterContainer (any mod's
    /// container joins without a UID list) or is one of the vanilla water sources
    /// (ClayBasin, WateringTrough, RainCistern, ...); its contained liquid must carry
    /// vanilla tag_WaterAny, so a brine vat, a cauldron of medicine or a kettle of tea is
    /// never drunk from (owner, 2026-10-09: "limit the Sirus animals to water"). The read and the
    /// drain both go through CardUtil.GetLiquidQuantity/ModifyLiquid, which resolve to the
    /// container's own ContainedLiquid card and route the write through the game's own liquid
    /// path, so an emptied container's liquid card is destroyed the way vanilla drinking does it
    /// rather than left behind at quantity 0. Auto-feed works identically for hunger using the
    /// nearest available food item.
    ///
    /// Companion Food Bowl (1.22.13): before that, a companion below HALF Hunger eats one food card
    /// out of a Food Bowl (fc_food_bowl) in the player's current environment. The bowl takes the
    /// same AnimalFoods set (its InventoryFilter is gated equal to VanillaIds.AnimalFoods by
    /// Development_Tools/Tests/Sirus23-CompanionFoodBowl.Tests.ps1). Like every rule here it runs
    /// only for companions on the player's board: the game does not tick a card left on another map.
    ///
    /// Morale-departure applies to all three companions. Health (SpecialDurability1) is
    /// C#-managed for the wolf only (WolfHealthRule: it falls on low Hunger and on empty Thirst):
    /// the fox and owl carry the same bar, but it drains from JSON PassiveEffects while their
    /// Hunger or Thirst is empty and kills them through the bar's own OnZero action
    /// (CardData/Item/FoxCompanion.json and OwlCompanion.json).
    ///
    /// Every stat read goes through TryGetStat and every stat write checks its result: a read or
    /// write that fails (field drift after a game or framework update) logs one warning per cause
    /// instead of making the companions silently stop eating, drinking, healing or leaving.
    /// </summary>
    public static class WolfTickPatch
    {
        public const string WolfId = "fc_wolf_companion";
        public const string FoxId  = "fc_fox_companion";
        public const string OwlId  = "fc_owl_companion";
        public const string HawkId = "fc_hawk_companion";

        private const string WolfCarcassUid = "aad9634a9b5273844bbe2e7a119c5b23";

        // Companion Food Bowl (plan 1.2, Chiwei c174135852): a placed bowl whose small inventory takes the
        // same AnimalFoods the companions auto-eat. A companion below BowlThreshold Hunger eats one food card
        // from a bowl in the player's current environment, before the loose-food rule below 20% is tried.
        public const string BowlId = "fc_food_bowl";
        private const float BowlThreshold = 0.50f;

        private const float Threshold = 0.20f;
        private static ManualLogSource Logger => Plugin.Logger;

        private static readonly string[] FallbackFoodIds =
        {
            "fe07d4d800bcc8646a0ff2513c78d5df",
            "e292e8faac1041f4ab1da9bcf900e751",
            "91168c5978471d54aaad816581f63ffd",
            "f4be79e87ba98db41a9c9e31bb76c33d",
            "5dc9560f81be57c4b9e22780f8a5ad78",
            "692c88d91a4fdf0498749339ae6d53f8",
            "4093dde8982306b4d8a5a9108ac5f0bb",
            "1e3f5517c9203e74b9c76a7bae1a3920",
            "ec0f203ea77c79741a70170a2c42e202",
            "f6cc4f98e0ab4ae47b4b2c8f5d48da02",
            "b25f1f9bfc2a0bc44a93079ed02cb43f",
            "699942038046235428420895876b83e2",
            "674b640f46671f1418af4559b259b442",
            "3dda5e481d296c5409b05ac6e66a9221"
        };

        // Vanilla tags (both in CardTag.txt): a container any mod ships joins by tagging
        // itself, and only a liquid tagged as water is drunk (Interop Registry R8, V-TAG).
        // Compared by name, never by instance (WarpResolver can mint a same-named tag SO).
        private const string WaterContainerTag = "tag_WaterContainer";
        private const string WaterLiquidTag = "tag_WaterAny";

        // Vanilla backstop: used when VanillaIds registry is empty (e.g. framework not installed).
        private static readonly string[] FallbackWaterIds =
        {
            "2ae9292d2b80ccb41a0840ca736e0d12",  // ClayBasin
            "1cecbd7141fa89a4c90686dcd2d92155",  // WateringTrough
            "536f722edbb5e9e4b959b1f3ad25f648",  // RainCistern
            "a968f3eaffc6b9743b82982b5af2ab8c",  // ClayBowl
            "db78f1724c2fffe4d9302c72457ca8bf",  // ClayJar
            "57ede9efd6e81ec41a065f3f7a2e3d8e",  // CopperBottle
            "dff317def52c6294a8ee7cfc3ce23b63",  // CopperJar
            "2674de0b486a266499d2edfc38f5148d",  // WaterskinWaterProofed_
        };

        private static HashSet<string> _foodIds;
        private static HashSet<string> _waterIds;
        // Per CardData: does the model carry the tag? Cards share models, so this keeps the
        // board scan to one CardTags walk per card type.
        private static readonly Dictionary<UnityEngine.Object, bool> _containerModelCache = new();
        private static readonly Dictionary<UnityEngine.Object, bool> _waterLiquidModelCache = new();
        private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

        private static List<object> _bowlsThisTick;
        private static readonly HashSet<object> _eatenThisTick = new();

        private static List<object> _cachedCompanions;
        private static List<object> _cachedFoods;
        private static List<object> _cachedWaters;
        private static int _lastAllCardsCount = int.MinValue;
        private static Type _cardBaseType;

        public static void Register()
        {
            _foodIds  = BuildIdSet(VanillaIds.AnimalFoods,  FallbackFoodIds);
            _waterIds = BuildWaterIdSet();
            TickEvents.DtpTick += OnTick;
        }

        private static HashSet<string> BuildWaterIdSet()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var registry = VanillaIds.WaterSources;
            if (registry != null && registry.Count > 0)
                foreach (var uid in registry) set.Add(uid);
            else
                foreach (var uid in FallbackWaterIds) set.Add(uid);
            return set;
        }

        private static HashSet<string> BuildIdSet(IReadOnlyList<string> registry, string[] fallback)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (registry != null && registry.Count > 0)
                foreach (var uid in registry) set.Add(uid);
            else
                foreach (var uid in fallback) set.Add(uid);
            return set;
        }

        private static void OnTick()
        {
            try
            {
                _cardBaseType ??= CardUtil.FindGameType("InGameCardBase");
                if (_cardBaseType == null)
                {
                    WarnOnce("cardbase-type", "[CompanionTick] the InGameCardBase game type could not be resolved; all companion upkeep is skipped this session.");
                    return;
                }

                RefreshCachesIfBoardChanged();
                if (_cachedCompanions == null || _cachedCompanions.Count == 0) return;

                _bowlsThisTick = null;
                _eatenThisTick.Clear();
                foreach (var companion in _cachedCompanions)
                {
                    if (TryInitFreshSpawn(companion)) continue;
                    if (!TryBowlFeed(companion)) TryAutoFeed(companion, _cachedFoods);
                    TryAutoDrink(companion, _cachedWaters);
                    TryManageHealth(companion);
                    TryMoraleCheck(companion);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[CompanionTick] tick error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void RefreshCachesIfBoardChanged()
        {
            int count = GetAllCardsCount();
            if (_cachedCompanions != null && count == _lastAllCardsCount) return;
            _lastAllCardsCount = count;

            _cachedCompanions = new List<object>();
            _cachedFoods      = new List<object>();
            _cachedWaters     = new List<object>();

            var all = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (all == null) return;

            foreach (var obj in all)
            {
                string uid = CardUtil.GetCardUniqueId(obj);
                if (uid == null) continue;
                if (uid == WolfId || uid == FoxId || uid == OwlId || uid == HawkId) _cachedCompanions.Add(obj);
                else if (_foodIds.Contains(uid))  _cachedFoods.Add(obj);
                else if (_waterIds.Contains(uid) || ModelHasTag(obj, WaterContainerTag, _containerModelCache))
                    _cachedWaters.Add(obj);
            }
        }

        private static int GetAllCardsCount()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return -1;
            if (Reflect.GetMember(gm, "AllCards") is System.Collections.ICollection c) return c.Count;
            WarnOnce("allcards-read", "[CompanionTick] GameManager.AllCards could not be read; board-change detection is frozen and companions added or removed this session may not get upkeep.");
            return -1;
        }

        // ── Upkeep rules ─────────────────────────────────────────────────────

        /// <summary>
        /// Companions spawned via ProducedCards/GiveCard (tame success) start with their
        /// durability fields at the struct default (0), not the CardData's designed
        /// FloatValue (SpawnStatDefaults doesn't apply to this spawn path — memory
        /// reference_givecard_postfix_stat_init). Left alone, TryMoraleCheck removes the
        /// companion on the very next tick because Progress (Morale) reads 0. Hunger and
        /// Thirst decay gradually and independently, so a companion legitimately reading
        /// exactly 0 across ALL THREE at once — while under normal play TryMoraleCheck would
        /// already have removed it the instant Morale alone hit 0 — only happens in the tick
        /// right after spawn. Treat that as "never initialized" and fill to full instead of
        /// treating it as starvation/death.
        /// </summary>
        internal static bool TryInitFreshSpawn(object companion)
        {
            if (!TryGetStat(companion, "SpoilageTime", out float hunger, out float hungerMax)) return false;
            if (!TryGetStat(companion, "UsageDurability", out float thirst, out float thirstMax)) return false;
            if (!TryGetStat(companion, "Progress", out float morale, out float moraleMax)) return false;
            if (hungerMax <= 0f || thirstMax <= 0f || moraleMax <= 0f) return false;
            if (hunger > 0f || thirst > 0f || morale > 0f) return false;

            // A failed write leaves the stat at 0, so this companion comes back here every tick and never
            // comes to life: say so once per stat instead of spinning silently.
            string uid = CardUtil.GetCardUniqueId(companion) ?? "companion";
            if (!CardUtil.SetDurability(companion, "SpoilageTime", hungerMax))
                WarnOnce("init-write:SpoilageTime", $"[CompanionTick] {uid} spawned with zeroed stats and its Hunger (SpoilageTime) could not be filled; it stays at 0.");
            if (!CardUtil.SetDurability(companion, "UsageDurability", thirstMax))
                WarnOnce("init-write:UsageDurability", $"[CompanionTick] {uid} spawned with zeroed stats and its Thirst (UsageDurability) could not be filled; it stays at 0.");
            if (!CardUtil.SetDurability(companion, "Progress", moraleMax))
                WarnOnce("init-write:Progress", $"[CompanionTick] {uid} spawned with zeroed stats and its Morale (Progress) could not be filled; it stays at 0.");
            // The fox and owl's Health bar carries the death action (OnZero, ModType Destroy), which
            // the engine fires on the first tick for a card that spawned at 0 even with no crossing
            // (reference_onzero_destroy_races_dtp_tick), so it must be filled with the other three.
            if (TryGetStat(companion, "SpecialDurability1", out float health, out float healthMax)
                && healthMax > 0f && health <= 0f
                && !CardUtil.SetDurability(companion, "SpecialDurability1", healthMax))
                WarnOnce("init-write:SpecialDurability1", $"[CompanionTick] {uid} spawned with zeroed stats and its Health (SpecialDurability1) could not be filled; it stays at 0.");
            Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion) ?? "companion"} spawned with zeroed stats — initialized to full (hunger {hungerMax:F0}, thirst {thirstMax:F0}, morale {moraleMax:F0}).");
            return true;
        }

        private static void TryManageHealth(object companion)
        {
            // Wolf only. The fox and owl have the same Health bar, but their drain is a JSON
            // PassiveEffect and their death is the bar's OnZero action; this tick would also heal
            // them above 60% hunger, drain them below 10%, and spawn a WOLF carcass if one died.
            if (CardUtil.GetCardUniqueId(companion) != WolfId) return;
            if (!TryGetStat(companion, "SpecialDurability1", out float healthCur, out float healthMax)) return;
            if (healthMax <= 0f) return;

            if (!TryGetStat(companion, "SpoilageTime", out float hungerCur, out float hungerMax)) return;
            float hungerRatio = hungerMax > 0f ? hungerCur / hungerMax : 0f;
            // Thirst drains Health like the fox's "Parched Health Drain". An unreadable Thirst bar has
            // already warned in TryGetStat; it counts as not parched so Hunger still works alone.
            float thirstRatio = TryGetStat(companion, "UsageDurability", out float thirstCur, out float thirstMax) && thirstMax > 0f
                ? thirstCur / thirstMax : 1f;

            float newHealth = WolfHealthRule.NextHealth(healthCur, healthMax, hungerRatio, thirstRatio);
            if (newHealth == healthCur) return;
            if (!CardUtil.SetDurability(companion, "SpecialDurability1", newHealth))
            {
                WarnOnce("health-write", "[CompanionTick] the wolf's Health (SpecialDurability1) could not be written, so it neither loses nor mends Health.");
                return;
            }
            if (newHealth <= 0f)
            {
                string cause = hungerRatio < WolfHealthRule.StarvingBelow
                    ? (thirstRatio <= WolfHealthRule.ParchedAtOrBelow ? "hunger and thirst" : "starvation")
                    : "thirst";
                Logger?.LogDebug($"[CompanionTick] wolf died of {cause}: spawning wolf carcass.");
                SpawnService.Spawn(WolfCarcassUid);
                if (!CardUtil.TryRemoveCard(companion))
                    WarnOnce("wolf-death-remove", "[CompanionTick] the dead wolf could not be removed from the board, so it stays at 0 Health beside its carcass.");
                _lastAllCardsCount = int.MinValue;
            }
        }

        private static void TryMoraleCheck(object companion)
        {
            if (!TryGetStat(companion, "Progress", out float cur, out float max)) return;
            if (max <= 0f || cur > 0f) return;

            string uid = CardUtil.GetCardUniqueId(companion) ?? "companion";
            Logger?.LogDebug($"[CompanionTick] {uid} morale depleted — companion has left.");
            if (!CardUtil.TryRemoveCard(companion))
                WarnOnce("depart-remove", $"[CompanionTick] {uid} ran out of Morale but could not be removed from the board, so it has not left.");
            _lastAllCardsCount = int.MinValue;
        }

        /// <summary>
        /// Companion Food Bowl: a companion below half Hunger eats one food card from a Food Bowl in the
        /// player's current environment, filling Hunger and adding 20 Morale like the loose-food rule.
        /// The bowl lookup is environment-scoped (GameQuery.CardsInPlayerEnv, never a bare AllCards UID scan:
        /// CLAUDE.md "An AllCards UID scan is NOT the cards in this room"), so a bowl left at another camp
        /// feeds nobody here. Food removed this tick is remembered, because CardUtil.TryRemoveCard runs the
        /// game's RemoveCard as a coroutine and the card is still in the bowl's slot until it finishes.
        /// Returns true when the companion ate from a bowl.
        /// </summary>
        private static bool TryBowlFeed(object companion)
        {
            if (!TryGetStat(companion, "SpoilageTime", out float cur, out float max)) return false;
            if (max <= 0f || (cur / max) >= BowlThreshold) return false;

            foreach (var bowl in FindBowlsInPlayerEnv())
            {
                object food = null;
                foreach (var card in Inventory.Cards(bowl))
                {
                    if (_eatenThisTick.Contains(card)) continue;
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (uid != null && _foodIds.Contains(uid)) { food = card; break; }
                }
                if (food == null) continue;

                if (!CardUtil.SetDurability(companion, "SpoilageTime", max))
                {
                    WarnOnce("bowl-hunger-write", "[CompanionTick] a companion's Hunger (SpoilageTime) could not be written, so it did not eat from the Food Bowl.");
                    return false;
                }
                string foodUid = CardUtil.GetCardUniqueId(food);
                if (!CardUtil.TryRemoveCard(food))
                    WarnOnce("bowl-remove", $"[CompanionTick] {foodUid} could not be removed from the Food Bowl after a companion ate it.");
                _eatenThisTick.Add(food);
                _lastAllCardsCount = int.MinValue;
                if (TryGetStat(companion, "Progress", out float mor, out float morMax) && morMax > 0f
                    && !CardUtil.SetDurability(companion, "Progress", (float)Math.Min(morMax, mor + 20.0)))
                    WarnOnce("morale-write", "[CompanionTick] a companion's Morale (Progress) could not be raised after it ate.");
                Logger?.LogInfo($"[CompanionTick] {CardUtil.GetCardUniqueId(companion)} ate {foodUid} from the Food Bowl (hunger {cur:F0}->{max:F0})");
                return true;
            }
            return false;
        }

        /// <summary>The Food Bowls in the player's current environment, read once per tick and only when a
        /// companion is hungry enough to look.</summary>
        private static List<object> FindBowlsInPlayerEnv()
        {
            if (_bowlsThisTick != null) return _bowlsThisTick;
            _bowlsThisTick = new List<object>();
            foreach (var card in GameQuery.CardsInPlayerEnv())
                if (CardUtil.GetCardUniqueId(card) == BowlId) _bowlsThisTick.Add(card);
            return _bowlsThisTick;
        }

        private static void TryAutoFeed(object companion, List<object> foods)
        {
            if (foods == null || foods.Count == 0) return;

            if (!TryGetStat(companion, "SpoilageTime", out float cur, out float max)) return;
            if (max <= 0f || (cur / max) >= Threshold) return;

            object food = PickNearest(companion, foods);
            if (food == null) return;

            if (!CardUtil.SetDurability(companion, "SpoilageTime", max))
            {
                WarnOnce("feed-hunger-write", "[CompanionTick] a companion's Hunger (SpoilageTime) could not be written, so it did not eat the food on the board.");
                return;
            }
            string foodUid = CardUtil.GetCardUniqueId(food);
            if (CardUtil.TryRemoveCard(food))
            {
                foods.Remove(food);
                _lastAllCardsCount = int.MinValue;
                // Mirror the Feed CI: auto-feeding also grants a small morale boost so the
                // companion doesn't immediately depart on the same tick it eats.
                if (TryGetStat(companion, "Progress", out float mor, out float morMax) && morMax > 0f
                    && !CardUtil.SetDurability(companion, "Progress", (float)Math.Min(morMax, mor + 20.0)))
                    WarnOnce("morale-write", "[CompanionTick] a companion's Morale (Progress) could not be raised after it ate.");
                Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion)} auto-ate {foodUid} (hunger {cur:F0}->{max:F0})");
            }
            else
                WarnOnce("feed-remove", $"[CompanionTick] {foodUid} could not be removed from the board after a companion ate it, so the companion's Hunger was filled for free.");
        }

        private static void TryAutoDrink(object companion, List<object> waters)
        {
            if (waters == null || waters.Count == 0) return;

            if (!TryGetStat(companion, "UsageDurability", out float cur, out float max)) return;
            if (max <= 0f || (cur / max) >= Threshold) return;

            object source = null;
            float bestDist = float.MaxValue;
            foreach (var w in waters)
            {
                if (!HoldsWater(w)) continue;
                float qty = CardUtil.GetLiquidQuantity((InGameCardBase)w);
                if (float.IsNaN(qty) || qty <= 0f) continue;
                float d = DistanceSquared(companion, w);
                if (d < bestDist) { bestDist = d; source = w; }
            }
            if (source == null) return;

            if (!CardUtil.SetDurability(companion, "UsageDurability", max))
            {
                WarnOnce("drink-thirst-write", "[CompanionTick] a companion's Thirst (UsageDurability) could not be written, so it did not drink.");
                return;
            }
            if (!CardUtil.ModifyLiquid((InGameCardBase)source, -100f))
                WarnOnce("drink-liquid-write", $"[CompanionTick] {CardUtil.GetCardUniqueId(source)}'s water could not be reduced after a companion drank from it, so the drink was free.");
            Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion)} auto-drank from {CardUtil.GetCardUniqueId(source)} (thirst {cur:F0}->{max:F0})");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool TryGetStat(object card, string statName, out float cur, out float max)
        {
            cur = CardUtil.GetDurability(card, statName);
            max = CardUtil.GetDurabilityMax(card, statName);
            if (float.IsNaN(cur) || float.IsNaN(max))
            {
                // Never a normal "below threshold" answer: every companion card has all four bars, so a NaN is
                // field drift or a card without its model, and every rule that reads this stat stops at once.
                WarnOnce($"stat-read:{statName}", $"[CompanionTick] {CardUtil.GetCardUniqueId(card) ?? "a companion"}'s {statName} could not be read (current {cur}, max {max}); companion upkeep that needs it is skipped.");
                cur = 0f; max = 0f;
                return false;
            }
            return true;
        }

        private static float DistanceSquared(object a, object b)
        {
            if (a is not UnityEngine.Component ca || b is not UnityEngine.Component cb) return 0f;
            return (ca.transform.position - cb.transform.position).sqrMagnitude;
        }

        private static object PickNearest(object companion, List<object> candidates)
        {
            object best = null;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                float d = DistanceSquared(companion, c);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        /// <summary>
        /// True when the container's contained liquid card carries tag_WaterAny. A container
        /// with no contained liquid holds nothing drinkable. Read at drink time, because a
        /// container's contents change without the board's card count changing.
        /// </summary>
        private static bool HoldsWater(object container)
        {
            var contained = Reflect.GetMember(container, "ContainedLiquid");
            if (contained is not UnityEngine.Object uo || uo == null) return false;
            return ModelHasTag(contained, WaterLiquidTag, _waterLiquidModelCache);
        }

        private static bool ModelHasTag(object card, string tagName, Dictionary<UnityEngine.Object, bool> cache)
        {
            if (CardUtil.GetCardData(card) is not UnityEngine.Object model || model == null)
            {
                WarnOnce("model-unreadable", $"[CompanionTick] a card's CardModel could not be read; it is not treated as {tagName}.");
                return false;
            }
            if (cache.TryGetValue(model, out bool hit)) return hit;

            hit = false;
            if (Reflect.GetMember(model, "CardTags") is System.Collections.IEnumerable tags)
            {
                foreach (var tag in tags)
                    if (tag is UnityEngine.Object t && t != null
                        && string.Equals(t.name, tagName, StringComparison.Ordinal))
                    { hit = true; break; }
            }
            else
            {
                WarnOnce("cardtags-unreadable", $"[CompanionTick] CardData.CardTags could not be read on '{model.name}'; companion water checks fall back to the vanilla source list.");
            }
            cache[model] = hit;
            return hit;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warnedCauses.Add(cause)) Logger?.LogWarning(message);
        }

    }
}
