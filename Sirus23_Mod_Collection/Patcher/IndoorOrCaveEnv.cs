using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Shared "is this destination environment a cave/mine/tunnel or man-made structure" check —
/// the single fact ("birds don't belong underground/indoors") behind two different engine entry
/// points: <see cref="CompanionStayPatch"/> (tamed Owl companion follow-along on
/// GameManager.ChangeEnvironment) and <see cref="WildOwlDutyGuardPatch"/> (wild Owl's nightly
/// seek-player teleport duty on MoveDutyAction.CanBePerformed). Extracted so the UID/tag
/// allowlist below has exactly one place to grow.
/// </summary>
internal static class IndoorOrCaveEnv
{
    // Secondary/future-proofing signal only — see EnvUids doc comment below. Real vanilla
    // CardTagsWarpData for these environments ships as a single obfuscated Unity asset name
    // in the static JSON export (not a resolvable tag string), so this can't be trusted as the
    // sole check; it still fires correctly for content that self-declares one of these names
    // directly (e.g. Community_Mod_Chest's interiors, which carry "tag_EnvIndoors" explicitly).
    private static readonly string[] Tags = { "tag_Cave", "tag_EnvCaveSystem", "tag_EnvIndoors" };

    /// <summary>
    /// Primary signal. A UID allowlist rather than a CardTags check because, confirmed while
    /// diagnosing the original companion-follow fix: (1) vanilla cave/tunnel/mine CardData ships
    /// CardTagsWarpData as a single obfuscated Unity asset name (e.g. "NonDrawingGraphic_7661")
    /// in the static JSON export — not independently verifiable offline, and (2)
    /// Community_Mod_Chest's 7 building interiors shipped with ZERO CardTags at all before that
    /// same fix pass added "tag_EnvIndoors" to their JSON directly. UIDs pulled from
    /// Documentation/GameData/CSFF-JsonData_Current (vanilla) and each mod's own
    /// WorldMap/MapNodes.json (clone caves get a fresh UID and do not inherit the clone source's
    /// tags — CardCloneService.CloneCard, see reference_alwaysupdate_env_node_follow).
    /// </summary>
    private static readonly string[] EnvUids =
    {
        // Vanilla cave / mine / tunnel network
        "0d597c607faea644792819dd69735016", // Bear Cave
        "41d2fb7700db4614b8ae12b526d8ec24", // Great Cave (Old Hollow)
        "3ddd45aa9e1c28d48bec644a7bba37e0", // Cave (Pine Slopes)
        "75511a9fcdc45f647bc032eed359ef44", // Cave (River Pass)
        "45c5db6a28297e44b8eba819f391e0f8", // Secret Cave (Shady Thicket)
        "0b397533399cd9243ab2f3a77557f93b", // Still Cave
        "75a9d5c8dd8989a48a33cb7616db4226", // Entrance Tunnel (Death Crack)
        "86e4aee0a55ea3e439921af41431630a", // Collapsed Tunnel
        "6befcb14221b6e249819e2dc206ed641", // Dark Tunnel (North East)
        "f3546cee72796a74e8dc6dd59414e6b9", // Dark Tunnel (North West)
        "45fc009b78bcc1345a4c970df6a552fc", // Dark Tunnel (South East)
        "6fa92975cc96fd14fa6fafa18e46be23", // Dark Tunnel (South West)
        "2f37a6886d614d741ab4943136565946", // Dark Tunnel (West)
        "e5ff4e632df90e643b4d968b4c55d6f8", // Flint Sett
        "e4da35707a07fec468cde1d2cc6ee0b7", // Flooded Tunnel
        "92f0475eba969b54782c9092beb0a67b", // Northern Tunnel
        "2b8eed1c14cc78d429d9633728d8e7ab", // Ominous Tunnel
        "893bfd37608d3d2438f49cf349f5f4d8", // Overgrown Tunnel
        "03d28e9e8f09c48479c24dbe2cd88c77", // Southern Tunnel
        "04c947daf3e247b4fb52fe98895d27de", // Flintclaw's Chamber
        "a2511a2a8fcba7b4aa35e87c700b94c0", // Northern Chamber
        "24b659a069e11e54495f75d4d564f038", // The Shaft
        "17d6cfe8ff886b643afee74dc18760a7", // Waterfall Caves
        "63bf07510e1a58d448cb36effe180a91", // Wolf Cave
        "2f8328a25c72568489c1070e84093a5c", // Copper Mine (built)
        "c3cf346faa68eea4ea2598946e195f3d", // Flint Mine (built)
        "51acacfbe84f3d249b4de8ed3b89f067", // Witchstone Mine (built)
        "cb9db3f516c06de4d899052203bc5182", // Copper Mine (construction site)
        "04e4396d30da5854b994ef210f041c8c", // Flint Mine (construction site)
        "ecb76c482ca6a4d43a9e62d4a32ca5b0", // Witchstone Mine (construction site)

        // Vanilla player-built home/shelter — also man-made structures
        "73cc0442edacf1e4f8cb5a5ee66bf7a9", // Cabin
        "f80156a6230780c4cb2cb7a1be5497b5", // Cabin Attic
        "582ef9725bd3e4941a61a95558f6c98c", // Cabin Room
        "3b25ec2297c2d4e42a41f5c4b40e32d2", // Cabin (construction site)
        "a9f7d5db7ddf9f14d88c7fb9e0f2c68a", // Cellar
        "b60b06a6405702a45bf2e3985e76d213", // Mud Hut
        "2574f8bb87e2c07419f951e58adb87dd", // Mud Hut Cellar (built)
        "a98be0703900d5f45a89c3990c370138", // Mud Hut Enclosure (built)
        "4fe8e153fe88de74baa6b6f574e7bb39", // Mud Hut Room (built)
        "2ed6bb44a851062498c3f36294b13ec9", // Mud Hut Cellar (construction site)
        "b2b3c63ab86619d4ba3345d9dedad388", // Mud Hut Enclosure (construction site)
        "073bd924076bc8949b4513cec5f40826", // Mud Hut (construction site)
        "3f89d243941334e4a8e07f953fd4ddd5", // Mud Hut Enclosure
        "dc69d24e60cda004cb484c164bcc0761", // Mud Hut left room expansion
        "d22e2dfb79c4dcf48ba076a6f3a7d1eb", // Mud Hut right room expansion
        "617d3fd9af4c9e4499e62d2b3250dcd9", // Coop

        // Community_Mod_Chest — village building interiors (also carry "tag_EnvIndoors"
        // directly; listed here too as a redundant, verifiable-offline signal)
        "cmcAcademyInterior",
        "cmcApothecaryCabinInterior",
        "cmcInnInterior",
        "cmcEnvJailCell",
        "cmcMillerCottageInterior",
        "cmcVillageHallInterior",
        "cmcWeaverCottageInterior",

        // AdvancedCopperTools — WorldMap clone caves (all clone vanilla Waterfall Caves;
        // clone nodes get a fresh UID and don't inherit the source's tags)
        "actCopperCaveEnv",
        "actTinCaveEnv",
        "actIronCaveEnv",
        "actSaltMineEnv",
        "actRockQuarryEnv",
    };

    private static HashSet<string> _envUidSet;

    /// <summary>
    /// True if <paramref name="envCard"/> is a known cave/mine/tunnel or man-made structure
    /// (checked primarily by UID, with a CardTags scan as a secondary signal). Returns false
    /// (never throws) on a null card or any reflection failure.
    /// </summary>
    internal static bool Matches(object envCard)
    {
        if (envCard == null) return false;
        try
        {
            string uid = CardUtil.GetCardUniqueId(envCard);

            _envUidSet ??= new HashSet<string>(EnvUids, StringComparer.Ordinal);
            if (uid != null && _envUidSet.Contains(uid)) return true;

            if (Reflect.GetMember(envCard, "CardTags") is IEnumerable tags)
            {
                foreach (var tag in tags)
                {
                    if (tag is UnityEngine.Object tagObj && tagObj != null && Array.IndexOf(Tags, tagObj.name) >= 0)
                        return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug($"[IndoorOrCaveEnv] Matches check failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }
}
