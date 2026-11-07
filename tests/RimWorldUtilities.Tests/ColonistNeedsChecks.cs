using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using RimWorldUtilities.UI;
using Verse;

internal static class ColonistNeedsChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunCore(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        check(!f.Service.HasEnabledNeeds && ColonistNeedsService.BasicNeeds.Count == 8, "Eight basic needs, all off");
        check(!f.Service.TrySetEnabled("Food", true), "Unavailable patch cannot be enabled");
        f.Service.SetAvailability(true);
        foreach (var id in ColonistNeedsService.BasicNeeds)
        {
            check(!f.Service.IsEnabled(id), "Default off: " + id);
            var need = f.Add(id, 2.5f);
            need.CurLevel = 0.2f;
            check(f.Service.TrySetEnabled(id, true), "Enable " + id);
            check(need.CurLevel == 2.5f && f.Durable!.EnabledColonistNeeds.Contains(id),
                $"Immediate refill uses actual maximum: {id}, level={need.CurLevel}, max={need.MaxLevel}, eligible={f.Service.IsEligible(f.Pawn)}");
            check(!f.EnabledDuringWrite, "Persist before applying: " + id);
            var writes = need.Sets;
            f.Service.Refill(f.Pawn);
            check(need.Sets == writes, "Avoid redundant setter calls at maximum: " + id);
            var enumerations = f.Enumerations;
            check(f.Service.TrySetEnabled(id, false), "Disable " + id);
            check(need.CurLevel == 2.5f && f.Enumerations == enumerations, "Disable neither reverses values nor enumerates pawns");
            need.CurLevel = 0.3f;
            f.Service.Refill(f.Pawn);
            check(need.CurLevel == 0.3f, "Disabled need stays untouched: " + id);
        }
        check(!f.Service.HasEnabledNeeds && f.Enumerations == 8, "Enumerate only on actual enables");
        var food = f.Pawn.needs.AllNeeds[0];
        f.FailWrites = true;
        check(!f.Service.TrySetEnabled("Food", true) && !f.Service.IsEnabled("Food") && food.CurLevel == 0.3f,
            "Failed persistence cannot enable or refill");
        check(f.Service.PersistenceError.Length > 0, "Expose persistence failure");
        f.FailWrites = false;
        check(f.Service.TrySetEnabled("Food", true), "Retry saving an enable");
        f.FailWrites = true;
        check(!f.Service.TrySetEnabled("Food", false) && f.Service.IsEnabled("Food"), "Failed disable retains the saved state");

        foreach (var id in new[] { "Hemogen", "Chemical_Alcohol", "MissingModNeed", "food", "" })
            check(!f.Service.TrySetEnabled(id, true), "Reject unsupported need: " + id);
        f = new Fixture();
        f.Settings.EnabledColonistNeeds.Add("MissingModNeed");
        f.Service.SetAvailability(true);
        check(!f.Service.HasEnabledNeeds && !f.Service.Refill(f.Pawn), "Unknown saved identifiers do no periodic work");
        f.Service.TrySetEnabled("Food", true);
        check(f.Pawn.needs.AllNeeds.Count == 0, "Enabling a missing need never creates it");
        var added = f.Add("Food", 0.35f);
        f.Service.Refill(f.Pawn);
        check(added.CurLevel == 0.35f, "Dynamically added need uses its actual maximum");
        added.Maximum = 0.15f;
        f.Service.Refill(f.Pawn);
        check(added.CurLevel == 0.15f, "Re-read a changed maximum and clamp through the public property");
        f.Pawn.needs.AllNeeds.Remove(added);
        added.CurLevel = 0.1f;
        f.Service.Refill(f.Pawn);
        check(added.CurLevel == 0.1f, "Removed needs are not cached");
        var unknown = f.Add("MissingModNeed", 1f);
        var noDefinition = f.Add("Food", 1f);
        noDefinition.def = null!;
        f.Service.Refill(f.Pawn);
        check(unknown.CurLevel == 0f && noDefinition.CurLevel == 0f, "Unsupported/null definitions are ignored");
        check(f.Settings.EnabledColonistNeeds.Contains("MissingModNeed"), "Preserve unknown settings for future compatibility");

        ValidateEligibility(check);
        ValidateSpecial(check);
        f = new Fixture();
        f.Service.SetAvailability(true);
        var a = f.Add("Food", 2f);
        var b = f.Add("Rest", 1f);
        f.Pawns.Add(f.Pawn); // overlapping finder routes must not refill twice.
        var traveler = new Fixture();
        CraftingChecks.SetFaction(traveler.Pawn, f.Player);
        Set(traveler.Pawn, typeof(Thing), "mapIndexOrState", (sbyte)-1);
        var travelerFood = traveler.Add("Food", 0.7f);
        f.Pawns.Add(traveler.Pawn);
        f.Service.TrySetEnabled("Food", true);
        check(a.Sets == 1 && b.Sets == 0, "Immediate enable affects only that need and deduplicates pawns");
        check(travelerFood.CurLevel == 0.7f, "Immediate enable reaches every eligible accessible pawn, including travelers");
        f.Service.TrySetEnabled("Rest", true);
        a.CurLevel = b.CurLevel = 0.1f;
        f.Service.TrySetEnabled("Food", false);
        f.Service.Refill(f.Pawn);
        check(a.CurLevel == 0.1f && b.CurLevel == 1f, "Need toggles remain independent");

        f.Settings.EnabledLimits["Steel"] = 250000;
        f.Settings.OriginalLimits["Steel"] = 83;
        f.Settings.PendingResets.Add("WoodLog");
        f.Settings.AlwaysLegendaryCrafting = true;
        f.Settings.CraftingSpeedMultiplier = 20;
        f.Service.TrySetEnabled("Mood", true);
        check(f.Durable!.EnabledLimits["Steel"] == 250000 && f.Durable.OriginalLimits["Steel"] == 83 &&
            f.Durable.PendingResets.Contains("WoodLog") && f.Durable.AlwaysLegendaryCrafting &&
            f.Durable.CraftingSpeedMultiplier == 20, "Needs writes preserve Item Editor and Crafting settings");
        var copy = f.Settings.Copy();
        check(copy.Matches(f.Settings), "Copy includes needs settings");
        copy.EnabledColonistNeeds.Clear();
        check(!copy.Matches(f.Settings) && f.Settings.EnabledColonistNeeds.Count == 2, "Independent copies and verification include needs");
        f.Settings.CopyFrom(copy);
        check(!f.Service.HasEnabledNeeds, "CopyFrom replaces enabled identifiers without stale flags");
        check(ColonistNeedsPanel.RequiredHeight(600f, false) < ColonistNeedsPanel.RequiredHeight(350f, false),
            "Narrow basic panel gets single-column height");
        check(ColonistNeedsPanel.RequiredHeight(600f, true) < ColonistNeedsPanel.RequiredHeight(350f, true),
            "Narrow special panel gets single-column height");
        check(ColonistNeedsPanel.RequiredHeight(600f, true) == ColonistNeedsPanel.RequiredHeight(600f, false) + 62f &&
            ColonistNeedsPanel.RequiredHeight(350f, true) == ColonistNeedsPanel.RequiredHeight(350f, false) + 92f,
            "Reserve Special heading, gap and checkbox rows only with Biotech");
    }

    private static void ValidateSpecial(Action<bool, string> check)
    {
        check(ColonistNeedsService.SpecialNeeds.SequenceEqual(new[] { "Learning", "Play" }), "Only Learning and Play added");
        foreach (var id in ColonistNeedsService.SpecialNeeds)
        {
            var f = new Fixture();
            check(!f.Service.IsEnabled(id) && !f.Service.HasEnabledNeeds, "Special default off: " + id);
            check(!f.Service.TrySetEnabled(id, true), "Special respects patch availability: " + id);
            f.Service.SetAvailability(true);
            var need = f.Add(id, 1.7f);
            check(f.Service.TrySetEnabled(id, true) && f.Service.HasEnabledNeeds, "Special-only configuration activates hook: " + id);
            check(!f.EnabledDuringWrite && need.CurLevel == 1.7f && f.Durable!.EnabledColonistNeeds.Contains(id),
                "Save before immediate Special refill using actual maximum: " + id);
            need.Maximum = 0.8f;
            need.CurLevel = 0.2f;
            f.Service.Refill(f.Pawn);
            check(need.CurLevel == 0.8f, "Periodic Special refill re-reads maximum: " + id);
            f.Pawn.needs.AllNeeds.Remove(need);
            need.CurLevel = 0.1f;
            var replacement = f.Add(id, 1.3f);
            f.Service.Refill(f.Pawn);
            check(need.CurLevel == 0.1f && replacement.CurLevel == 1.3f, "Special needs are never cached: " + id);
            check(f.Service.TrySetEnabled(id, false) && replacement.CurLevel == 1.3f, "Disable does not reverse Special value: " + id);
            replacement.CurLevel = 0.2f;
            f.Service.Refill(f.Pawn);
            check(!f.Service.HasEnabledNeeds && replacement.CurLevel == 0.2f, "Special disable immediately stops refill: " + id);
            f.FailWrites = true;
            check(!f.Service.TrySetEnabled(id, true) && !f.Service.IsEnabled(id) && replacement.CurLevel == 0.2f,
                "Failed Special save cannot enable or refill: " + id);
        }

        var independent = new Fixture();
        independent.Service.SetAvailability(true);
        var learning = independent.Add("Learning", 1f);
        var play = independent.Add("Play", 1f);
        var food = independent.Add("Food", 2f);
        independent.Service.TrySetEnabled("Learning", true);
        check(learning.CurLevel == 1f && play.CurLevel == 0f && food.CurLevel == 0f, "Learning enable is independent");
        independent.Service.TrySetEnabled("Play", true);
        independent.Service.TrySetEnabled("Food", true);
        independent.Service.TrySetEnabled("Learning", false);
        learning.CurLevel = play.CurLevel = food.CurLevel = 0.2f;
        independent.Service.Refill(independent.Pawn);
        check(learning.CurLevel == 0.2f && play.CurLevel == 1f && food.CurLevel == 2f, "Play and Basic remain on after Learning off");
        var copy = independent.Settings.Copy();
        check(copy.Matches(independent.Settings) && copy.EnabledColonistNeeds.SetEquals(new[] { "Play", "Food" }),
            "Existing copy and verification include Special identifiers");

        // Fixture lists model the needs provided by vanilla's developmental-stage filter.
        // The service must never add an adult need or the other juvenile stage's need.
        foreach (var (age, present) in new[] { (0, "Play"), (5, "Learning"), (25, "") })
        {
            var f = new Fixture();
            f.SetAge(age);
            f.Service.SetAvailability(true);
            if (present.Length > 0) f.Add(present, 1f);
            foreach (var id in ColonistNeedsService.SpecialNeeds) f.Service.TrySetEnabled(id, true);
            f.Service.Refill(f.Pawn);
            check(f.Pawn.needs.AllNeeds.Count == (present.Length > 0 ? 1 : 0) &&
                f.Pawn.needs.AllNeeds.All(n => n.def.defName == present && n.CurLevel == n.MaxLevel),
                "Missing/non-applicable stage needs remain absent at age " + age);
        }

        var noDlc = new Fixture { Biotech = false };
        noDlc.Service.SetAvailability(true);
        foreach (var id in ColonistNeedsService.SpecialNeeds)
        {
            check(!noDlc.Service.TrySetEnabled(id, true), "Cannot enable without Biotech: " + id);
            noDlc.Settings.EnabledColonistNeeds.Add(id); // Saved preferences from a Biotech session.
            noDlc.Add(id, 1f);
        }
        check(!noDlc.Service.SpecialNeedsAvailable && !noDlc.Service.HasEnabledNeeds && !noDlc.Service.Refill(noDlc.Pawn),
            "Saved Special preferences cannot refill without DLC");
        noDlc.Service.TrySetEnabled("Food", true);
        noDlc.Add("Food", 2f);
        noDlc.Service.Refill(noDlc.Pawn);
        check(noDlc.Pawn.needs.AllNeeds.Take(2).All(n => n.CurLevel == 0f) && noDlc.Pawn.needs.AllNeeds[2].CurLevel == 2f,
            "Basic refill remains functional without Biotech; Special stays untouched");
        check(noDlc.Durable!.EnabledColonistNeeds.IsSupersetOf(ColonistNeedsService.SpecialNeeds),
            "Saving Basic preferences preserves unavailable Special preferences");
        check(noDlc.Service.TrySetEnabled("Play", false) && !noDlc.Service.IsEnabled("Play"), "Unavailable preference can still be disabled");
    }

    private static void ValidateEligibility(Action<bool, string> check)
    {
        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("free colonist", _ => { }, true),
            ("baby", f => f.SetAge(0), true),
            ("child", f => f.SetAge(5), true),
            ("unspawned traveler", f => Set(f.Pawn, typeof(Thing), "mapIndexOrState", (sbyte)-1), true),
            ("downed colonist", f => Set(f.Pawn.health, typeof(Pawn_HealthTracker), "healthState", PawnHealthState.Down), true),
            ("prisoner of player faction", f => f.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", f => f.SetGuest(GuestStatus.Slave), false),
            ("guest", f => { f.SetGuest(GuestStatus.Guest); Set(f.Pawn.guest, typeof(Pawn_GuestTracker), "hostFactionInt", f.Player); }, false),
            ("quest lodger", f => f.MakeLodger(), false),
            ("neutral visitor", f => CraftingChecks.SetFaction(f.Pawn, Empty<Faction>()), false),
            ("hostile visitor", f => CraftingChecks.SetFaction(f.Pawn, Empty<Faction>()), false),
            ("factionless", f => CraftingChecks.SetFaction(f.Pawn, null), false),
            ("animal", f => f.Pawn.def.race.intelligence = Intelligence.Animal, false),
            ("humanlike mechanoid", f => Set(f.Pawn.def.race, typeof(RaceProperties), "fleshType", FleshTypeDefOf.Mechanoid), false),
            ("dead", f => Set(f.Pawn.health, typeof(Pawn_HealthTracker), "healthState", PawnHealthState.Dead), false),
            ("destroyed", f => CraftingChecks.Destroy(f.Pawn), false),
            ("suspended", f => f.Pawn.ForceSuspended = true, false)
        };
        foreach (var test in cases)
        {
            Current.Game.questManager.ExtraFactionQuestParts.Clear();
            var f = new Fixture();
            f.Service.SetAvailability(true);
            var need = f.Add("Food", 1f);
            test.Change(f);
            check(f.Service.IsEligible(f.Pawn) == test.Eligible, "Strict colonist eligibility: " + test.Name);
            f.Service.TrySetEnabled("Food", true);
            check(need.CurLevel == (test.Eligible ? 1f : 0f), "Immediate eligibility: " + test.Name);
            need.CurLevel = 0.2f;
            f.Service.Refill(f.Pawn);
            check(need.CurLevel == (test.Eligible ? 1f : 0.2f), "Interval eligibility: " + test.Name);
            foreach (var id in ColonistNeedsService.SpecialNeeds)
            {
                var special = f.Add(id, 1.2f);
                f.Service.TrySetEnabled(id, true);
                check(special.CurLevel == (test.Eligible ? 1.2f : 0f), "Special immediate eligibility: " + id + "/" + test.Name);
                special.CurLevel = 0.1f;
                f.Service.Refill(f.Pawn);
                check(special.CurLevel == (test.Eligible ? 1.2f : 0.1f), "Special interval eligibility: " + id + "/" + test.Name);
            }
        }
        Current.Game.questManager.ExtraFactionQuestParts.Clear();
    }

    public static void RunNative(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        var need = f.Add("Food", 2.5f);
        var target = ColonistNeedsPatch.FindTarget()!;
        ColonistNeedsPatch.Install(f.Service);
        check(f.Service.IsAvailable, "Install native needs prefix/postfix: " + f.Service.UnavailableReason);
        ColonistNeedsPatch.Install(f.Service);
        check(Harmony.GetPatchInfo(target).Prefixes.Count(p => p.owner == ColonistNeedsPatch.OwnerId) == 1 &&
            Harmony.GetPatchInfo(target).Postfixes.Count(p => p.owner == ColonistNeedsPatch.OwnerId) == 1,
            "Register exactly one prefix and postfix");
        f.SetIntervalPhase(0);
        check(f.Pawn.IsHashIntervalTick(150, 1), "Fixture matches native hash gate");
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(need.Intervals == 1, "Original executes with every toggle disabled");
        f.Service.TrySetEnabled("Food", true);
        need.CurLevel = 0.1f;
        var writes = need.Sets;
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(need.Intervals == 2 && need.BeforeNative == 2.5f && need.CurLevel == 2.5f,
            "Fill before and after the original NeedInterval exactly once");
        check(need.Sets == writes + 3, "One prefix refill, one native decrement, one postfix refill");
        f.SetIntervalPhase(1);
        need.CurLevel = 0.1f;
        writes = need.Sets;
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(need.Intervals == 2 && need.Sets == writes && need.CurLevel == 0.1f, "No update outside 150-tick gate");
        f.SetBatchedPhase();
        f.Pawn.needs.NeedsTrackerTickInterval(2);
        check(need.Intervals == 3 && need.CurLevel == 2.5f, "Honor native batched-delta hash semantics");
        f.SetIntervalPhase(150);
        f.Service.TrySetEnabled("Food", false);
        need.CurLevel = 1f;
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(need.Intervals == 4 && need.CurLevel == 0.75f, "Disabling immediately restores native-only updates");
        f.Service.TrySetEnabled("Food", true);
        var late = f.NewNeed("Food", 3f);
        need.DuringInterval = () =>
        {
            f.Pawn.needs.AllNeeds.Remove(need);
            f.Pawn.needs.AllNeeds.Add(late);
        };
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(late.CurLevel == 3f && late.Intervals == 0 && need.CurLevel == 2.25f,
            "Postfix re-reads live needs without calling the new need's interval");
        f.Pawn.needs.AllNeeds.Clear();
        f.Pawn.needs.AllNeeds.Add(need);
        need.DuringInterval = () => f.SetGuest(GuestStatus.Prisoner);
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(need.CurLevel == 2.25f, "Postfix rechecks prisoner status after native callbacks");
        f.Pawn.guest = null!;
        need.DuringInterval = () => f.Service.TrySetEnabled("Food", false);
        f.Pawn.needs.NeedsTrackerTickInterval(1);
        check(!f.Service.IsEnabled("Food") && need.CurLevel == 2.25f, "Disable during native update prevents postfix refill");
        need.DuringInterval = null;
        f.Service.TrySetEnabled("Food", false);
        f.Service.TrySetEnabled("Food", true);
        var stale = Empty<Pawn_NeedsTracker>();
        Set(stale, typeof(Pawn_NeedsTracker), "pawn", f.Pawn);
        var old = f.NewNeed("Food", 1f);
        Set(stale, typeof(Pawn_NeedsTracker), "needs", new List<Need> { old });
        stale.NeedsTrackerTickInterval(1);
        check(old.CurLevel == 0f && old.Intervals == 1, "Do not refill a detached tracker");
        ValidateSpecialNative(f, check);
    }

    private static int growthCalculations;
    private static bool FixedGrowthRate(ref float __result)
    {
        growthCalculations++;
        __result = 8f;
        return false;
    }

    private static void ValidateSpecialNative(Fixture f, Action<bool, string> check)
    {
        f.Service.TrySetEnabled("Food", false);
        f.Pawn.needs.AllNeeds.Clear();
        f.SetAge(5);
        f.Pawn.ageTracker.canGainGrowthPoints = true;
        f.Pawn.ageTracker.growthPoints = 11f;
        var learning = Empty<TestLearning>();
        learning.def = Empty<NeedDef>();
        learning.def.defName = "Learning";
        Set(learning, typeof(Need), "pawn", f.Pawn);
        f.Pawn.needs.AllNeeds.Add(learning);
        var play = Empty<TestPlay>();
        play.def = Empty<NeedDef>();
        play.def.defName = "Play";
        f.Pawn.needs.AllNeeds.Add(play);

        // Keep the real Need_Learning growth increment. Only its rate provider is
        // isolated from profile/stat/definition initialization in this test process.
        var harmony = new Harmony("slava.rimworldutilities.tests.learning-growth");
        var rate = typeof(Pawn_AgeTracker).GetProperty(nameof(Pawn_AgeTracker.GrowthPointsPerDay))!.GetMethod!;
        harmony.Patch(rate, prefix: new HarmonyMethod(typeof(ColonistNeedsChecks), nameof(FixedGrowthRate)));
        try
        {
            growthCalculations = 0;
            f.SetIntervalPhase(0);
            learning.CurLevel = play.CurLevel = 0.3f;
            f.Pawn.needs.NeedsTrackerTickInterval(1);
            check(learning.Intervals == 1 && play.Intervals == 1 && learning.CurLevel < 0.3f && play.CurLevel < 0.3f,
                "Disabled Special needs execute real native intervals and decay");
            check(Math.Abs(f.Pawn.ageTracker.growthPoints - 11.02f) < 0.00001f && growthCalculations == 1,
                "Native Learning alone awards growth points once while disabled");

            f.Service.TrySetEnabled("Learning", true);
            check(learning.CurLevel == learning.MaxLevel && play.CurLevel < 0.3f, "Immediate Learning enable without Play change");
            learning.CurLevel = 0.1f;
            var growth = f.Pawn.ageTracker.growthPoints;
            f.Pawn.needs.NeedsTrackerTickInterval(1);
            check(learning.Intervals == 2 && learning.BeforeNative == learning.MaxLevel && learning.CurLevel == learning.MaxLevel &&
                play.Intervals == 2 && play.CurLevel < 0.3f, "Special-only prefix/postfix preserves both native interval calls");
            check(Math.Abs(f.Pawn.ageTracker.growthPoints - growth - 0.02f) < 0.00001f && growthCalculations == 2,
                "Enabling Learning preserves the native growth calculation exactly once");

            f.Service.TrySetEnabled("Play", true);
            check(play.CurLevel == play.MaxLevel, "Immediate native Play refill");
            f.Service.TrySetEnabled("Learning", false);
            learning.CurLevel = play.CurLevel = 0.2f;
            f.Pawn.needs.NeedsTrackerTickInterval(1);
            check(learning.Intervals == 3 && play.Intervals == 3 && learning.CurLevel < 0.2f &&
                play.BeforeNative == play.MaxLevel && play.CurLevel == play.MaxLevel, "Play stays full while Learning resumes native decay");
            f.SetIntervalPhase(1);
            play.CurLevel = 0.2f;
            f.Pawn.needs.NeedsTrackerTickInterval(1);
            check(play.Intervals == 3 && play.CurLevel == 0.2f && growthCalculations == 3, "No Special work outside native hash gate");
            f.SetIntervalPhase(0);
            f.Service.TrySetEnabled("Play", false);
            f.Pawn.needs.NeedsTrackerTickInterval(1);
            check(play.Intervals == 4 && play.CurLevel < 0.2f && growthCalculations == 4, "Disable Play immediately leaves native updates intact");
        }
        finally { harmony.Unpatch(rate, HarmonyPatchType.Prefix, harmony.Id); }
    }

    internal sealed class Fixture
    {
        public readonly CustomStackLimitSettings Settings = new();
        public readonly TestPawn Pawn = Empty<TestPawn>();
        public readonly Faction Player = Empty<Faction>();
        public readonly List<Pawn> Pawns = new();
        public readonly ColonistNeedsService Service;
        public CustomStackLimitSettings? Durable;
        public bool FailWrites;
        public bool EnabledDuringWrite;
        public int Enumerations;
        public bool Biotech = true;

        public Fixture()
        {
            Player.def = Empty<FactionDef>();
            Player.def.isPlayer = true;
            Pawn.def = Empty<ThingDef>();
            Pawn.def.race = Empty<RaceProperties>();
            Pawn.def.race.intelligence = Intelligence.Humanlike;
            Set(Pawn.def.race, typeof(RaceProperties), "fleshType", FleshTypeDefOf.Normal);
            Pawn.health = Empty<Pawn_HealthTracker>();
            Set(Pawn.health, typeof(Pawn_HealthTracker), "healthState", PawnHealthState.Mobile);
            Pawn.thingIDNumber = 0;
            CraftingChecks.SetFaction(Pawn, Player);
            Pawn.needs = Empty<Pawn_NeedsTracker>();
            Set(Pawn.needs, typeof(Pawn_NeedsTracker), "pawn", Pawn);
            Set(Pawn.needs, typeof(Pawn_NeedsTracker), "needs", new List<Need>());
            Pawns.Add(Pawn);
            Service = new ColonistNeedsService(Settings, candidate =>
            {
                if (FailWrites) throw new IOException("Simulated needs persistence failure");
                EnabledDuringWrite = Settings.EnabledColonistNeeds.Count > 0;
                Durable = candidate.Copy();
            }, () => Player, () => { Enumerations++; return Pawns; }, () => Biotech);
        }
        public TestNeed Add(string id, float maximum)
        {
            var need = NewNeed(id, maximum);
            Pawn.needs.AllNeeds.Add(need);
            return need;
        }
        public TestNeed NewNeed(string id, float maximum)
        {
            var need = Empty<TestNeed>();
            need.def = Empty<NeedDef>();
            need.def.defName = id;
            need.Maximum = maximum;
            return need;
        }
        public void SetGuest(GuestStatus status)
        {
            Pawn.guest = Empty<Pawn_GuestTracker>();
            Pawn.guest.guestStatusInt = status;
        }
        public void SetAge(int years)
        {
            Pawn.ageTracker = Empty<Pawn_AgeTracker>();
            Set(Pawn.ageTracker, typeof(Pawn_AgeTracker), "ageBiologicalTicksInt", years * 3600000L);
        }
        public void SetIntervalPhase(int phase) => Set(Current.Game.tickManager, typeof(TickManager), "ticksGameInt",
            (150 - Pawn.thingIDNumber.HashOffset() % 150) % 150 + phase);
        public void SetBatchedPhase()
        {
            for (var phase = 0; phase < 150; phase++)
            {
                SetIntervalPhase(phase);
                if (!Pawn.IsHashIntervalTick(150, 1) && Pawn.IsHashIntervalTick(150, 2)) return;
            }
            throw new Exception("No batched-only native hash phase found.");
        }
        public void MakeLodger()
        {
            Current.Game.questManager.ExtraFactionQuestParts.Add(new QuestPart_ExtraFaction
            {
                quest = Empty<Quest>(), affectedPawns = new() { Pawn },
                extraFaction = new ExtraFaction(Player, ExtraFactionType.HomeFaction)
            });
        }
    }

    internal sealed class TestPawn : Pawn
    {
        public bool ForceSuspended;
        public override bool Suspended => ForceSuspended;
    }
    internal sealed class TestNeed : Need
    {
        public float Maximum;
        public int Sets;
        public int Intervals;
        public float BeforeNative;
        public Action? DuringInterval;
        private TestNeed(Pawn pawn) : base(pawn) { }
        public override float MaxLevel => Maximum;
        public override float CurLevel { get => base.CurLevel; set { Sets++; base.CurLevel = value; } }
        public override void NeedInterval()
        {
            Intervals++;
            BeforeNative = CurLevel;
            CurLevel -= 0.25f;
            DuringInterval?.Invoke();
        }
    }
    private sealed class TestLearning : Need_Learning
    {
        public int Intervals;
        public float BeforeNative;
        private TestLearning(Pawn pawn) : base(pawn) { }
        protected override bool IsFrozen => false;
        public override void NeedInterval() { Intervals++; BeforeNative = CurLevel; base.NeedInterval(); }
    }
    private sealed class TestPlay : Need_Play
    {
        public int Intervals;
        public float BeforeNative;
        private TestPlay(Pawn pawn) : base(pawn) { }
        protected override bool IsFrozen => false;
        public override void NeedInterval() { Intervals++; BeforeNative = CurLevel; base.NeedInterval(); }
    }
    private sealed class Scope : IDisposable
    {
        private readonly Game previousGame = Current.Game;
        private readonly FleshTypeDef previousNormal;
        private readonly FleshTypeDef previousMech;
        public Scope()
        {
            var field = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previousBinding = field.GetValue(null);
            try { field.SetValue(null, true); RuntimeHelpers.RunClassConstructor(typeof(FleshTypeDefOf).TypeHandle); }
            finally { field.SetValue(null, previousBinding); }
            previousNormal = FleshTypeDefOf.Normal;
            previousMech = FleshTypeDefOf.Mechanoid;
            FleshTypeDefOf.Normal = Empty<FleshTypeDef>();
            FleshTypeDefOf.Mechanoid = Empty<FleshTypeDef>();
            var game = Empty<Game>();
            game.tickManager = Empty<TickManager>();
            game.questManager = new QuestManager();
            Current.Game = game;
        }
        public void Dispose()
        {
            Current.Game = previousGame;
            FleshTypeDefOf.Normal = previousNormal;
            FleshTypeDefOf.Mechanoid = previousMech;
        }
    }
    private static T Empty<T>() where T : class => CraftingChecks.Empty<T>();
    private static void Set(object value, Type type, string field, object content) =>
        type.GetField(field, PrivateInstance)!.SetValue(value, content);
}
