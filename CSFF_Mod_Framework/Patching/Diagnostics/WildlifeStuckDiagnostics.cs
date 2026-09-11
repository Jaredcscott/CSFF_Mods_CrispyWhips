using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in diagnostic for investigating player reports of vanilla wildlife (bears, wolves)
/// getting permanently stuck on mod-injected WorldMap trail nodes with no way back to a
/// vanilla environment. Root cause is genuinely unconfirmed between two candidates:
/// (a) <c>WorldMapData.MapDict</c> staleness for pathfinding-driven movement onto/off a
/// clone node (see <see cref="Injection.WorldMapInjector.RebuildPathfindingLookup"/>'s doc
/// comment — that fix targets NPCDuty's A* pathfinding specifically; whether vanilla
/// wildlife AI shares the same MapDict-backed pathfinding call is unverified), or (b)
/// something in how a mod-injected trail node's edges/exits are seeded, leaving a dead end
/// wildlife AI can wander into but never route back out of. Per CLAUDE.md Debugging
/// Discipline, this logs raw facts only — it does NOT attempt a fix. Once a player
/// reproduces the stuck case with this enabled and the log confirms which mechanism is at
/// fault, demote/remove this class and implement the real fix.
///
/// Off by default. Enable via BepInEx config:
///   [Diagnostics] LogWildlifeStuck = true
/// </summary>
internal static class WildlifeStuckDiagnostics
{
    // Confirmed via Documentation/GameData/.../UniqueIDScriptableGUID/NPCAgent.json —
    // the live (non "NOT_USED_") vanilla wildlife agents most likely to roam onto a
    // mod-injected forest/trail node.
    private static readonly HashSet<string> WatchedAgentUids = new(StringComparer.OrdinalIgnoreCase)
    {
        "61dd2882b59388346b42628f5e76432b", // Agent_Bear
        "0cb137c5a8daaed47ba291779d8146f8", // Agent_Wolf
        "c3ac5867e8021ce43a5094a2bb469935", // Agent_WolfPack
        "d1602c74669f2e642983a004355a3f81", // Agent_PrimevalWolf
    };

    private sealed class Watch
    {
        public object Npc;          // live InGameNPC instance
        public string LastEnvUid;
        public int SameEnvTicks;
    }

    private static readonly Dictionary<object, Watch> _watches = new();
    private static BepInEx.Configuration.ConfigEntry<bool> _enabled;
    private static Type _npcType;
    private static System.Reflection.PropertyInfo _modelProp;
    private static System.Reflection.FieldInfo _envField;
    private static int _tickCounter;
    private static bool _subscribed;

    public static void Configure(BepInEx.Configuration.ConfigFile config)
    {
        _enabled = config.Bind("Diagnostics", "LogWildlifeStuck", false,
            "When true, watches live vanilla Bear/Wolf/WolfPack/PrimevalWolf agents and logs "
            + "how many consecutive DTP ticks each has stayed in the same environment. Used to "
            + "confirm and localize player reports of wildlife getting permanently stuck on "
            + "mod-injected WorldMap trail nodes. Temporary diagnostic — no behavior is changed.");

        if (!_enabled.Value) return;
        if (_subscribed) return;
        TickEvents.DtpTick += OnTick;
        _subscribed = true;
        Log.Info("WildlifeStuckDiagnostics: enabled — watching Bear/Wolf/WolfPack/PrimevalWolf for stuck-in-place behavior");
    }

    private static void OnTick()
    {
        if (_enabled?.Value != true) return;

        try
        {
            _tickCounter++;

            var gm = MBSingleton<GameManager>.Instance;
            if (gm?.AllNPCs == null) return;

            _npcType ??= AccessTools.TypeByName("InGameNPC");
            if (_npcType == null) return;
            _modelProp ??= AccessTools.Property(_npcType, "NPCModel");
            _envField ??= AccessTools.Field(_npcType, "CurrentEnvironment");
            if (_modelProp == null || _envField == null) return;

            // Refresh the watch list every 10 ticks (catch newly-spawned/despawned individuals)
            // instead of rescanning AllNPCs every tick (CLAUDE.md: cached-reference pattern).
            if (_tickCounter % 10 == 1)
            {
                foreach (var stale in _watches.Keys.Where(n => !Reflect.IsAlive(n)).ToList())
                    _watches.Remove(stale);

                foreach (var npc in gm.AllNPCs)
                {
                    if (npc == null || _watches.ContainsKey(npc)) continue;
                    var model = _modelProp.GetValue(npc) as NPCAgent;
                    if (model == null || !WatchedAgentUids.Contains(model.UniqueID)) continue;
                    _watches[npc] = new Watch { Npc = npc };
                    Log.Info($"WildlifeStuckDiagnostics: now watching {model.name} ({model.UniqueID})");
                }
            }

            foreach (var kv in _watches)
            {
                var npc = kv.Key;
                var w = kv.Value;
                if (!Reflect.IsAlive(npc)) continue;

                var envObj = _envField.GetValue(npc);
                string envUid = ExtractEnvUid(envObj);
                if (envUid == null) continue;

                if (envUid == w.LastEnvUid)
                {
                    w.SameEnvTicks++;
                }
                else
                {
                    if (w.LastEnvUid != null && w.SameEnvTicks >= 20)
                        Log.Info($"WildlifeStuckDiagnostics: agent left env '{w.LastEnvUid}' after {w.SameEnvTicks} ticks, now in '{envUid}'");
                    w.LastEnvUid = envUid;
                    w.SameEnvTicks = 0;
                }

                // Log every 20 ticks (5 in-game hours) so a genuinely stuck agent surfaces
                // repeatedly in the log without spamming every single DTP tick.
                if (w.SameEnvTicks > 0 && w.SameEnvTicks % 20 == 0)
                {
                    var model = _modelProp.GetValue(npc) as NPCAgent;
                    Log.Info($"WildlifeStuckDiagnostics: {model?.name} ({model?.UniqueID}) has stayed in env "
                        + $"'{envUid}' for {w.SameEnvTicks} consecutive DTP ticks — possible stuck case if this env "
                        + "is a mod-injected node (recognizable by a non-GUID UniqueID, e.g. cmcLocPineTrail, hfLocForagingPath).");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WildlifeStuckDiagnostics: tick failed: {Log.ExceptionText(ex)}");
        }
    }

    private static string ExtractEnvUid(object envObj)
    {
        if (envObj == null) return null;
        var uid = Reflect.GetMember(envObj, "UniqueID") as string;
        if (!string.IsNullOrEmpty(uid)) return uid;

        var mainEnvCard = Reflect.GetMember(envObj, "MainEnvCard");
        uid = Reflect.GetMember(mainEnvCard, "UniqueID") as string;
        if (!string.IsNullOrEmpty(uid)) return uid;

        return envObj.ToString();
    }
}
