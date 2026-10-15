using BepInEx.Configuration;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace HarshWilds.Patcher;

/// <summary>
/// One config switch per threat, under <c>[Threats]</c> in <c>crispywhips.harsh_wilds.cfg</c>, each
/// mirrored into a hidden GameStat (<c>hw_switch_&lt;threat&gt;</c>, 0 or 1) that the threat's own
/// content gates on. Declarative content cannot read a BepInEx config, so the stat is the bridge: a
/// roaming animal gates on it through its manifest's <c>Spawn.Conditions</c> (framework 2.27.0), and
/// events and triggers gate on it through their own requirements.
///
/// <para>The stat's <c>BaseValue</c> is 1, so a fresh game is on before any of this runs; the write
/// below only has work to do for a threat switched off, or switched back on over a save that recorded
/// it off. It runs as a postfix on <c>GameManager.InitializeStatsAndActions</c>, which is where the
/// game applies a save's stat values (<c>.decomp/GameManager.cs</c>, its <c>StatsDict</c> build and
/// the <c>CurrentGameData.StatsDict</c> read inside that method), and which runs before NPCs spawn and
/// before <c>OnGMInitialized</c>, when the framework first evaluates spawn conditions. So a switched-off
/// animal is retired from the first tick, and a later save load cannot restore the old value over it.
/// A config edit is read at plugin load: it takes a full quit-to-desktop relaunch, not a save reload.</para>
///
/// <para>A switch is added in the same change as the threat it controls, never ahead of it: a switch
/// that turns nothing off is advertised dead code
/// (Documentation/Design/Harsh_Wilds_As_Built.md section 2).</para>
/// </summary>
internal static class Switchboard
{
    private sealed class Threat
    {
        public string Name;
        public string StatUid;
        public ConfigEntry<bool> Enabled;
    }

    private static readonly List<Threat> Threats = new();

    // The raid-pressure dial (plan section 17): [Tuning] RaidPressure Low, Normal or High, mirrored into the
    // hidden stat hw_tune_raids as 0, 1 or 2. Each raid's Nothing collection reads it through two
    // StatsDropChanceModifiers entries (Low adds weight to Nothing, High takes some off, Normal adds exactly
    // 0), so Low makes the eagle, the black bear and the heron rarer and High makes them likelier. Every
    // weight is a tunable placeholder, not balanced. It is a dial, not a threat: no [Threats] switch.
    private const string RaidPressureStat = "hw_tune_raids";
    private static readonly string[] RaidPressureValues = { "Low", "Normal", "High" };
    private static ConfigEntry<string> RaidPressure;
    private static readonly HashSet<string> WarnedMissing = new(StringComparer.Ordinal);

