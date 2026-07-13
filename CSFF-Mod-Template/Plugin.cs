using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

// TODO: Change this namespace to match your mod's AssemblyName (e.g., "My_Cool_Mod").
//       Must match the RootNamespace in your .csproj file.
namespace TODO_ModName;

// TODO: Fill in PluginGuid, PluginName, and PluginVersion below.
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    // TODO: Change to "yourname.mod_name" — lowercase, dots OK, globally unique.
    //       Example: "crispywhips.herbs_and_fungi"
    private const string PluginGuid = "yourname.TODO_ModName";

    // TODO: Human-readable name shown in BepInEx logs. Example: "Herbs and Fungi"
    public const string PluginName = "TODO Mod Display Name";

    // TODO: Keep in sync with ModInfo.json and README.md.
    public const string PluginVersion = "1.0.0";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _harmony = new Harmony(PluginGuid);
        try
        {
            // TODO: Call ApplyPatch for each Harmony patch class you add.
            //       If your mod is JSON-only (no runtime C# patches), remove this
            //       entire try block and delete Patcher/GameLoadPatch.cs.
            TODO_ModName.Patcher.GameLoadPatch.ApplyPatch(_harmony);

            // One startup Info line per mod — the expected baseline for clean loads.
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    // Always keep this — it unpaches Harmony when the plugin unloads.
    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
