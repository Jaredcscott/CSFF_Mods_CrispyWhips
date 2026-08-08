using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// DTP-tick handlers for perks whose effects depend on game-time conditions:
    ///
    ///   Lunacy      — Stress/Insight scale with MoonPhase; MoonUrge spikes at full moon.
    ///   Agoraphobia — Stress/Fear build while the player is in an open (CT4) environment.
    ///   Nyctophobia — Fear/Stress build at night when Moonlight stat is below 50.
    ///
    /// Subscribes to TickEvents.DtpTick (fires every 15 in-game minutes = 1/4 hour).
    /// Per-DTP deltas are 1/4 of the per-hour values in the design spec.
    /// </summary>
    internal static class TraitsTickHandler
    {
        // ── Perk UIDs ───────────────────────────────────────────────────────────
        private const string LunacyPerkUid           = "traitsperklunacy";
        private const string AgoraphobiaPerkUid      = "traitsperkagoraphobia";
        private const string NyctophobiaPerkUid      = "traitsperknyctophobia";
        private const string DrunkardPerkUid         = "traitsperkdrunkard";
        private const string SeasonalAllergiesPerkUid = "traits_perk_seasonal_allergies";
        private const string BlackThumbPerkUid        = "traitsperkblackthumb";

        // ── Stat GUIDs — resolved via VanillaIds where the name matches the registry ─
        private static readonly string GuidStress      = VanillaIds.GetStat("Stress");
        private static readonly string GuidFear        = VanillaIds.GetStat("Fear");
        private static readonly string GuidInsight     = VanillaIds.GetStat("Skill_Insight");
        private static readonly string GuidMoonPhase   = VanillaIds.GetStat("MoonPhase");
        private static readonly string GuidMoonUrge    = VanillaIds.GetStat("MoonUrge");
        private static readonly string GuidMoonlight   = VanillaIds.GetStat("Moonlight");
        private static readonly string GuidAlcohol     = VanillaIds.GetStat("AlcoholEffect"); // Moonlight
        private static readonly string GuidNausea      = VanillaIds.GetStat("Nausea");
        private static readonly string GuidRash        = VanillaIds.GetStat("Rash");
        private static readonly string GuidForagingAid = VanillaIds.GetStat("ForagingAid");
        // YearTime reuses vanilla's SeasonCounter_Summer stat (0–1440 counter) as a
        // year-time clock — the semantic name doesn't match a VanillaIds registry key,
        // so this stays a direct GUID literal (CLAUDE.md: leave direct lookups as-is
        // when the stat isn't a 1:1 name match in the registry).
        private const string GuidYearTime    = "4a9284146ffc28242b1f591ac7f7541f"; // YearTime

        // ── Per-DTP delta values (design spec ÷ 4 DTP/hr) ──────────────────────
        // Lunacy: Stress +0.1/hr, Insight +0.15/hr; at full moon MoonUrge +5/hr
        private const float LunacyStressDeltaPerDtp   = 0.025f; // 0.1 / 4
        private const float LunacyInsightDeltaPerDtp  = 0.0375f; // 0.15 / 4
        private const float LunacyMoonUrgeDeltaPerDtp = 1.25f;   // 5 / 4
        // Agoraphobia: Stress +2/hr, Fear +1/hr outdoors
        private const float AgorphobiaStressDeltaPerDtp = 0.5f;  // 2 / 4
        private const float AgoraphobiaFearDeltaPerDtp  = 0.25f; // 1 / 4
        // Nyctophobia: Fear +3/hr, Stress +2/hr at night without light
        private const float NyctophobiaFearDeltaPerDtp   = 0.75f; // 3 / 4
        private const float NyctophobiaStressDeltaPerDtp = 0.5f;  // 2 / 4
        // Drunkard sobriety craving: Stress +0.1/hr when AlcoholEffect < 20
        private const float DrunkardCravingStressDeltaPerDtp = 0.025f; // 0.1 / 4

        // Seasonal flare year-time ranges (year-time stat counts 0–1440 per year)
        private const float SpringMin   = 1f;
        private const float SpringMax   = 720f;
        private const float LateSmrMin  = 901f;
        private const float LateSmrMax  = 1440f;

        // ── Reflection cache ────────────────────────────────────────────────────
        private static bool       _initialized;
        private static FieldInfo    _allPerksField;
        // DayTimePoints — try multiple names (CLAUDE.md)
        private static readonly string[] DtpPropertyNames =
            { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" };
        private static MemberInfo _dtpMember;  // field or property

        // ── Stat object cache (resolve once at first use) ───────────────────────
        private static object _statStress;
        private static object _statFear;
        private static object _statInsight;
        private static object _statMoonPhase;
        private static object _statMoonUrge;
        private static object _statMoonlight;
        private static object _statAlcohol;
        private static object _statNausea;
        private static object _statRash;
        private static object _statForagingAid;
        private static object _statYearTime;
        private static bool   _statsCached;

        // Season-flare state — reset when year-time leaves the trigger range
        private static bool _allergyFlaredThisSpring;
        private static bool _witheringFiredThisLateSummer;

        // ── Public init ─────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (!TryResolveReflection())
            {
                Plugin.Logger.LogWarning("[TraitsTickHandler] reflection init failed — Lunacy/Agoraphobia/Nyctophobia effects inactive.");
                return;
            }
            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[TraitsTickHandler] Lunacy/Agoraphobia/Nyctophobia/Drunkard tick handlers active.");
        }

        // ── Main tick ───────────────────────────────────────────────────────────

        private static void OnDtpTick()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                bool hasLunacy            = HasPerk(gm, LunacyPerkUid);
                bool hasAgoraphobia       = HasPerk(gm, AgoraphobiaPerkUid);
                bool hasNyctophobia       = HasPerk(gm, NyctophobiaPerkUid);
                bool hasDrunkard          = HasPerk(gm, DrunkardPerkUid);
                bool hasSeasonalAllergies = HasPerk(gm, SeasonalAllergiesPerkUid);
                bool hasBlackThumb        = HasPerk(gm, BlackThumbPerkUid);

                if (!hasLunacy && !hasAgoraphobia && !hasNyctophobia && !hasDrunkard
                    && !hasSeasonalAllergies && !hasBlackThumb) return;

                EnsureStatsCached();

                if (hasLunacy)            ApplyLunacy();
                if (hasAgoraphobia)       ApplyAgoraphobia();
                if (hasNyctophobia)       ApplyNyctophobia(gm);
                if (hasDrunkard)          ApplyDrunkardCraving();
                if (hasSeasonalAllergies) ApplySeasonalAllergiesFlare();
                if (hasBlackThumb)        ApplyWitheringFlare();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TraitsTickHandler] tick threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // ── Per-perk handlers ───────────────────────────────────────────────────

        private static void ApplyLunacy()
        {
            if (_statMoonPhase == null) return;

            float moonPhase = StatAccess.GetCurrentValue(_statMoonPhase);
            float moonMax   = StatAccess.GetMaxValue(_statMoonPhase);
            if (float.IsNaN(moonPhase) || float.IsNaN(moonMax) || moonMax <= 0f) return;

            float scale = Mathf.Clamp01(moonPhase / moonMax);
            if (scale <= 0f) return;

            StatAccess.ModifyCurrentValue(_statStress,  LunacyStressDeltaPerDtp  * scale);
            StatAccess.ModifyCurrentValue(_statInsight, LunacyInsightDeltaPerDtp * scale);

            if (scale > 0.9f)
                StatAccess.ModifyCurrentValue(_statMoonUrge, LunacyMoonUrgeDeltaPerDtp);
        }

        private static void ApplyAgoraphobia()
        {
            if (!IsPlayerOutdoors()) return;
            StatAccess.ModifyCurrentValue(_statStress, AgorphobiaStressDeltaPerDtp);
            StatAccess.ModifyCurrentValue(_statFear,   AgoraphobiaFearDeltaPerDtp);
        }

        private static void ApplyNyctophobia(object gm)
        {
            float dtp = GetDtp(gm);
            float hour = (96f - (dtp % 96f)) / 4f;
            bool isNight = hour < 6f || hour > 20f;
            if (!isNight) return;

            float moonlight = _statMoonlight != null ? StatAccess.GetCurrentValue(_statMoonlight) : 0f;
            if (moonlight >= 50f) return;  // sufficient light — no fear

            StatAccess.ModifyCurrentValue(_statFear,   NyctophobiaFearDeltaPerDtp);
            StatAccess.ModifyCurrentValue(_statStress, NyctophobiaStressDeltaPerDtp);
        }

        private static void ApplyDrunkardCraving()
        {
            // Stress builds while sober (AlcoholEffect < 20) — models withdrawal craving.
            if (_statAlcohol == null) return;
            if (StatAccess.GetCurrentValue(_statAlcohol) >= 20f) return;
            StatAccess.ModifyCurrentValue(_statStress, DrunkardCravingStressDeltaPerDtp);
        }

        private static void ApplySeasonalAllergiesFlare()
        {
            // One-shot spring flare (ResetWhenOutOfRange: re-arms each spring).
            if (_statYearTime == null) return;
            float yt = StatAccess.GetCurrentValue(_statYearTime);
            if (float.IsNaN(yt) || yt < SpringMin || yt > SpringMax) { _allergyFlaredThisSpring = false; return; }
            if (_allergyFlaredThisSpring) return;
            _allergyFlaredThisSpring = true;
            StatAccess.ModifyCurrentValue(_statNausea, 15f);
            StatAccess.ModifyCurrentValue(_statRash,   15f);
        }

        private static void ApplyWitheringFlare()
        {
            // One-shot late-summer ForagingAid drain (ResetWhenOutOfRange).
            if (_statYearTime == null) return;
            float yt = StatAccess.GetCurrentValue(_statYearTime);
            if (float.IsNaN(yt) || yt < LateSmrMin || yt > LateSmrMax) { _witheringFiredThisLateSummer = false; return; }
            if (_witheringFiredThisLateSummer) return;
            _witheringFiredThisLateSummer = true;
            StatAccess.ModifyCurrentValue(_statForagingAid, -20f);
        }

        // ── Environment check ───────────────────────────────────────────────────

        // Returns true when the player is in an open CT4 environment (outdoors).
        private static bool IsPlayerOutdoors()
        {
            var envUid = GameQuery.CurrentEnvironmentUniqueId;
            if (string.IsNullOrEmpty(envUid)) return false;

            var envCard = CardUtil.GetCardDataById(envUid);
            if (envCard == null) return false;

            var ct = Reflect.GetMember(envCard, "CardType");
            if (ct is int cardType) return cardType == 4;   // CT4 = open Environment node
            if (ct is Enum ctEnum) return Convert.ToInt32(ctEnum) == 4;
            return false; // can't determine — safe default (no debuff)
        }

        // ── Stat helpers ─────────────────────────────────────────────────────────

        private static void EnsureStatsCached()
        {
            if (_statsCached) return;
            _statsCached = true;
            _statStress      = GetStatById(GuidStress);
            _statFear        = GetStatById(GuidFear);
            _statInsight     = GetStatById(GuidInsight);
            _statMoonPhase   = GetStatById(GuidMoonPhase);
            _statMoonUrge    = GetStatById(GuidMoonUrge);
            _statMoonlight   = GetStatById(GuidMoonlight);
            _statAlcohol     = GetStatById(GuidAlcohol);
            _statNausea      = GetStatById(GuidNausea);
            _statRash        = GetStatById(GuidRash);
            _statForagingAid = GetStatById(GuidForagingAid);
            _statYearTime    = GetStatById(GuidYearTime);
        }

        // Calls UniqueIDScriptable.GetFromID<GameStat>(guid) via the generic method.
        // Legitimate direct call (not hand-rolled CurrentValue/MaxValue reflection) — kept as-is.
        private static object GetStatById(string guid)
        {
            try
            {
                var uidType = AccessTools.TypeByName("UniqueIDScriptable");
                if (uidType == null) return null;
                var getFromId = uidType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (getFromId == null) return null;

                var gameStat = AccessTools.TypeByName("GameStat");
                if (gameStat == null) return null;

                return getFromId.MakeGenericMethod(gameStat).Invoke(null, new object[] { guid });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TraitsTickHandler] GetStatById reflection failed for guid={guid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        // ── DTP reader ──────────────────────────────────────────────────────────

        private static bool _loggedDtpErr;

        private static float GetDtp(object gm)
        {
            if (_dtpMember == null) return 48f; // noon fallback
            try
            {
                object raw = _dtpMember is FieldInfo fi
                    ? fi.GetValue(gm)
                    : ((PropertyInfo)_dtpMember).GetValue(gm);
                return raw is float f ? f : Convert.ToSingle(raw);
            }
            catch (Exception ex)
            {
                if (!_loggedDtpErr) { _loggedDtpErr = true;
                    Plugin.Logger.LogWarning($"[TraitsTickHandler] DTP read failed, defaulting to 48: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
                return 48f;
            }
        }

        // ── Perk presence ───────────────────────────────────────────────────────

        private static bool HasPerk(object gm, string uid)
        {
            var perks = _allPerksField?.GetValue(gm) as IEnumerable;
            if (perks == null) return false;
            foreach (var perk in perks)
                if (perk is UniqueIDScriptable u && uid.Equals(u.UniqueID, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ── Reflection init ─────────────────────────────────────────────────────

        private static bool TryResolveReflection()
        {
            if (_initialized) return _allPerksField != null;
            _initialized = true;

            var asm    = AppDomain.CurrentDomain.GetAssemblies()
                             .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var gmType = asm?.GetType("GameManager");
            if (gmType == null) { Plugin.Logger.LogWarning("[TraitsTickHandler] GameManager type not found"); return false; }

            _allPerksField = AccessTools.Field(gmType, "AllPerks")
                ?? gmType.GetField("<AllPerks>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_allPerksField == null) { Plugin.Logger.LogWarning("[TraitsTickHandler] GameManager.AllPerks field not found"); return false; }

            // Resolve DayTimePoints — try multiple names
            foreach (var name in DtpPropertyNames)
            {
                var fi = gmType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) { _dtpMember = fi; break; }
                var pi = gmType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null) { _dtpMember = pi; break; }
            }

            return true;
        }
    }
}