    internal static void Bind(ConfigFile config)
    {
        Add(config, "Lynx", "hw_switch_lynx",
            "Lynx: a big cat of the rocky pine forests that stalks and attacks at dusk and at night. " +
            "Set to false to remove it from the world. Takes effect after a full game restart.");
        Add(config, "Wisent", "hw_switch_wisent",
            "Wisent: a huge wild ox of the oak glades that fights back when approached. " +
            "Set to false to remove it from the world. Takes effect after a full game restart.");
        Add(config, "MuskOx", "hw_switch_muskox",
            "Musk ox: a winter herd ox of the northern hills and meadows that fights back when approached. " +
            "Set to false to remove it from the world. Takes effect after a full game restart.");
        Add(config, "Eagle", "hw_switch_eagle",
            "Eagle: a great eagle that now and then swoops down and carries off one piece of small game lying " +
            "loose on the ground outdoors (never anything in a container, a trap or your hands). " +
            "Set to false to stop it from ever coming. Takes effect after a full game restart.");
        Add(config, "BlackBear", "hw_switch_blackbear",
            "Black bear: in autumn a black bear now and then finds the meat, fish and sausage in your placed food stores " +
            "(drying racks, barrels, shelves) and carries off one piece. It never appears as an animal and never fights. " +
            "Set to false to stop it from ever coming. Takes effect after a full game restart.");
        Add(config, "Wolverine", "hw_switch_wolverine",
            "Wolverine: in winter a wolverine now and then robs your traps and carries off one catch (a fox, badger, boar, deer or wolf " +
            "carcass, or a tied boar) from a sprung trap, or from any placed store in the place you are in, such as a chest. " +
            "It never appears as an animal and never fights. Set to false to stop it from ever coming. " +
            "Takes effect after a full game restart.");
        Add(config, "Bandits", "hw_switch_bandits",
            "Bandits: after the first ten days a bandit roams the river rapids by day and may waylay you there. " +
            "Beat him and his body can be searched; flee and you lose nothing; lose the fight and he takes up to three " +
            "pieces of travel food from what you carry (never a tool, anything worn or anything a recipe or task needs). " +
            "Set to false to remove bandits from the world. Takes effect after a full game restart.");
        Add(config, "Drought", "hw_switch_drought",
            "Drought: in summer, after at least four days without rain, a drought may set in. For about a week no rain " +
            "falls, so rain catchers, cisterns and barrels stop refilling and rain stops watering the gardens. " +
            "Set to false to stop new droughts (one already under way runs its course). Takes effect after a full game restart.");
        Add(config, "InsectPlague", "hw_switch_insectplague",
            "Insect plague: in summer, swarms of biting insects may descend for about three days. Outdoors you are bitten far " +
            "more than usual and the bites add up; rain, a fire at your camp, snakegrass fed to a hearth and " +
            "staying indoors thin them out. Set to false to stop plagues, including one already under way. " +
            "Takes effect after a full game restart.");
        Add(config, "CropBlight", "hw_switch_cropblight",
            "Crop blight: outside winter, when you stand by a garden plot, a blight may spread fungus through the crops planted in " +
            "it for about two days, and infected plants lose health unless you treat the plot with fungicide or garlic water and " +
            "do not overwater it. Set to false to stop blights, including one already under way. " +
            "Takes effect after a full game restart.");
        Add(config, "BloodMoon", "hw_switch_bloodmoon",
            "Blood moon: on about one full moon in three the moon turns red at dusk and two more lynxes than usual prowl the pine " +
            "groves and mountain thickets until dawn, each able to attack once. The extra lynxes need the lynx switch too, so " +
            "with Lynx switched off a blood moon brings none. Set to false to stop blood moons, including one already under way. " +
            "Takes effect after a full game restart.");
        Add(config, "Eclipse", "hw_switch_eclipse",
            "Eclipse: in the first ten days of summer, at noon on a new-moon day, an eclipse may fall (about an even chance). " +
            "For about an hour the light drops sharply (an alert and a lower light reading) and refuses light-needing tasks for about fifteen minutes at its peak. " +
            "One already under way runs its course. Set to false to stop eclipses. Takes effect after a full game restart.");
        Add(config, "Aurora", "hw_switch_aurora",
            "Aurora: on dark winter nights (a new moon or a waxing crescent) an aurora may appear for up to ten hours. " +
            "While you stand outside under the open night sky your mood rises slowly, and the lift pauses while you are indoors. " +
            "Set to false to stop auroras, including the mood lift of one already under way. Takes effect after a full game restart.");
        Add(config, "FleasAndLice", "hw_switch_fleasandlice",
            "Fleas and lice: sleeping with an improvised bed, shelter or hideout, a bedroll or a log bed in the room, staying very " +
            "dirty, or keeping a Dog Friend (or a tamed wolf or fox from Sirus23's Mod Collection) may bring fleas and lice. " +
            "They cost comfort and slowly raise stress, and never leave " +
            "on their own: staying clean, the smoke of bog myrtle or juniper, bug repellent salve and a herbal wash drive " +
            "them out. Set to false to stop them and clear any you have. Takes effect after a full game restart.");
        Add(config, "ToothDecay", "hw_switch_toothdecay",
            "Tooth decay: honeycomb, sugar, a lot of fruit and plain time now and then rot a tooth. It adds to your pain and " +
            "slowly raises stress, and never goes away on its own: a chewing stick (more with a herb) wears it down, and in the " +
            "worst stage a tooth puller takes the bad tooth out. Set to false to stop it and clear any you have. " +
            "Takes effect after a full game restart.");
        Add(config, "SpiritSickness", "hw_switch_spiritsickness",
            "Spirit sickness: when a spirit you called (the Lynx, Goat or Blood Spirit) is angered, it may leave you spirit-sick. " +
            "It dulls your focus and slowly raises stress, and never fades by itself: a cleansing smudge burned at a lit " +
            "fire drives out one stage. Set to false to stop it and clear any you have. Takes effect after a full game restart.");
        Add(config, "DancingPlague", "hw_switch_dancingplague",
            "Dancing plague: dancing in a fairy ring at night (an hour) may leave you unable to stop, about half the time. " +
            "For some hours it drains stamina and sharpens hunger, then wears off by itself; a cleansing smudge burned at a lit " +
            "fire breaks it at once. Set to false to stop it, remove the Dance in the Ring action and clear any you have. " +
            "Takes effect after a full game restart.");
        Add(config, "Heron", "hw_switch_heron",
            "Heron: with Water-Driven Infrastructure installed, outside winter a grey heron now and then visits a filled or " +
            "stocked fishpond where you are and eats a few of its Minnow and Perch (never the Pike, and the pond itself is " +
            "never harmed). Without that mod it never comes. Set to false to stop it from ever coming. " +
            "Takes effect after a full game restart.");
        Add(config, "Adder", "hw_switch_adder",
            "Adder: in summer, by day, out in open clearings and on rocky or sloped ground, an adder may bite you, " +
            "giving you a bite wound and about four hours of venom that adds pain and slowly raises stress. Wearing " +
            "shoes lowers the odds. Set to false to stop adders from ever striking. " +
            "Takes effect after a full game restart.");
        Add(config, "ColdSnap", "hw_switch_coldsnap",
            "Cold snap: in the middle of spring and of autumn a sudden frost may set in for about two days. It is much " +
            "colder than the season (the game's base temperature falls by 10, so nights, mornings and evenings fall to the game's freezing state; " +
            "how cold you feel should follow, which has not been checked in play), and you need to dress and shelter for it. " +
            "Set to false to stop cold snaps; one already under way runs its course. Takes effect after a full game restart.");
        Add(config, "AlgaeBloom", "hw_switch_algaebloom",
            "Algae bloom: in summer, after at least three days without rain or during a drought, the rivers and lakes may " +
            "turn green for about three days. The larger fish (perch, eel, trout, char, pike and sturgeon) stop biting, so a " +
            "line or a spear brings up only minnows, or nothing, until it clears. Set to false to stop new blooms (one already " +
            "under way runs its course). Takes effect after a full game restart.");
        Add(config, "SmithingBurns", "hw_switch_smithingburns",
            "Smithing burns: an unskilled smith (Smithing skill under 40) working a smithing recipe with bare hands may " +
            "burn a hand and get a burn wound (about one build in seven). Leather gloves or fur mittens, or more " +
            "skill, prevent it. Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "ForageBites", "hw_switch_foragebites",
            "Forage bites: outside winter, foraging in a thicket (one of 25 overgrown places) may leave you bitten by " +
            "insects (about one forage in four unprotected). Shoes, gloves and bug-proof cloth lower the odds. " +
            "Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "MudVolcano", "hw_switch_mudvolcano",
            "Mud volcano: with Forest Lore installed, its mud volcano now and then erupts while you stand in its place, " +
            "throwing hot mud: it always dirties you and sometimes burns you. Without Forest Lore it never erupts. " +
            "Set to false to stop it from ever erupting. Takes effect after a full game restart.");
        Add(config, "WhiteWisent", "hw_switch_whitewisent",
            "White wisent: about one day in forty (a first guess) a rare white wisent comes into the oak glades, the wisent's " +
            "own range, and stays about four days. It never attacks first, and a hunter who kills it gets a carcass whose " +
            "Release the Spirit gives far more Beast Affinity than any other. It needs the Wisent switch too: with Wisent off " +
            "none comes. Set to false to remove it from the world. Takes effect after a full game restart.");
        Add(config, "Banshee", "hw_switch_banshee",
            "Banshee: at night, while you are bleeding badly (the game has no health bar, so bleeding profusely stands for being near death), " +
            "a banshee may keen outside. It raises your stress for some hours and tells you to bind the wound. " +
            "Rare; a threshold stone with an offering lowers the odds. Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "Nixie", "hw_switch_nixie",
            "Nixie: at night, on river and lake banks, a nixie may sing from the water. You can hold fast (steady nerves help) or follow; " +
            "drawn in, you stay soaked and cold for some hours. Rare; a threshold stone with an offering lowers the odds. " +
            "Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "Selkie", "hw_switch_selkie",
            "Selkie: with Far Territory installed, at night on its Driftwood Shore a selkie may come ashore and sing from the surf. " +
            "You can turn away (steady nerves help) or follow; drawn in, you stay soaked and cold for some hours. " +
            "Without Far Territory it never comes. Rare; a threshold stone with an offering lowers the odds. " +
            "Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "WildHunt", "hw_switch_wildhunt",
            "Wild Hunt: on a winter night, out of doors, the Wild Hunt may ride for about five hours. Under the open sky each moment " +
            "raises your stress and may bruise you; a roof is the way out. Rare; a threshold stone with an offering lowers the odds. " +
            "Set to false to stop it, including a ride under way. Takes effect after a full game restart.");
        Add(config, "LakeBeast", "hw_switch_lakebeast",
            "Lake beast: about one day in sixty (a first guess) a lake beast comes into the lake and its banks for about three days. " +
            "At night it can attack a player on the lake shore, once a night, and it fights as hard as a bear. " +
            "A hunter who kills it gets a carcass to butcher. Set to false to remove it from the world. " +
            "Takes effect after a full game restart.");
        Add(config, "FrostDemon", "hw_switch_frostdemon",
            "Frost demon: on about one winter evening in fifteen (a first guess) a frost demon comes down to the northern hills " +
            "and meadows until morning. It can hunt a player out of doors in or a few areas from them, once a night; its bites are frostbite. " +
            "A hunter who kills it gets a carcass to butcher. Set to false to remove it from the world. " +
            "Takes effect after a full game restart.");
        Add(config, "Lindwurm", "hw_switch_lindwurm",
            "Lindwurm: about one day in a hundred and twenty (a first guess) a lindwurm, a great serpent, lairs in the Great Cave " +
            "for a week or so. It can attack a player who goes into that cave, once a day, and it fights harder than a bear. " +
            "A hunter who kills it gets a carcass to butcher and a Dragon Spirit to release. Set to false to remove it from the world. " +
            "Takes effect after a full game restart.");
        Add(config, "FaerieTricks", "hw_switch_faerietricks",
            "Faerie tricks: while you are in the Faerie Realm (through a fairy ring on a full-moon night), about once an hour " +
            "the fair folk may lead you astray, and finding your way back tires you. " +
            "It never keeps you from going home. Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "MoonGlamour", "hw_switch_moonglamour",
            "Moon glamour: while you are in the Moon Realm (through a Moon Basin beside a Moon Stone on a full-moon night), about once an hour " +
            "the moon may cloud your mind, and shaking it off strains you. " +
            "It never keeps you from going home. Set to false to stop it. Takes effect after a full game restart.");
        Add(config, "RestlessDead", "hw_switch_restlessdead",
            "Restless dead: while you are in the Underworld (entered at night through a stone marker), about once an hour " +
            "the dead may take hold of you, and breaking free tires you and sets your nerves on edge. " +
            "It never keeps you from going home. Set to false to stop it. Takes effect after a full game restart.");
        BindTuning(config);
    }

