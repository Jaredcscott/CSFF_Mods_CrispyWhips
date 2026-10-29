using CSFFModFramework.Util;
namespace CSFFModFramework;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "crispywhips.CSFFModFramework";
    public const string PluginName = "CSFF Mod Framework";
    public const string PluginVersion = "2.28.10";

    public static Plugin Instance { get; private set; }
    internal new static ManualLogSource Logger { get; private set; }
    internal static Harmony Harmony { get; private set; }

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        Util.Log.Init(Logger);

        // Config: verbose logging (default off — suppresses per-item diagnostic spam)
        var verboseConfig = Config.Bind("General", "VerboseLogging", false,
            "Enable verbose/debug logging. Shows per-item diagnostic traces in LogOutput.log.",
            "启用详细/调试日志。在 LogOutput.log 中显示逐项的诊断跟踪。");
        Util.Log.Verbose = verboseConfig.Value;

        // Config: force Chinese localization (default off — override for testing without a Chinese game install)
        var forceChineseConfig = Config.Bind("General", "ForceChineseMode", false,
            "When true, loads Chinese (SimpCn.csv) regardless of game language setting. "
            + "Use to test Chinese translations without changing your system language.",
            "为 true 时，无论游戏语言设置如何都加载中文（SimpCn.csv）。用于在不更改系统语言的情况下测试中文翻译。");
        Loading.LocalizationLoader.ForceChineseMode = forceChineseConfig.Value;

        // Config: load-time diagnostics (default off — two full AllData scans around WarpResolver)
        var diagConfig = Config.Bind("General", "EnableLoadDiagnostics", false,
            "Run blueprint research diagnostic passes around WarpResolver. "
            + "Off by default — enable only when investigating research-timer regressions.",
            "在 WarpResolver 前后运行蓝图研究诊断。默认关闭，仅在排查研究计时回归问题时启用。");
        Loading.LoadOrchestrator.EnableLoadDiagnostics = diagConfig.Value;

        // Config: load-time authoring lint (default on: one Warn per affected mod, silent otherwise).
        Loading.AuthoringLint.Configure(Config);

        // Config: wildlife raid system (opt-in; injects tag_NotSafeFromAnimals and
        // tag_NotSafeFromBears on vanilla open-storage containers at load time).
        var raidEnabled = Config.Bind("Wildlife", "WildlifeRaidsEnabled", false,
            "Once per in-game day, roll a chance that wildlife raids an unguarded container "
            + "(any structure tagged tag_NotSafeFromAnimals) in the player's environment. "
            + "Bear encounters also roll a separate raid chance against any container tagged "
            + "tag_NotSafeFromAnimals or tag_NotSafeFromBears. "
            + "Disabled by default — enable to opt in.",
            "每个游戏日掷一次概率，判定野生动物是否会袭击玩家所在环境中无人看守的容器（任何带有 tag_NotSafeFromAnimals 标签的建筑）。遭遇熊时还会另外掷一次概率，袭击任何带有 tag_NotSafeFromAnimals 或 tag_NotSafeFromBears 标签的容器。默认关闭，需要时手动开启。");
        var raidChance = Config.Bind("Wildlife", "WildlifeRaidDailyChance", 0.35f,
            "Probability (0-1) per in-game day that a general wildlife raid is attempted.",
            "每个游戏日尝试一次普通野生动物袭击的概率（0-1）。");
        var bearRaidChance = Config.Bind("Wildlife", "BearRaidChance", 0.5f,
            "Probability (0-1) that a bear encounter also triggers a raid on nearby open containers. "
            + "Sealed containers (Copper Chest, vanilla Chest) are always safe.",
            "遭遇熊时同时袭击附近开放容器的概率（0-1）。密封容器（铜箱、原版箱子）始终安全。");
        var raidStress = Config.Bind("Wildlife", "WildlifeRaidStressPenalty", 2f,
            "Stress added to the player when a raid succeeds.",
            "袭击成功时给玩家增加的压力。");
        Wildlife.WildlifeRaidService.Enabled = raidEnabled.Value;
        Wildlife.WildlifeRaidService.DailyChance = raidChance.Value;
        Wildlife.WildlifeRaidService.BearRaidChance = bearRaidChance.Value;
        Wildlife.WildlifeRaidService.StressPenalty = raidStress.Value;
        var bearEscalationDays = Config.Bind("Wildlife", "BearRaidEscalationDays", 0,
            "Needs WildlifeRaidsEnabled. After this many in-game days in a row on which wildlife raided "
            + "an open container in your location, the next raid is a fight with a brown bear instead of "
            + "another spoiled item. 0 turns it off (the default); 3 is a reasonable start if you want it. "
            + "The count is kept only while the game runs: loading a save or returning to the menu resets it. "
            + "Encounter guards that keep wildlife out of a place (such as Community Mod Chest's village) "
            + "also stop this bear.",
            "需要开启 WildlifeRaidsEnabled。野生动物连续这么多个游戏日袭击你所在地点的开放容器后，下一次袭击将变成与棕熊的战斗，而不是又一件物品变质。0 为关闭（默认）；想要的话 3 是不错的起点。计数只在游戏运行期间保留：读取存档或返回主菜单都会重置。阻止野生动物进入某处的遭遇守卫（例如 Community Mod Chest 的村庄）也会挡住这头熊。");
        Wildlife.WildlifeRaidService.BearRaidEscalationDays = System.Math.Max(0, bearEscalationDays.Value);
        Wildlife.WildlifeRaidService.Init();

        // Config: declarative animal system (on by default — only activates when a mod
        // ships Animals/*.json; see Documentation/Plans/CSFFModFramework/Animal_System_Plan.md).
        var animalsEnabled = Config.Bind("Animals", "AnimalsEnabled", true,
            "Load declarative animal species from mods' Animals/*.json manifests "
            + "(framework-generated NPC agents: spawning, movement, tracks, traps, encounters). "
            + "Disable to skip all framework-generated animals.",
            "从各模组的 Animals/*.json 清单加载声明式动物物种（由框架生成的 NPC 角色：生成、移动、足迹、陷阱、遭遇）。关闭则跳过所有由框架生成的动物。");
        Animals.AnimalService.Enabled = animalsEnabled.Value;

        // Config: vanilla QuestLog auto-injection (OFF by default — this exact path caused a
        // user-confirmed blueprint research reset in CMC 1.7.0 and was never root-caused; see
        // Documentation/Retrospectives/questinjector-blueprint-reset-risk.md). A mod shipping
        // Quests.json will NOT attach any QuestLogs to PlayerCharacter.Quests unless this is set
        // true. Only enable after verifying blueprint research survives a save/reload cycle.
        var questInjectionEnabled = Config.Bind("Quests", "EnableQuestInjection", false,
            "DANGER: attach mod QuestLogs (Quests.json) to PlayerCharacter.Quests. This caused a "
            + "confirmed blueprint research reset on save load in CMC 1.7.0 and the root cause was "
            + "never found. Leave false unless you have verified on a disposable save that blueprint "
            + "research survives a save/reload cycle with the quest mod installed.",
            "危险：把模组的任务日志（Quests.json）附加到 PlayerCharacter.Quests。这曾在 CMC 1.7.0 中确认导致读档时蓝图研究被重置，且根本原因始终没有找到。除非你已在一个可丢弃的存档上验证过安装任务模组后蓝图研究能在存档/读档后保留，否则请保持 false。");
        Injection.QuestInjector.Enabled = questInjectionEnabled.Value;

        // Config: perk origin marker (on by default; owner decision 2026-09-14). Display only:
        // appends " [TAG]" to the rendered name of framework-loaded perks, never to PerkName.
        var perkOriginTag = Config.Bind("Perks", "ShowModOriginTag", true,
            "Mark every perk (trait) added by a mod with a short mod tag after its name, for example "
            + "\"Swimmer [CMC]\", in the character creation perk lists, the selected-perk panel and the "
            + "character sheet, so perks from different mods can be told apart. Vanilla perks are never "
            + "marked. The tag comes from the mod's ModInfo.json \"ShortName\", or the initials of its "
            + "name when it has none. Display only: perk names, saves and stat reports are unchanged. "
            + "Set false to hide the tags. A change needs a full quit to desktop and relaunch, because "
            + "this file is read once at startup.",
            "在角色创建的特质列表、已选特质面板和角色属性页中，给每个由模组添加的特质（特性）名字后面加上简短的模组标记，例如“Swimmer [CMC]”，以便区分来自不同模组的特质。原版特质永远不会被标记。标记取自模组 ModInfo.json 中的“ShortName”，没有时取其名称的首字母。仅影响显示：特质名称、存档和属性报告都不变。设为 false 隐藏标记。更改后需要完全退出到桌面再重新启动，因为此文件只在启动时读取一次。");
        Patching.PerkOriginTagPatch.Enabled = perkOriginTag.Value;

        var cardMotion = Config.Bind("Visuals", "CardMotion", true,
            "Subtle movement on mod art that asks for it: NPCs and animals breathe, location paintings drift "
            + "slowly. Drawn by bending the card's picture, so it uses no extra images or texture memory. "
            + "Set false to keep every card still. Takes effect at the next launch.",
            "为需要的模组插画加入轻微动态：NPC 和动物在呼吸，地点画作缓缓漂移。通过弯曲卡牌图片实现，不占用额外的图片或纹理内存。设为 false 让所有卡牌保持静止。下次启动时生效。");
        var cardMotionRate = Config.Bind("Visuals", "CardMotionUpdatesPerSecond", 20f,
            "How many times a second moving cards are redrawn (5 to 60). Lower costs less; 20 looks smooth "
            + "for movement this small. Takes effect at the next launch.",
            "动态卡牌每秒重绘的次数（5 到 60）。越低开销越小；对这么小的动作来说 20 已经很流畅。下次启动时生效。");
        Motion.CardMotionService.Enabled = cardMotion.Value;
        Motion.CardMotionDriver.TicksPerSecond = cardMotionRate.Value;

        Loading.SpriteTextureCache.CompressEnabled = Config.Bind("Performance", "CompressModSprites", false,
            "Compress cached mod sprites to DXT5 (about 4x less video memory and sprite cache; art can look slightly softer). "
            + "Only sprites whose width and height are multiples of 4 are compressed. Takes effect at the next launch; "
            + "the sprite cache rebuilds once on each change. Default false.",
            "把缓存的模组图片压缩为 DXT5（显存和图片缓存约减少到四分之一；插画可能略显柔和）。只有宽和高都是 4 的倍数的图片才会被压缩。下次启动时生效；每次更改后图片缓存会重建一次。默认 false。").Value;

        Triggers.TriggerService.Init();

        Harmony = new Harmony(PluginGuid);

        // Performance: pre-warm DOTween pool so mid-session expansion doesn't GC-spike
        // when many cards animate at once (inventory shuffles, blueprint open, etc).
        Patching.Performance.DOTweenCapacityPrewarm.Configure(Config);

        // Performance: strip Debug.LogWarning calls from DynamicLayoutSlot.AssignCard —
        // late-game saves with many improvements spam this per-frame during drag/drop.
        Patching.Performance.SlotAssignmentLogSuppress.ApplyPatch(Harmony, Config);

        // Performance: reuse a cached float[3] inside AmbienceImageEffect.Update instead of
        // allocating a fresh array per frame (~1.4 KB/sec of GC churn eliminated).
        Patching.Performance.AmbienceArrayReuse.ApplyPatch(Harmony, Config);

        // Performance: throttle InGameCardBase.LateUpdate for off-screen, non-animating cards.
        // Biggest remaining win for card-heavy saves — hundreds of cards in other environments
        // stop paying per-frame CheckIfVisibleOnScreen cost.
        Patching.Performance.OffScreenCardThrottle.Configure(Config, Harmony);

        // Performance: cap ChangeEnvironment's per-game-tick catch-up replay when
        // entering a long-unvisited environment. Unbounded in vanilla — a year-old
        // location on a late-game save replays ~38k ticks in one synchronous frame
        // (70+ s "Not Responding" freeze). Default cap 1344 ticks = 14 in-game days.
        // Also hands off to the two composable extensions below every travel,
        // regardless of whether the cap itself fires this hop.
        Patching.Performance.CatchUpTickCap.Configure(Config, Harmony);

        // Performance: adaptive wall-clock companion to CatchUpTickCap — bounds a
        // single catch-up replay by elapsed real time (with a tick floor so short
        // perishables always fully rot), instead of always paying the same fixed
        // tick-count worst case. Self-tunes to machine speed and save weight.
        Patching.Performance.CatchUpBudgetClamp.Configure(Config, Harmony);

        // Performance: while the game replays missed ticks on arrival, CharacterScreen.HasCardEquipped
        // answers false without scanning the equipment slots (every card the replay processes is
        // already known not to be equipped). Ships on, behind [Performance] CatchUpEquipmentScanSkip;
        // the log-only verify postfix stays behind LogCatchUpEquipmentScanHits. See
        // CatchUpEquipmentScanSkip.cs and Documentation/Design/CatchUp_Performance_As_Built.md.
        Patching.Performance.CatchUpEquipmentScanSkip.Configure(Config, Harmony);

        // Performance: K-chunked catch-up tick batching, composed with
        // CatchUpTickCap above — the biggest lever in the catch-up performance
        // plan. Groups elapsed catch-up ticks into chunks of K (default 16) and
        // scales/repeats the per-tick work accordingly, cutting a capped
        // 1344-tick replay from ~4.4-6.2s toward an estimated ~0.8-1.1s. See
        // CatchUpTickBatching.cs for the full exactness analysis.
        Patching.Performance.CatchUpTickBatching.Configure(Config, Harmony);

        // Core patches
        Patching.GameLoadPatch.ApplyPatch(Harmony);
        Loading.BlueprintContainerSaveLoadFix.ApplyFreshPlacementPatch(Harmony);
        Patching.LocalizationPatch.ApplyPatch(Harmony);
        Patching.BlueprintFlagFix.ApplyPatch(Harmony);
        Loading.UniqueIdIndexRepair.ApplyPatch(Harmony);
        Injection.TagGroupService.ApplyPatch(Harmony);
        Loading.TypedCacheAllDataSync.ApplyPatch(Harmony);
        Patching.BugFixes.WikiModQuickFindFix.ApplyPatch(Harmony);
        // Finalizer on Harmony's own PatchClassProcessor.Patch: when a WikiMod patch class cannot bind
        // to this game version, skip that ONE class instead of letting the throw abort WikiMod's whole
        // PatchAll (Harmony's type loop has no try/catch, so one stale class silently kills every class
        // after it plus the rest of WikiMod's Awake - 23 of 52 classes on WikiMod 3.5.1 / EA 0.67i,
        // including its card hover tooltip). MUST stay synchronous here: BepInEx loads us 3rd and
        // WikiMod last, and WikiMod's Awake runs before our next frame, so a deferred install is too late.
        Patching.BugFixes.WikiModPatchAllRescue.Configure(Config, Harmony);
        // Prefix on WikiMod's ApplyDurabilityTranslations (the one applier whose table is literal
        // Chinese, plan W0-5): skipped unless the game is in Chinese. Installed when the WikiMod
        // assembly loads, so it is in place before WikiMod's data pass.
        Patching.BugFixes.WikiModEnglishGuard.Configure(Config, Harmony);

        // Bug-fix / compat patches
        // Finalizer that swallows third-party NREs in BlueprintModelsScreen.Show/Toggle
        // (CardSizeReduce's Show_Postfix throws every frame on blueprint unlock).
        Patching.BugFixes.BlueprintScreenFix.ApplyPatch(Harmony);
        // Finalizer that swallows third-party NREs in ExplorationPopup.Setup
        // (WikiMod 2.6.5.1's SetupPostfix crashes on framework clone CT8 locations).
        Patching.BugFixes.ExplorationPopupFix.ApplyPatch(Harmony);
        // Prefix on ExplorationPopup.Setup that rebuilds a CT8's cached InGameCardBase.DismantleActions
        // array from its CardModel list when a gate strip/restore (or a later DA injection) left the
        // two diverged - the silent "travel button renders, clicks, does nothing" state
        // (retro river-bridge-east-click-noop). Logs one Info line per repair as the runtime signal.
        Patching.BugFixes.TravelDaCacheResync.ApplyPatch(Harmony);
        // CardSizeReduce 3.3.0 compat: patches AccessTools.Field to find auto-property
        // backing fields when CSR is installed; falls back to internal scaling shim
        // when CSR config is present but the DLL is missing. Self-no-ops without CSR.
        Patching.BugFixes.CardScaleCompat.Configure(Config, Harmony);
        // CheatsPatch 1.1.0 compat: postfix AccessTools.TypeByName so CheatsPatch's
        // patches resolve "UCheatsManager" → "CheatsManager" (CSFF rename).
        Patching.BugFixes.CheatsPatchCompat.Configure(Harmony);
        // NPC-interaction button text overflow: several NPC popup buttons (Talk/Trade/
        // Commissions row, dialog answer buttons) ship with TMP auto-sizing disabled, so
        // long labels clip past the button border instead of shrinking to fit.
        Patching.BugFixes.NPCButtonTextFit.ApplyPatch(Harmony);
        // NPC action-row overflow: an NPC that is both CanTrade and HasVisibleCommissions
        // (the Professor) puts 3 buttons in a row vanilla only ever laid out at 2, so the
        // Commissions button spills past the popup panel. Forces the row's existing
        // HorizontalLayoutGroup to shrink children to fit, scoped to that exact NPC state.
        Patching.BugFixes.NPCActionRowOverflowFix.ApplyPatch(Harmony);
        // Same underlying vanilla bug as ChangeEnvironmentCrashGuard (applied LAST, below), but
        // patched at the root (AddInstancedEnv itself) instead of one caller — covers
        // GameManager.ProduceCards and every other unguarded call site, not just
        // ChangeEnvironment. Confirmed in the wild via a permanent action-lock while placing a
        // Rain Cistern Kit (2026-08-14).
        Patching.BugFixes.AddInstancedEnvCrashGuard.ApplyPatch(Harmony);
        // Same "I can't do two things at once..." lock, reached through an autosave: a card
        // removed without vanilla GameManager.RemoveCard stays in GameManager.AllCards, and
        // GameLoad.SaveGameByReference throws on its null CardModel inside ActionRoutine. This
        // prefix drops such broken entries before every save (player report 2026-09-19).
        Patching.BugFixes.SaveStaleCardGuard.ApplyPatch(Harmony);
        // Animal tracks are only ever cleared in the player's current environment, so every other
        // place keeps every track ever left in it and each autosave writes them all (a day-408 save
        // held 47,648, 71% of the file; a seconds-long freeze every autosave, report 2026-10-16).
        // This prefix drops tracks past their lifetime, the same test vanilla applies on arrival.
        Patching.BugFixes.ExpiredTrackPruner.ApplyPatch(Harmony);
        // A removed mod location aliased in UidAliases.json resolves to its target, so several saved
        // boards can share one key and LoadCards keeps one of them, overwriting the target's real
        // board. This folds each removed board into the target's before LoadCards reads the save.
        Patching.BugFixes.RetiredEnvironmentSaveRepair.ApplyPatch(Harmony);
        // A portal arrival runs AddCard outside any action routine, so a stat-triggered action on
        // the departure card can empty NextEnvironment mid-switch and leave the player in no
        // environment (CalculateEnvironmentWeight NRE, T2.763). Restores it only while an arrival
        // started by PortalService is in flight.
        Patching.BugFixes.PortalArrivalNextEnvGuard.ApplyPatch(Harmony);
        // Vanilla undoes a multiplying passive effect as a multiply only for Spoilage; for every other
        // durability it subtracts the factor from the base rate and leaves the multiplier applied
        // (Sirus23 Wolf Den Morale -1.40625 after leaving two dens, T2.543).
        Patching.BugFixes.PassiveMultiplierCancelFix.ApplyPatch(Harmony);
        // Vanilla Animal Enclosure and Chicken Coop ("Land Sickness Fix") ask TagIsOnBoard about a
        // null CardTag; its instanced-env branch dereferences it and the NRE leaves RootAction set,
        // so every later action answers "I can't do two things at once..." (retro
        // vanilla-pen-coop-null-tag-wedge, 2026-10-09). Answers false, as its other branches do.
        Patching.BugFixes.NullTagOnBoardGuard.ApplyPatch(Harmony);
        // Starting a drag asks every board card in one delegate chain what the dragged card would do
        // to it; one card that throws stops the chain before the drag starts, so the board stays
        // dimmed and the game thinks the card is still held until a reload (Nexus report 2026-10-21,
        // Flint Knife onto a fish). Swallows that card's exception and names it once in the log.
        Patching.BugFixes.DragStartCrashGuard.ApplyPatch(Harmony);

        // WorldMap clone envs could show doubled forage terrain (two Ponds, two Pine Trees,
        // two Small Pine Trees) after a player's FIRST mid-session visit to an expansion tile —
        // PreCreateCloneEnvSaveData's own trim only self-heals at the NEXT boot. This postfix on
        // GameManager.CheckForMissingDefaultCardsInEnv re-runs the same trim immediately after
        // every arrival, so a mid-session duplicate is corrected in the same visit it appeared.
        Injection.WorldMapInjector.ApplyLiveTrimPatch(Harmony);

        // No-op — portal travel now uses game-default ChangeEnvironment behavior:
        // slot items travel with the player; floor/placed items stay at source.
        Portal.PortalTravelPatch.ApplyPatch(Harmony);

        // Wildlife: bear raid on encounter - fires WildlifeRaidService.OnBearEncounter() when a
        // bear fight (Combat_EncounterBear, started by Agent_Bear) begins. Self-no-ops when raids
        // are disabled.
        Patching.WildlifeRaidPatch.ApplyPatch(Harmony);

        // Encounter guards (Tier 2): the single StartEncounter suppression prefix all
        // Api.EncounterGuards predicates flow through. Zero-cost when no guards exist.
        Patching.EncounterGuardPatch.ApplyPatch(Harmony);

        // Encounter guards at the agent's source (2.28.0): postfixes NPCAction.ToAction and
        // StartEncounterDutyAction.CanBePerformed so a guard that NAMES an agent or an encounter stops
        // that agent's own attack. Zero-cost when no guard opts into agent starts.
        Patching.AgentEncounterGuardPatch.ApplyPatch(Harmony);

        // Connection gates (Tier 2): re-evaluate ImprovementBuilt / PerkEquipped gates
        // when an improvement is completed, updating map connection visibility immediately.
        // Zero-cost when no gates are declared.
        Patching.ConnectionGatePatch.ApplyPatch(Harmony);

        // GIF animation support — patches CardGraphics.Setup / RefreshCookingStatus.
        // Self-skips registration when no mod ships CardData/Gif/*.json.
        Patching.GifAnimationPatch.ApplyPatch(Harmony);

        // Card motion layer: CardGraphics.Setup + InGameNPC.RefreshPortrait postfixes that bend the
        // art of sprites named in CardData/Motion/*.json. Self-skips with no definitions or when off.
        Patching.CardMotionPatch.ApplyPatch(Harmony);

        // Perk origin marker: display-only postfixes on MenuPerkButton.Setup, MenuPerkPreview.Setup
        // and MainMenu.SelectPerk that append the owning mod's short tag to a framework-loaded
        // perk's rendered name. Patches nothing when [Perks] ShowModOriginTag is false.
        Patching.PerkOriginTagPatch.ApplyPatch(Harmony);

        // Animals: GameManager.Awake prefix that appends queued mod agents to
        // WorldSettings.NPCAgents right before the game consumes it. No-ops per run
        // when nothing is queued (i.e. no mod ships Animals/*.json or the system is off).
        Animals.SpawnRegistrar.ApplyPatch(Harmony);

        // Animals: null-guards InGameNPC.CreateModelCards' InventorySlots. Vanilla only
        // assigns InventorySlots when StartingInventory produces a non-empty list, so any
        // NPCAgent with a genuinely empty inventory (every hand-authored Animal-system agent)
        // crashes SortInventory() on first inspect. No-op for every vanilla NPCAgent.
        Animals.ModelCardInventoryFix.ApplyPatch(Harmony);

        // Animals: a generated night attack (Encounter.Aggression) waits while the player's current
        // action still has ticks left, instead of cancelling it; it fires on the action's last tick
        // and plays after the action completes (a trip is no longer lost to an owl, 2026-09-27).
        Animals.AttackWaitsForPlayerAction.ApplyPatch(Harmony);

        // Perk starting items: a mod perk's AddedCardsStats / EquippedCardsStats gives its starting
        // items real quality and type values instead of the card defaults (quality 0, plank ID 0,
        // leather Skin Type 0). Zero-cost when no perk declares any.
        Injection.PerkStartingItemStats.ApplyPatch(Harmony);

        // Opt-in diagnostic: logs GameLoad.AutoSaveGame calls (first 8) so beta
        // day/week/season checkpoint behavior can be inspected without changing it.
        Patching.Diagnostics.AutoSaveDiagnostics.Configure(Config, Harmony);

        // Opt-in diagnostic: times CheckForTracks + ChangeEnvironment coroutines on
        // every travel and counts GiveCard invocations during CheckForTracks. Used
        // to investigate the 1–2 s freeze when entering a location with fresh tracks.
        Patching.Diagnostics.TrackingTimingDiagnostics.Configure(Config, Harmony);

        // Opt-in diagnostics for the performance plan's measurement runs (features 15 and 23): a 60 s
        // frame-time summary with the NPC tick split, and a per-card end-state block after each
        // catch-up travel. Both install nothing unless their [Diagnostics] flag is true.
        Patching.Diagnostics.FrameTimingDiagnostics.Configure(Config, Harmony);
        Patching.Diagnostics.CatchUpEndStateDiagnostics.Configure(Config, Harmony);

        // Opt-in diagnostic: logs mod blueprints that BlueprintModelsScreen.FindTabFor
        // fails to resolve, and why — used to investigate mod blueprints appearing under
        // their own crafting-journal tab but never showing up in Search.
        Patching.Diagnostics.BlueprintSearchDiagnostic.Configure(Config, Harmony);

        // Opt-in diagnostic: watches live vanilla Bear/Wolf/WolfPack/PrimevalWolf agents
        // and logs how long each stays in the same environment — used to confirm and
        // localize player reports of wildlife getting permanently stuck on mod-injected
        // WorldMap trail nodes. See WildlifeStuckDiagnostics' doc comment.
        Patching.Diagnostics.WildlifeStuckDiagnostics.Configure(Config);

        // Performance (opt-in, default off): run AI selection for agents outside the player's
        // environment every second tick. Patches CheckForDuties' compiler-generated MoveNext
        // (never inlined), so it does not depend on order, but it goes after every other patch
        // anyway, ahead of the crash guard that must stay last.
        Patching.Performance.OffEnvNpcAiThrottle.Configure(Config, Harmony);

        // Finalizer that swallows WorldMapData.AddInstancedEnv's ArgumentException (two
        // instanced envs colliding on default map Coordinates) so GameManager.ChangeEnvironment
        // can't leave RootAction stuck, which otherwise permanently locks the player out of
        // every action ("I can't do two things at once...") until the app is restarted.
        // KEEP THIS THE LAST PATCH APPLIED HERE: Harmony JIT-compiles ChangeEnvironment's
        // replacement MoveNext at patch time, and Mono inlines small callees (a coroutine's
        // state-machine builder, a property getter) into it as they are AT THAT MOMENT, so any
        // patch applied afterwards to such a callee is skipped on every travel. Through 2.26.10
        // this ran before ApplyLiveTrimPatch and the live clone-env trim never ran on travel.
        // Enforced by Development_Tools/Tests/Framework-ChangeEnvironmentPatchOrder.Tests.ps1.
        Patching.BugFixes.ChangeEnvironmentCrashGuard.ApplyPatch(Harmony);

        Util.Log.Info($"{PluginName} v{PluginVersion} loaded. ({Harmony.GetPatchedMethods().Count()} methods patched)");
    }

    private void Update()
    {
        Wildlife.WildlifeRaidService.PollUpdate();
        Triggers.TriggerService.PollUpdate();
        Api.TickEvents.PollUpdate();
        Patching.Diagnostics.FrameTimingDiagnostics.OnFrame();
    }

    private void OnDestroy()
    {
        Loading.SpriteTextureCache.FlushBundleWrite();
        Harmony?.UnpatchSelf();
    }
}
