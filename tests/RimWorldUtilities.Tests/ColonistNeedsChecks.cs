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

        foreach (var id in new[] { "Learning", "Play", "Hemogen", "Chemical_Alcohol", "MissingModNeed", "food", "" })
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
        check(ColonistNeedsPanel.RequiredHeight(600f) < ColonistNeedsPanel.RequiredHeight(350f), "Narrow panel gets single-column height");
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
            }, () => Player, () => { Enumerations++; return Pawns; });
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
