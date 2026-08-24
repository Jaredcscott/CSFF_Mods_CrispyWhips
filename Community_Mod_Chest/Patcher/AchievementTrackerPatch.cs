using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Town Achievement Board detection — Tier B poll (Village_Master_Plan.md §10.9.2.4 Tier B).
    /// One 5-second TickEvents.Interval covering nine of the eleven achievement detectors: Happy
    /// New Year, Forest Explorer, Spelunker (§10.9.4 Wave 1); Master Hunter's derived count/earned
    /// recompute (Wave 2); and Full Kit, Master Angler, Master Shaman, Stinky Jar, Spiritual
    /// Overcrowding, and Finding a Friend (Wave 3 — this pass, the pack's final prompt). All
    /// eleven achievements now have a wired detector; see AchievementKillEffectsPatch for the two
    /// kill-driven ones below.
    ///
    /// <para>Right to Bear Arms is deliberately NOT here: it is a single-target latch written
    /// straight onto <c>cmcStatAchBearSlain</c> by the Bear Encounter's appended kill effect
    /// (<see cref="AchievementKillEffectsPatch"/>), so there is nothing to derive.</para>
    ///
    /// Detectors:
    ///   - Happy New Year — GameQuery.CurrentDay &gt;= GameQuery.DaysPerYear (live gamemode day
    ///     length, not a hardcoded 120). Earned latch cmcStatAchYearSurvived.
    ///   - Forest Explorer — GameManager.VisitedEnvironments (List&lt;CardData&gt;, save-persisted)
    ///     intersected with 10 curated surface CT4 env UIDs (Wave 0 lookup, embedded below).
    ///     Member latches cmcStatAchEnv*, derived count cmcStatAchExplorerCount, earned latch
    ///     cmcStatAchExplorer.
    ///   - Spelunker — same shape against 8 curated underground CT4 env UIDs (cmcStatAchCave*,
    ///     cmcStatAchSpelunkerCount, cmcStatAchSpelunker).
    ///   - Master Hunter — derives cmcStatAchHunterCount / cmcStatAchHunter from the 12
    ///     cmcStatAchHunt* member latches. Those latches are set by the ENGINE, not here: see
    ///     <see cref="AchievementKillEffectsPatch"/>, which appends a +1 StatModifier onto each
    ///     huntable animal's vanilla Encounter at load.
    ///   - Full Kit / Master Angler / Master Shaman — one SHARED GameManager.AllCards scan
    ///     (<see cref="CheckCollectionsAndStinkyJar"/>) intersected against three curated
    ///     vanilla item-UID lists (6 metal tools / 6 fish / 19 bound spirits, Wave 0 lookup). A
    ///     card sighting latches "possessed at least once" — it cannot distinguish crafted from
    ///     traded/found, so the board's own DA wording is deliberately possession-framed, not a
    ///     craft/catch claim (root CLAUDE.md § Feature Honesty).
    ///   - Stinky Jar — rides the SAME AllCards scan: a storage-pot card (4 curated UIDs) whose
    ///     ContainedLiquidModel is one of the 3 Urine aging stages, filled to the pot's own
    ///     CurrentMaxLiquidQuantity. Single boolean earned latch, no member/count stats.
    ///   - Spiritual Overcrowding — a per-player stat read (vanilla Spiritual Noise GameStat),
    ///     NOT a card scan, so it is its own poll branch. Earns when the LIVE current value
    ///     reaches the LIVE max (CurrentMinMaxValue.y via HiddenStat.GetMax — perks can shift this
    ///     at runtime, so the JSON default of 13 is never hardcoded).
    ///   - Finding a Friend — has the player ever crossed paths with one of the 3 Trader
    ///     NPCAgents (GameManager.FindNPC against the whole-game AllNPCs roster)? Also its own
    ///     poll branch, not a card scan.
    ///
    /// Each detector reads its earned latch FIRST and short-circuits before doing any real work —
    /// a settled save costs a handful of cheap stat reads per poll, no card/environment/NPC
    /// scanning at all. Stat I/O goes through HiddenStat (Get/Set/GetMax) — the generic
    /// GetFromID+StatsDict wrapper already used by this feature's sibling
    /// AchievementBoardSeedPatch.cs, rather than reopening a third copy of the reflection dance
    /// VillageClock.ReadStat/WriteStat also implements (root CLAUDE.md § Verify Framework API
    /// Exists doctrine: reuse the proven helper closest to the call site).
    /// </summary>
    internal static class AchievementTrackerPatch
    {
        private const string YearSurvivedStatUid = "cmcStatAchYearSurvived";

        // Wave 0 curated list (EA 0.66h, Documentation/GameData/CSFF-JsonData_Current/) — 10
        // surface CT4 environment UIDs for Forest Explorer. Index-aligned with
        // ExplorerMemberStatUids. Maintenance surface: re-verify both UID lists against current
        // vanilla data on game updates (root CLAUDE.md § Game-Update Reference Refresh).
        private static readonly string[] ExplorerEnvUids =
        {
            "4c37a8a55fdf1e248946658659dc3ca2", // GroveOak_SacredGrove
            "a7e737858eaebac4981ac595527bc369", // GrovePine_MountainGrove
            "3560005192d0d0a4ea2ff43f17ea0a4a", // ThicketPine_MountainThicket
            "c5d869f30ddcd194f924042021d7df96", // ClearingPine_FlowerGlade
            "9b5f925efe81b284ca921f1d33e141a1", // River_GrovePine_LakeIsland
            "9cae9480aeb898944b15d5ffe1bf3bea", // WillOWispGrove1
            "3910968a54c56e045b069407039df6cf", // WillOWispGrove2
            "ad9abd316502f594da1bda2befb5ea16", // GroveAlder_SleepyGrove
            "156af397d5201f943b663a1f9abc382f", // ThicketOak_WhisperingThicket
            "71e894a03cb89164796b8e21c3c0ff3c", // ClearingAlder_SwampHill
        };

        private static readonly string[] ExplorerMemberStatUids =
        {
            "cmcStatAchEnvSacredGrove",
            "cmcStatAchEnvMountainGrove",
            "cmcStatAchEnvMountainThicket",
            "cmcStatAchEnvFlowerGlade",
            "cmcStatAchEnvLakeIsland",
            "cmcStatAchEnvWillOWispGrove1",
            "cmcStatAchEnvWillOWispGrove2",
            "cmcStatAchEnvSleepyGrove",
            "cmcStatAchEnvWhisperingThicket",
            "cmcStatAchEnvSwampHill",
        };

        private const string ExplorerCountStatUid = "cmcStatAchExplorerCount";
        private const string ExplorerEarnedStatUid = "cmcStatAchExplorer";

        // Wave 0 curated list (EA 0.66h) — 8 underground CT4 environment UIDs for Spelunker.
        // Index-aligned with SpelunkerMemberStatUids.
        private static readonly string[] SpelunkerEnvUids =
        {
            "0d597c607faea644792819dd69735016", // Env_BearCave
            "63bf07510e1a58d448cb36effe180a91", // Env_WolfCave
            "17d6cfe8ff886b643afee74dc18760a7", // Env_WaterfallCaves
            "41d2fb7700db4614b8ae12b526d8ec24", // Env_CaveOldHollow
            "75511a9fcdc45f647bc032eed359ef44", // Env_CaveRiverPass
            "ca8ecdd9232b168449ef84103fa30276", // Env_DeathCrack_DeathChamber
            "04c947daf3e247b4fb52fe98895d27de", // Env_FmFlintClawsChamber
            "24b659a069e11e54495f75d4d564f038", // Env_FmTheShaft
        };

        private static readonly string[] SpelunkerMemberStatUids =
        {
            "cmcStatAchCaveBearCave",
            "cmcStatAchCaveWolfCave",
            "cmcStatAchCaveWaterfallCaves",
            "cmcStatAchCaveOldHollow",
            "cmcStatAchCaveRiverPass",
            "cmcStatAchCaveDeathChamber",
            "cmcStatAchCaveFlintClawsChamber",
            "cmcStatAchCaveTheShaft",
        };

        private const string SpelunkerCountStatUid = "cmcStatAchSpelunkerCount";
        private const string SpelunkerEarnedStatUid = "cmcStatAchSpelunker";

        // Master Hunter member latches. UNLIKE the two env sets above, this detector observes
        // nothing itself — each latch is written by the ENGINE, from a +1 StatModifier that
        // AchievementKillEffectsPatch appends onto that animal's vanilla Encounter's
        // EnemyDefeatedEffects at load. All this branch does is derive the count and the earned
        // latch from the 12 members. Order is cosmetic (nothing here is index-aligned), but kept
        // alphabetical to mirror that patch's table — the two lists MUST stay in sync: an animal
        // missing here can never be counted, so Master Hunter would be permanently unearnable.
        private static readonly string[] HunterMemberStatUids =
        {
            "cmcStatAchHuntBadger",
            "cmcStatAchHuntBear",
            "cmcStatAchHuntBeaver",
            "cmcStatAchHuntBoar",
            "cmcStatAchHuntDoe",
            "cmcStatAchHuntDuck",
            "cmcStatAchHuntFox",
            "cmcStatAchHuntHare",
            "cmcStatAchHuntPartridge",
            "cmcStatAchHuntSquirrel",
            "cmcStatAchHuntStag",
            "cmcStatAchHuntWolf",
        };

        private const string HunterCountStatUid = "cmcStatAchHunterCount";
        private const string HunterEarnedStatUid = "cmcStatAchHunter";

        // ── Full Kit (6 metal tools) — Wave 0 curated list (EA 0.66h). Index-aligned with
        // ToolMemberStatUids.
        private static readonly string[] ToolItemUids =
        {
            "968d89b358ccf624a9e34f659166a0a1", // Metal Axe
            "a8b3d99a923d83340a2f039617f3dff3", // Metal Pickaxe
            "8ff5977172c44ed44a0f992491529821", // Metal Hoe
            "22858c679a340ae4f84c7e9a20e8a4e7", // Metal Knife
            "3587432ac7e29784991ff2cccf92891d", // Metal Shovel
            "f766e4e8f4820ab44a70c7a4eaf80578", // Metal Sickle
        };

        private static readonly string[] ToolMemberStatUids =
        {
            "cmcStatAchToolAxe",
            "cmcStatAchToolPickaxe",
            "cmcStatAchToolHoe",
            "cmcStatAchToolKnife",
            "cmcStatAchToolShovel",
            "cmcStatAchToolSickle",
        };

        private const string FullKitCountStatUid = "cmcStatAchFullKitCount";
        private const string FullKitEarnedStatUid = "cmcStatAchFullKit";

        // ── Master Angler (6 fish) — Wave 0 curated list. Index-aligned with FishMemberStatUids.
        private static readonly string[] FishItemUids =
        {
            "a966c94277465f741857b994202791f3", // Char
            "bb8a42f395c31894188ff3038d76128f", // Eel
            "e64237c9a44ea3945bc75b8b9f4735c0", // Perch
            "70df5b800c3fd56499fd412bb9ce7523", // Pike
            "de00abf9d0d186b42bed6807ba49a4cb", // Sturgeon
            "f4972170adc8b3d4f9d4bc09c6e7f760", // Trout
        };

        private static readonly string[] FishMemberStatUids =
        {
            "cmcStatAchFishChar",
            "cmcStatAchFishEel",
            "cmcStatAchFishPerch",
            "cmcStatAchFishPike",
            "cmcStatAchFishSturgeon",
            "cmcStatAchFishTrout",
        };

        private const string AnglerCountStatUid = "cmcStatAchAnglerCount";
        private const string AnglerEarnedStatUid = "cmcStatAchAngler";

        // ── Master Shaman (19 bound spirits) — Wave 0 curated list. Index-aligned with
        // ShamanMemberStatUids. Order is cosmetic (animals then fish), kept alphabetical-ish to
        // mirror the plan's own table.
        private static readonly string[] ShamanItemUids =
        {
            "fe1180066aef7034a95a1af4f51ce2be", // Badger Spirit
            "cfd8dea9b4a4e4d4ca799c191896d5f5", // Bear Spirit
            "7760a378973db95418367676ad326585", // Beaver Spirit
            "f348f0077b03de448bdd748114f30787", // Boar Spirit
            "1d14228ffe556f845bc52120696d4bfe", // Deer Spirit
            "ae8742fb1d9ed8049ac9c7d6807e4975", // Flintclaw Spirit
            "51481c8a38114ac40904721468a9487d", // Fox Spirit
            "43055d69fb17034409d1d2da68dd2add", // Frog Spirit
            "650277c10d25e5f45b7b8017e766debd", // Hare Spirit
            "d0852b7b8329ed3469bdae79313cd878", // Moontouched Spirit
            "e5a2fa9baffbfd84598919255af2c026", // Mouse Spirit
            "f5fe3ace1fc97fd459be0cc324f2fc98", // Partridge Spirit
            "eb737eed43ea560449e7e71c5850814e", // Primeval Wolf Spirit
            "73ca6585b729c9745acd14e21d43df9f", // Squirrel Spirit
            "10e1cebbf3b82464a99acfaef86a2752", // Wolf Spirit
            "3bba7648515fed44980b225e9d551f20", // Perch Spirit
            "6670bf421a92d454a911ccd1c343a933", // Pike Spirit
            "a5fce873f56a0b941837c6a5c5be31f1", // Sturgeon Spirit
            "026a88336fde7e743a9e4cda6d887224", // Trout Spirit
        };

        private static readonly string[] ShamanMemberStatUids =
        {
            "cmcStatAchShamanBadger",
            "cmcStatAchShamanBear",
            "cmcStatAchShamanBeaver",
            "cmcStatAchShamanBoar",
            "cmcStatAchShamanDeer",
            "cmcStatAchShamanFlintclaw",
            "cmcStatAchShamanFox",
            "cmcStatAchShamanFrog",
            "cmcStatAchShamanHare",
            "cmcStatAchShamanMoontouched",
            "cmcStatAchShamanMouse",
            "cmcStatAchShamanPartridge",
            "cmcStatAchShamanPrimevalWolf",
            "cmcStatAchShamanSquirrel",
            "cmcStatAchShamanWolf",
            "cmcStatAchShamanPerch",
            "cmcStatAchShamanPike",
            "cmcStatAchShamanSturgeon",
            "cmcStatAchShamanTrout",
        };

        private const string ShamanCountStatUid = "cmcStatAchShamanCount";
        private const string ShamanEarnedStatUid = "cmcStatAchShaman";

        // ── Stinky Jar — no member/count stats, single boolean earned latch. Rides the same
        // AllCards scan as the three collection achievements above.
        private static readonly HashSet<string> StinkyJarPotUids = new(StringComparer.Ordinal)
        {
            "d31a1dd6772813444a0cb8370aa59464", // ClayStoragePotUnsealed (item)
            "894492bd12dcba94e8b6ababc67a58e7", // ClayStoragePotPlacedUnsealed
            "3af3dec775f09ad47a5f73ddd13ed228", // ClayStoragePotSealed
            "a3713cbe65bca38419b8f4119a3c186c", // ClayStoragePotPlacedSealed
        };

        private static readonly HashSet<string> StinkyJarUrineUids = new(StringComparer.Ordinal)
        {
            "9ae738fe3c4fc334e9d75fe660ab8243", // Urine (Fresh)
            "1edd1f22b4387b744b6a01f146a51770", // Urine (Stale)
            "0c9006a8faa98b841960e46661f854f9", // Urine (Aged)
        };

        private const string StinkyJarEarnedStatUid = "cmcStatAchStinkyJar";

        // ── Spiritual Overcrowding — per-player stat read, own poll branch (not a card scan).
        private const string SpiritualNoiseGameStatUid = "30e940eb00405d94b94822377313fb1b"; // vanilla
        private const string NoiseMaxEarnedStatUid = "cmcStatAchNoiseMax";

        // ── Finding a Friend — NPCAgent roster check, own poll branch (not a card scan).
        private static readonly (string NpcAgentUid, string Label)[] TraderNpcAgents =
        {
            ("56f116608ec0e6241903cb25b7901ba7", "TraderGeneral"),
            ("b7899a696e8b25f44adac0bb05ed0ece", "TraderMetal"),
            ("914f211bd854bd249810f9983ff168ed", "TraderAnimal"),
        };

        private const string TraderFoundEarnedStatUid = "cmcStatAchTraderFound";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, Tick, "AchievementTracker");
            Plugin.Logger.LogDebug("[AchievementTrackerPatch] initialized.");
        }

        private static void Tick()
        {
            try
            {
                CheckHappyNewYear();
                CheckEnvSet(ExplorerEnvUids, ExplorerMemberStatUids, ExplorerCountStatUid, ExplorerEarnedStatUid, "Forest Explorer");
                CheckEnvSet(SpelunkerEnvUids, SpelunkerMemberStatUids, SpelunkerCountStatUid, SpelunkerEarnedStatUid, "Spelunker");
                CheckMasterHunter();
                CheckCollectionsAndStinkyJar();
                CheckSpiritualOvercrowding();
                CheckFindingAFriend();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AchievementTrackerPatch] Tick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckHappyNewYear()
        {
            if (HiddenStat.Get(YearSurvivedStatUid) >= 0.5f) return; // already earned — skip the day read

            if (GameQuery.CurrentDay < GameQuery.DaysPerYear) return;

            HiddenStat.Set(YearSurvivedStatUid, 1f);
            Plugin.Logger.LogInfo("[AchievementTrackerPatch] Achievement earned: Happy New Year.");
        }

        /// <summary>
        /// Master Hunter: derive <c>cmcStatAchHunterCount</c> and the earned latch from the 12
        /// member latches the kill-effect StatModifiers set. Deliberately shaped differently from
        /// <see cref="CheckEnvSet"/>: that method can short-circuit on "nothing newly latched"
        /// because it latches the members itself, whereas here the engine writes them behind our
        /// back, so the 12 must actually be re-read each poll. That is 12 dictionary lookups on
        /// already-cached stat models, and only while the achievement is still unearned — the
        /// first line makes a completed save cost one read per poll forever after.
        ///
        /// <para>An unreadable stat reads -1 and simply fails the <c>&gt;= 0.5f</c> test, so a
        /// pre-GameManager tick under-counts rather than falsely awarding; and the stored-count
        /// comparison below means such a tick writes nothing at all.</para>
        /// </summary>
        private static void CheckMasterHunter()
        {
            if (HiddenStat.Get(HunterEarnedStatUid) >= 0.5f) return; // already earned — skip the 12 reads

            int count = 0;
            for (int i = 0; i < HunterMemberStatUids.Length; i++)
                if (HiddenStat.Get(HunterMemberStatUids[i]) >= 0.5f) count++;

            float stored = HiddenStat.Get(HunterCountStatUid);
            if (stored < 0f) return; // stats not readable yet — don't write a bogus 0 over saved progress

            // Write only on an actual change: this poll runs every 5s for the whole run, and
            // re-writing an unchanged value would churn the stat-change path for nothing.
            if (Math.Abs(stored - count) > 0.01f)
                HiddenStat.Set(HunterCountStatUid, count);

            if (count < HunterMemberStatUids.Length) return;

            HiddenStat.Set(HunterEarnedStatUid, 1f);
            Plugin.Logger.LogInfo("[AchievementTrackerPatch] Achievement earned: Master Hunter.");
        }

        /// <summary>Shared shape for Forest Explorer / Spelunker: intersect VisitedEnvironments
        /// with a curated UID list, latch newly-seen members, recompute the derived count, and
        /// flip the earned latch when every member is latched.</summary>
        private static void CheckEnvSet(string[] envUids, string[] memberStatUids, string countStatUid, string earnedStatUid, string achievementName)
        {
            if (HiddenStat.Get(earnedStatUid) >= 0.5f) return; // already earned — skip the scan entirely

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            if (Reflect.GetMember(gm, "VisitedEnvironments") is not IEnumerable visited) return;

            var visitedUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var envCard in visited)
            {
                var uid = CardUtil.GetCardUniqueId(envCard);
                if (!string.IsNullOrEmpty(uid)) visitedUids.Add(uid);
            }
            if (visitedUids.Count == 0) return;

            bool anyNewlyLatched = false;
            for (int i = 0; i < envUids.Length; i++)
            {
                if (HiddenStat.Get(memberStatUids[i]) >= 0.5f) continue; // already latched
                if (!visitedUids.Contains(envUids[i])) continue;
                HiddenStat.Set(memberStatUids[i], 1f);
                anyNewlyLatched = true;
            }
            if (!anyNewlyLatched) return; // nothing changed this poll — count/earned already reflect prior state

            int count = 0;
            for (int i = 0; i < memberStatUids.Length; i++)
                if (HiddenStat.Get(memberStatUids[i]) >= 0.5f) count++;
            HiddenStat.Set(countStatUid, count);

            if (count >= envUids.Length)
            {
                HiddenStat.Set(earnedStatUid, 1f);
                Plugin.Logger.LogInfo($"[AchievementTrackerPatch] Achievement earned: {achievementName}.");
            }
        }

        /// <summary>
        /// ONE shared <c>GameManager.AllCards</c> scan (Wave 3) covering four detectors: Full Kit,
        /// Master Angler, Master Shaman, and Stinky Jar. Reuses the reflect-and-walk idiom
        /// <c>LostCatPatch.CatExistsAnywhere</c> established — object gm -&gt; IEnumerable
        /// AllCards -&gt; per-card CardModel -&gt; UniqueID — rather than opening a second copy of
        /// that loop; the three collection achievements and Stinky Jar's pot check all ride the
        /// SAME per-card iteration instead of four separate scans. A save with all four already
        /// earned skips the scan entirely (root CLAUDE.md's "read the earned latch first" idiom
        /// this file already uses for every other detector).
        /// </summary>
        private static void CheckCollectionsAndStinkyJar()
        {
            bool fullKitDone = HiddenStat.Get(FullKitEarnedStatUid) >= 0.5f;
            bool anglerDone = HiddenStat.Get(AnglerEarnedStatUid) >= 0.5f;
            bool shamanDone = HiddenStat.Get(ShamanEarnedStatUid) >= 0.5f;
            bool stinkyDone = HiddenStat.Get(StinkyJarEarnedStatUid) >= 0.5f;
            if (fullKitDone && anglerDone && shamanDone && stinkyDone) return; // nothing left to scan for

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return;

            bool fullKitNewlyLatched = false;
            bool anglerNewlyLatched = false;
            bool shamanNewlyLatched = false;

            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                var uid = Reflect.GetMember(model, "UniqueID") as string;
                if (string.IsNullOrEmpty(uid)) continue;

                if (!fullKitDone && LatchIfMember(uid, ToolItemUids, ToolMemberStatUids))
                    fullKitNewlyLatched = true;
                if (!anglerDone && LatchIfMember(uid, FishItemUids, FishMemberStatUids))
                    anglerNewlyLatched = true;
                if (!shamanDone && LatchIfMember(uid, ShamanItemUids, ShamanMemberStatUids))
                    shamanNewlyLatched = true;

                if (!stinkyDone && StinkyJarPotUids.Contains(uid) && IsJarFullOfAgedUrine(card))
                {
                    HiddenStat.Set(StinkyJarEarnedStatUid, 1f);
                    stinkyDone = true; // stop re-checking pots for the rest of this scan
                    Plugin.Logger.LogInfo("[AchievementTrackerPatch] Achievement earned: Stinky Jar.");
                }
            }

            if (fullKitNewlyLatched) RecomputeCollectionCount(ToolMemberStatUids, FullKitCountStatUid, FullKitEarnedStatUid, "Full Kit");
            if (anglerNewlyLatched) RecomputeCollectionCount(FishMemberStatUids, AnglerCountStatUid, AnglerEarnedStatUid, "Master Angler");
            if (shamanNewlyLatched) RecomputeCollectionCount(ShamanMemberStatUids, ShamanCountStatUid, ShamanEarnedStatUid, "Master Shaman");
        }

        /// <summary>If <paramref name="uid"/> matches an entry in <paramref name="itemUids"/> whose
        /// member latch isn't set yet, latches it and returns true (newly latched this call).
        /// Linear scan over a small (6–19 entry) array per card is cheap at this scale — a combined
        /// UID-&gt;stat dictionary would save little here and cost readability.</summary>
        private static bool LatchIfMember(string uid, string[] itemUids, string[] memberStatUids)
        {
            for (int i = 0; i < itemUids.Length; i++)
            {
                if (!string.Equals(itemUids[i], uid, StringComparison.Ordinal)) continue;
                if (HiddenStat.Get(memberStatUids[i]) >= 0.5f) return false; // already latched
                HiddenStat.Set(memberStatUids[i], 1f);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Stinky Jar's fullness check (Wave 0 field-shape finding, confirmed via decompile): the
        /// pot's <c>ContainedLiquidModel</c> must be one of the 3 Urine aging stages, and the
        /// CONTAINER's OWN <c>CurrentMaxLiquidQuantity</c> — not the liquid card's — is the
        /// capacity <c>ContainedLiquid.CurrentLiquidQuantity</c> is compared against.
        /// </summary>
        private static bool IsJarFullOfAgedUrine(object potCard)
        {
            var liquidModel = Reflect.GetMember(potCard, "ContainedLiquidModel");
            if (liquidModel == null) return false;

            var liquidUid = Reflect.GetMember(liquidModel, "UniqueID") as string;
            if (liquidUid == null || !StinkyJarUrineUids.Contains(liquidUid)) return false;

            var containedLiquid = Reflect.GetMember(potCard, "ContainedLiquid");
            if (containedLiquid == null) return false;

            float quantity = Reflect.GetFloat(containedLiquid, "CurrentLiquidQuantity", -1f);
            float capacity = Reflect.GetFloat(potCard, "CurrentMaxLiquidQuantity", -1f);
            if (quantity < 0f || capacity <= 0f) return false;

            return quantity >= capacity;
        }

        /// <summary>Shared tail for the three collection achievements: recompute the derived count
        /// from the member latches and flip the earned latch once every member is set. Same shape
        /// as <see cref="CheckEnvSet"/>'s tail; kept as a separate method rather than folded into
        /// that one because CheckEnvSet's own short-circuit is keyed to its two ALREADY-SHIPPED
        /// callers (Forest Explorer/Spelunker) and reshaping it is out of scope for this pass.
        /// </summary>
        private static void RecomputeCollectionCount(string[] memberStatUids, string countStatUid, string earnedStatUid, string achievementName)
        {
            int count = 0;
            for (int i = 0; i < memberStatUids.Length; i++)
                if (HiddenStat.Get(memberStatUids[i]) >= 0.5f) count++;
            HiddenStat.Set(countStatUid, count);

            if (count >= memberStatUids.Length)
            {
                HiddenStat.Set(earnedStatUid, 1f);
                Plugin.Logger.LogInfo($"[AchievementTrackerPatch] Achievement earned: {achievementName}.");
            }
        }

        /// <summary>
        /// Spiritual Overcrowding: a per-player stat read (vanilla Spiritual Noise GameStat), NOT
        /// an AllCards scan — deliberately its own poll branch, not folded into the shared scan
        /// above. Reads the LIVE max via <see cref="HiddenStat.GetMax"/> (CurrentMinMaxValue.y —
        /// perks can shift this at runtime) rather than hardcoding the JSON default of 13.
        /// </summary>
        private static void CheckSpiritualOvercrowding()
        {
            if (HiddenStat.Get(NoiseMaxEarnedStatUid) >= 0.5f) return; // already earned

            float current = HiddenStat.Get(SpiritualNoiseGameStatUid);
            if (current < 0f) return; // not readable yet

            float max = HiddenStat.GetMax(SpiritualNoiseGameStatUid);
            if (max <= 0f) return; // not readable yet (or a degenerate max)

            if (current < max) return;

            HiddenStat.Set(NoiseMaxEarnedStatUid, 1f);
            Plugin.Logger.LogInfo("[AchievementTrackerPatch] Achievement earned: Spiritual Overcrowding.");
        }

        /// <summary>
        /// Finding a Friend: has the player ever crossed paths with one of the 3 Trader NPCAgents?
        /// Its own poll branch — an NPCAgent roster check, not a card scan.
        ///
        /// <para>Uses the SAME compile-time idiom <see cref="AchievementKillEffectsPatch"/>
        /// established (direct <c>UniqueIDScriptable.GetFromID&lt;T&gt;</c>, and here
        /// <c>GameManager.Instance</c> itself) rather than
        /// <see cref="CardUtil.GetGameManagerInstance"/>'s reflection wrapper. This is safe: a
        /// compile-time <c>GameManager</c> symbol in this assembly binds at COMPILE TIME to the
        /// exact type in the referenced Assembly-CSharp.dll (the csproj references exactly one).
        /// The ModCore-shadowing risk root CLAUDE.md warns about is specific to broad RUNTIME
        /// type-NAME scans (<c>AccessTools.TypeByName</c> / a reflection <c>FindType</c> across
        /// every loaded assembly), which a statically-bound reference never performs — the same
        /// reasoning that already lets <c>GuardOutcomePatch</c> subscribe to
        /// <c>GameManager.OnEncounterEnemyDefeated</c> directly.</para>
        ///
        /// <para><b>Confirmed bug, fixed 2026-08-22 (reported: achievement showed earned on Day
        /// 2 of a fresh save):</b> <c>GameManager.FindNPC(NPCAgent)</c>
        /// (.decomp/GameManager.cs:1895) scans the whole-game <c>AllNPCs</c> roster and returns
        /// non-null as soon as ANY InGameNPC for that model exists — but per
        /// [[reference_npc_vs_card_env_divergence]], NPCAgents (Traders included) are instantiated
        /// into <c>AllNPCs</c> during the normal boot sequence
        /// (<c>GameManager.InitializeStatsAndActions</c> / <c>AssignOrCreateNPCCards</c>),
        /// independent of the player ever having been near them — existence in the roster is NOT
        /// evidence of an actual encounter. The fix: additionally require
        /// <c>npc.CurrentEnvironment.MatchesPlayerEnv</c> — a live per-poll check
        /// (<c>EnvID.MatchesPlayerEnv</c>, .decomp/EnvID.cs:89) that the trader's board is the
        /// SAME board the player is standing on right now. At a 5s poll cadence this reliably
        /// catches an actual co-location without needing to hook the Approach/Talk interaction
        /// buttons (which carry no stat-modification field to latch from).</para>
        /// </summary>
        private static void CheckFindingAFriend()
        {
            if (HiddenStat.Get(TraderFoundEarnedStatUid) >= 0.5f) return; // already earned

            var gm = GameManager.Instance;
            if (gm == null) return;

            foreach (var (npcAgentUid, label) in TraderNpcAgents)
            {
                var agent = UniqueIDScriptable.GetFromID<NPCAgent>(npcAgentUid);
                if (agent == null)
                {
                    Plugin.Logger.LogDebug($"[AchievementTrackerPatch] Trader NPCAgent '{npcAgentUid}' ({label}) not resolvable yet — retrying next poll.");
                    continue;
                }

                var npc = gm.FindNPC(agent);
                if (npc == null) continue;

                // Existing in GameManager.AllNPCs only proves the trader has been instantiated by
                // the boot sequence — NOT that the player has ever seen them. Require actual
                // co-location on the player's current board.
                if (!npc.CurrentEnvironment.MatchesPlayerEnv) continue;

                HiddenStat.Set(TraderFoundEarnedStatUid, 1f);
                Plugin.Logger.LogInfo($"[AchievementTrackerPatch] Achievement earned: Finding a Friend (crossed paths with {label}).");
                return;
            }
        }
    }
}
