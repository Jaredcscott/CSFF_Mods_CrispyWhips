using System.Collections;
using HarmonyLib;
using CSFFModFramework.Patching;
using CSFFModFramework.Util;

namespace CSFFModFramework.Portal;

// No-op patch — portal travel now uses game-default ChangeEnvironment behavior:
// slot items (hand/equipment) travel with the player; floor/placed items stay at source.
internal static class PortalTravelPatch
{
    public static void ApplyPatch(Harmony harmony) { }
}
