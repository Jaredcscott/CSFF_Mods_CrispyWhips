using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// C3, the mines are dark (MASTER_PLAN.md Part II section 10). The darkness itself is data:
    /// act_mine_darkness (CT13) sits on the board of four ACT caves with InDarkPlace +1, which
    /// switches off every vanilla daylight entry on GameStat Light (each requires InDarkPlace 0),
    /// and act_mine_daylight_shaft does the same in the Metal Mines. This pass does the two things
    /// JSON cannot:
    ///
    /// 1. Sets IsDarkPlace on the five cave environment cards. The caves clone Env_WaterfallCaves,
    ///    which has it false, and SpecialEffectsManager darkens the screen by the Light stat ONLY
    ///    when the current env card has IsDarkPlace set; without it a cave reads Light 0 and still
    ///    looks lit. Visual only: nothing else in the game reads the flag.
    ///
    /// 2. Gives the Metal Mines its daylight opening (owner, 2026-10-02: "a small hole which is
    ///    letting just enough light to see DURING THE DAY"; the Metal Mines is where a portal
    ///    arrival lands and digs out). Appends one TimeOfDayMods entry to Light: +ShaftLight from
    ///    ShaftStartHour to ShaftEndHour while act_mine_daylight_shaft is on the current board.
    ///    StatTimeOfDayModifier.CardsAndTagsValid tests CardIsOnBoard(card, ..., CurrentEnvironment),
    ///    so the entry is live only in the cave that holds the shaft. The hour range is one no
    ///    vanilla Light entry uses, because GameManager.UpdateTimeOfDayMods skips an entry whose
    ///    TimeRange equals one already active.
    ///
    /// Fail-safe: if the Light entry cannot be added, the shaft's own InDarkPlace effect is removed,
    /// so the Metal Mines keeps vanilla daylight rather than trapping a player who has no light
    /// behind the light-gated "Dig Through" walls.
    ///
    /// Reflection only: ACT's DLL carries no typed game references (root CLAUDE.md, Game-Update
    /// Reference Refresh), and this keeps it that way.
    /// </summary>
    internal static class MineDarknessPatch
    {
        internal const string LightStatUid = "79290cafb08e48f4d871704c20e69b1c";
        internal const string ShaftUid = "act_mine_daylight_shaft";

        // Tunable: Light 15 clears the 10..100 gate on the mine and dig actions and sits in
        // Light's "Light" status band (11..50), so the shaft lets the player see and nothing more.
        internal const float ShaftLight = 15f;
        // 08:00 to 16:00 sits inside every season's vanilla daylight window (winter's shortest is
        // 07:00 to 17:00) and matches no vanilla Light entry's range.
        internal const int ShaftStartHour = 8;
        internal const int ShaftEndHour = 16;

        private static readonly string[] CaveEnvUids =
        {
            "actCopperCaveEnv", "actTinCaveEnv", "actIronCaveEnv", "actSaltMineEnv", "actRockQuarryEnv",
        };

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static ManualLogSource Logger => Plugin.Logger;

        internal static void Apply(IEnumerable allData)
        {
            var wanted = new HashSet<string>(CaveEnvUids, StringComparer.Ordinal) { LightStatUid, ShaftUid };
            var byUid = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                foreach (var item in allData)
                {
                    if (item == null) continue;
                    var uid = item.GetType().GetField("UniqueID", Flags)?.GetValue(item) as string;
                    if (uid != null && wanted.Contains(uid)) byUid[uid] = item;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] MineDarkness: AllData scan failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }

            MarkCavesDark(byUid);

            bool added;
            try { added = AddShaftDaylight(byUid); }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] MineDarkness: daylight shaft entry threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
                added = false;
            }
            if (!added) DisarmShaftDarkness(byUid);
        }

        private static void MarkCavesDark(Dictionary<string, object> byUid)
        {
            int marked = 0;
            foreach (var envUid in CaveEnvUids)
            {
                try
                {
                    if (!byUid.TryGetValue(envUid, out var env))
                    {
                        Logger.LogWarning($"[ACT] MineDarkness: cave env '{envUid}' not in AllData; it will read Light 0 but not look dark.");
                        continue;
                    }
                    var field = env.GetType().GetField("IsDarkPlace", Flags);
                    if (field == null || field.FieldType != typeof(bool))
                    {
                        Logger.LogWarning($"[ACT] MineDarkness: {env.GetType().Name}.IsDarkPlace not found; the caves will not look dark.");
                        return;
                    }
                    if (!(bool)field.GetValue(env)) field.SetValue(env, true);
                    marked++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[ACT] MineDarkness: could not mark '{envUid}' dark: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            Logger.LogDebug($"[ACT] MineDarkness: IsDarkPlace set on {marked}/{CaveEnvUids.Length} cave env cards.");
        }

        // Returns true when Light carries the shaft entry (added now or already present).
        private static bool AddShaftDaylight(Dictionary<string, object> byUid)
        {
            if (!byUid.TryGetValue(LightStatUid, out var light))
            {
                Logger.LogWarning("[ACT] MineDarkness: GameStat Light not in AllData; the Metal Mines keeps vanilla daylight.");
                return false;
            }
            if (!byUid.TryGetValue(ShaftUid, out var shaft))
            {
                Logger.LogWarning($"[ACT] MineDarkness: '{ShaftUid}' not in AllData; no daylight opening to add.");
                return false;
            }

            var modsField = light.GetType().GetField("TimeOfDayMods", Flags);
            var modType = modsField?.FieldType.GetElementType();
            if (modsField == null || modType == null)
            {
                Logger.LogWarning("[ACT] MineDarkness: GameStat.TimeOfDayMods not found or not an array; the Metal Mines keeps vanilla daylight.");
                return false;
            }

            var startField = modType.GetField("StartingHour", Flags);
            var endField = modType.GetField("EndingHour", Flags);
            var cardsField = modType.GetField("RequiredCardsOrTagsOnBoard", Flags);
            var statsField = modType.GetField("RequiredStatValues", Flags);
            var valueField = modType.GetField("ValueModifier", Flags);
            var rateField = modType.GetField("RateModifier", Flags);
            var refType = cardsField?.FieldType.GetElementType();
            var targetField = refType?.GetField("Target", Flags);
            if (startField == null || endField == null || cardsField == null || statsField == null
                || valueField == null || rateField == null || refType == null || targetField == null)
            {
                Logger.LogWarning("[ACT] MineDarkness: StatTimeOfDayModifier/CardOrTagRef fields not found; the Metal Mines keeps vanilla daylight.");
                return false;
            }

            var existing = modsField.GetValue(light) as Array ?? Array.CreateInstance(modType, 0);
            foreach (var m in existing)
            {
                if ((int)startField.GetValue(m) != ShaftStartHour || (int)endField.GetValue(m) != ShaftEndHour) continue;
                if (cardsField.GetValue(m) is Array refs)
                    foreach (var r in refs)
                        if (ReferenceEquals(targetField.GetValue(r), shaft)) return true;
                Logger.LogWarning($"[ACT] MineDarkness: a Light entry already uses {ShaftStartHour}:00-{ShaftEndHour}:00; the shaft entry would be skipped as a duplicate range. The Metal Mines keeps vanilla daylight.");
                return false;
            }

            var cardRef = Activator.CreateInstance(refType);
            targetField.SetValue(cardRef, shaft);
            var refArray = Array.CreateInstance(refType, 1);
            refArray.SetValue(cardRef, 0);

            object mod = Activator.CreateInstance(modType);
            startField.SetValue(mod, ShaftStartHour);
            endField.SetValue(mod, ShaftEndHour);
            cardsField.SetValue(mod, refArray);
            statsField.SetValue(mod, Array.CreateInstance(statsField.FieldType.GetElementType(), 0));
            valueField.SetValue(mod, new UnityEngine.Vector2(ShaftLight, ShaftLight));
            rateField.SetValue(mod, UnityEngine.Vector2.zero);

            var grown = Array.CreateInstance(modType, existing.Length + 1);
            Array.Copy(existing, grown, existing.Length);
            grown.SetValue(mod, existing.Length);
            modsField.SetValue(light, grown);

            Logger.LogDebug($"[ACT] MineDarkness: Light gains +{ShaftLight} from {ShaftStartHour}:00 to {ShaftEndHour}:00 while '{ShaftUid}' is on the board ({grown.Length} entries).");
            return true;
        }

        private static void DisarmShaftDarkness(Dictionary<string, object> byUid)
        {
            try
            {
                if (!byUid.TryGetValue(ShaftUid, out var shaft)) return;
                var field = shaft.GetType().GetField("PassiveStatEffects", Flags);
                if (field == null || !field.FieldType.IsArray)
                {
                    Logger.LogError($"[ACT] MineDarkness: could not clear '{ShaftUid}' PassiveStatEffects; the Metal Mines is dark with no daylight opening.");
                    return;
                }
                field.SetValue(shaft, Array.CreateInstance(field.FieldType.GetElementType(), 0));
                Logger.LogWarning($"[ACT] MineDarkness: daylight opening unavailable; '{ShaftUid}' no longer darkens the Metal Mines, which keeps vanilla daylight.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] MineDarkness: fail-safe threw; the Metal Mines may be dark with no daylight opening: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