    private static void BindTuning(ConfigFile config)
    {
        RaidPressure = config.Bind("Tuning", "RaidPressure", "Normal",
            new ConfigDescription(
                "Raid pressure: how often the eagle, the black bear and the heron come. Low makes them rarer, " +
                "High makes them likelier, Normal is the standard game. It changes only the odds of each roll, never " +
                "what a raid takes, and it does not touch the world events or the animals you can fight. " +
                "Takes effect after a full game restart.",
                new AcceptableValueList<string>(RaidPressureValues)));
    }

    private static void Add(ConfigFile config, string name, string statUid, string description)
    {
        Threats.Add(new Threat
        {
            Name = name,
            StatUid = statUid,
            Enabled = config.Bind("Threats", name, true, description),
        });
    }

    internal static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
        if (target == null)
        {
            Plugin.Logger.LogWarning("Switchboard: GameManager.InitializeStatsAndActions not found; threat switches will not apply.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(Switchboard), nameof(InitializeStatsAndActions_Postfix)));
    }

    // Low 0, Normal 1, High 2. A value the list does not hold (a hand-edited cfg is clamped by BepInEx to the
    // list, but a read of null is still handled) falls back to Normal, the no-op, with one warning.
    private static void ApplyRaidPressure()
    {
        string setting = RaidPressure?.Value;
        int level = System.Array.IndexOf(RaidPressureValues, setting);
        if (level < 0)
        {
            if (WarnedMissing.Add("<raid-pressure-value>"))
                Plugin.Logger.LogWarning($"Switchboard: RaidPressure '{setting}' is not Low, Normal or High; using Normal.");
            level = 1;
        }

        if (!StatAccess.Set(RaidPressureStat, level))
        {
            if (WarnedMissing.Add(RaidPressureStat))
                Plugin.Logger.LogWarning($"Switchboard: could not write GameStat '{RaidPressureStat}'; the RaidPressure dial cannot apply.");
            return;
        }

        if (level != 1)
            Plugin.Logger.LogInfo($"Switchboard: RaidPressure is {RaidPressureValues[level]} in the config.");
    }

    private static void InitializeStatsAndActions_Postfix(GameManager __instance)
    {
        if (__instance == null || __instance.StatsDict == null) return;

        // StatAccess resolves the live stat through GameManager.Instance and caches it per GameManager, so
        // it only ever sees THIS run's stats when the postfix's instance is the live singleton. A
        // different instance would mean every write below lands on another run's stat: say so once and
        // write nothing rather than corrupt it.
        if (!ReferenceEquals(CardUtil.GetGameManagerInstance(), __instance))
        {
            if (WarnedMissing.Add("<instance-mismatch>"))
                Plugin.Logger.LogWarning("Switchboard: GameManager.Instance is not the manager that is initialising, so the threat switches were not applied this run.");
            return;
        }

        foreach (var threat in Threats)
        {
            float value = threat.Enabled.Value ? 1f : 0f;

            // StatAccess.Set (framework 2.28.0) is the game's own permanent stat change by UniqueID:
            // the clamp is the stat's own 0..1, and the stat's listeners and statuses update. It
            // returns true when the stat already reads the value. On false it has already warned once
            // per cause in the framework's wording; this is the one Harsh Wilds breadcrumb per switch.
            if (!StatAccess.Set(threat.StatUid, value))
            {
                if (WarnedMissing.Add(threat.StatUid))
                    Plugin.Logger.LogWarning($"Switchboard: could not write GameStat '{threat.StatUid}'; the {threat.Name} switch cannot apply.");
                continue;
            }

            if (value == 0f)
                Plugin.Logger.LogInfo($"Switchboard: {threat.Name} is switched off in the config; it stays out of this run.");
        }

        ApplyRaidPressure();
    }
}
