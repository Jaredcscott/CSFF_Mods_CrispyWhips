using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Herbs_And_Fungi;

[BepInDependency("crispywhips.CSFFModFramework", "2.0.0")]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.Herbs_And_Fungi";
    public const string PluginName = "Herbs and Fungi";
    public const string PluginVersion = "1.6.4";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    private void Awake()
    {
        Instance = this;

        // Set up logger for static access
        Logger = base.Logger;
        
        // Initialize and apply Harmony patches
        _harmony = new Harmony(PluginGuid);
        try
        {
            Herbs_And_Fungi.Patcher.GameLoadPatch.ApplyPatch(_harmony);
            Herbs_And_Fungi.Patcher.TruffleFatCookPatch.ApplyPatch(_harmony);
            Herbs_And_Fungi.Patcher.EncounterSuppressPatch.ApplyPatch(_harmony);
            Herbs_And_Fungi.Patcher.PickleVatRoutePatch.ApplyPatch(_harmony);
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}