using CSFFModFramework.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in catch-up end-state dump (round-2 plan feature 23). After each travel whose replay ran at
/// least one catch-up tick, logs ONE Info block listing the destination board's cards (cap 200, ordered
/// by UniqueID, then board order, at most 8 of one UniqueID) with every durability value, the liquid
/// quantity and their per-tick rates. Two runs of the
/// same stale route at <c>CatchUpTickBatching.BatchSizeOverride</c> 0 and then 16 give two blocks that an
/// offline differ compares with a tolerance of K-1 ticks of each card's rate, which is what proves the
/// shipped batching changed decay granularity only (CatchUp_Performance_As_Built.md "Phase 3").
///
/// Off by default, patched only when armed:
///   [Diagnostics] LogCatchUpEndState = true
/// Line format (one card per line, fixed field order so a differ can split on spaces): the end values,
/// then each stat's current per-tick rate (the differ's tolerance is K-1 ticks of it):
///   <c>uid sp=.. us=.. fu=.. pr=.. s1=.. s2=.. s3=.. s4=.. liq=.. rsp=.. rus=.. rfu=.. rpr=.. rs1=.. rs2=.. rs3=.. rs4=.. rliq=..</c>
/// Only the cards the catch-up replays are listed: not <c>InBackground</c>, not <c>IndependentFromEnv</c>,
/// and in the player's (destination) environment. AllCards also holds every other environment's
/// left-behind cards (1,707 of 2,689 on the W0-7 save) plus blueprints, equipment and carried cards, which
/// filled 150-plus of the 200 UID-ordered rows until this filter (Prompt 27, 2026-10-09).
/// </summary>
internal static class CatchUpEndStateDiagnostics
{
    private const int CardCap = 200;
    private const int PerUidCap = 8;

    private static int _inChangeEnvironment;
    private static int _applyRatesCount;
    private static int _callIndex;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogCatchUpEndState", false,
            "When true, after each travel that replayed catch-up ticks, logs one block listing the "
            + "destination board's cards (up to 200, ordered by UniqueID) with every durability value "
            + "and liquid quantity. Used to compare the shipped catch-up batching against exact "
            + "per-tick replay. Patches are installed only when this is true. Off by default.",
            "为 true 时，每次旅行补算了错过的时间刻后，记录一段目的地版面上的卡牌列表（最多 200 张，按 UniqueID 排序），包含每个耐久值和液体量。用于比较已发布的补算分批与逐刻精确补算。只有为 true 时才会安装相关补丁。默认关闭。");
        if (!enabled.Value) return;

        bool a = SafePatcher.TryPatch(harmony, "GameManager", "ChangeEnvironment",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CatchUpEndStateDiagnostics), nameof(ChangeEnvironment_Postfix))));
        bool b = SafePatcher.TryPatch(harmony, "GameManager", "ApplyRates",
            prefix: new HarmonyMethod(AccessTools.Method(typeof(CatchUpEndStateDiagnostics), nameof(ApplyRates_Prefix))));
        Util.Log.Info($"CatchUpEndStateDiagnostics: enabled (ChangeEnvironment={a}, ApplyRates={b}); one block per catch-up travel.");
    }

    static void ApplyRates_Prefix()
    {
        if (Volatile.Read(ref _inChangeEnvironment) > 0) Interlocked.Increment(ref _applyRatesCount);
    }

    static IEnumerator ChangeEnvironment_Postfix(IEnumerator result, GameManager __instance)
    {
        Interlocked.Increment(ref _inChangeEnvironment);
        int ratesBefore = Volatile.Read(ref _applyRatesCount);
        try
        {
            while (result.MoveNext()) yield return result.Current;
        }
        finally
        {
            Interlocked.Decrement(ref _inChangeEnvironment);
            int calls = Volatile.Read(ref _applyRatesCount) - ratesBefore;
            if (calls > 0)
            {
                try { Util.Log.Info(BuildBlock(__instance, calls)); }
                catch (Exception ex) { Util.Log.Warn($"CatchUpEndStateDiagnostics: end-state block failed: {ex.Message}"); }
            }
        }
    }

    private static string BuildBlock(GameManager gm, int applyRatesCalls)
    {
        int index = Interlocked.Increment(ref _callIndex);
        string env = "?";
        try { env = gm != null ? gm.CurrentEnvironment.ToString() : "?"; }
        catch (Exception ex) { Util.Log.Debug($"CatchUpEndStateDiagnostics: env label read failed: {ex.Message}"); }

        var rows = new List<(string uid, int order, string line)>();
        var all = gm?.AllCards;
        int total = 0, background = 0, notOnBoard = 0, rateFailures = 0;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c || !c.CardModel) continue;
                if (c.InBackground) { background++; continue; }
                // Only the cards the catch-up replays: on the destination board and not independent of
                // it. Blueprints, equipment, the hand and carried cards are none of these.
                if (c.IndependentFromEnv || !c.CardEnvironment.MatchesPlayerEnv) { notOnBoard++; continue; }
                total++;
                string uid = c.CardModel.UniqueID ?? "?";
                string rates;
                try
                {
                    rates = $" rsp={c.CurrentSpoilageRate:F4} rus={c.CurrentUsageRate:F4} rfu={c.CurrentFuelRate:F4} "
                            + $"rpr={c.CurrentConsumableRate:F4} rs1={c.CurrentSpecial1Rate:F4} rs2={c.CurrentSpecial2Rate:F4} "
                            + $"rs3={c.CurrentSpecial3Rate:F4} rs4={c.CurrentSpecial4Rate:F4} rliq={c.CurrentEvaporationRate:F4}";
                }
                catch (Exception ex)
                {
                    rateFailures++;
                    rates = " rates=?";
                    Util.Log.Debug($"CatchUpEndStateDiagnostics: rate read failed for {uid}: {ex.Message}");
                }
                rows.Add((uid, i, $"{uid} sp={c.CurrentSpoilage:F3} us={c.CurrentUsageDurability:F3} fu={c.CurrentFuel:F3} "
                                  + $"pr={c.CurrentProgress:F3} s1={c.CurrentSpecial1:F3} s2={c.CurrentSpecial2:F3} "
                                  + $"s3={c.CurrentSpecial3:F3} s4={c.CurrentSpecial4:F3} liq={c.CurrentLiquidQuantity:F3}" + rates));
            }
        }
        rows.Sort((x, y) =>
        {
            int c = string.CompareOrdinal(x.uid, y.uid);
            return c != 0 ? c : x.order.CompareTo(y.order);
        });

        // At most PerUidCap cards of one UniqueID (the first in board order), so a pile of 180 Rotten
        // Remains cannot fill the whole sample with one card kind.
        var picked = new List<string>(CardCap);
        string lastUid = null;
        int ofUid = 0;
        foreach (var r in rows)
        {
            if (picked.Count >= CardCap) break;
            if (r.uid != lastUid) { lastUid = r.uid; ofUid = 0; }
            if (++ofUid > PerUidCap) continue;
            picked.Add(r.line);
        }
        int shown = picked.Count;
        var sb = new StringBuilder();
        sb.Append($"CatchUpEndState#{index}[{env}]: batchOverride={Performance.CatchUpTickBatching.BatchSizeOverride}, "
                  + $"batched={Performance.CatchUpTickBatching.Armed}, applyRatesCalls={applyRatesCalls}, cards={total}, "
                  + $"background={background}, notOnBoard={notOnBoard}, rateFailures={rateFailures}, perUidCap={PerUidCap}, shown={shown}");
        foreach (var line in picked) sb.Append('\n').Append(line);
        return sb.ToString();
    }
}
