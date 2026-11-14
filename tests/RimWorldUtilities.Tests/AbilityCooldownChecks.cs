using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldUtilities;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

internal static class AbilityCooldownChecks
{
    public static void RunCore(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        check(!f.Service.DisableCooldowns && !f.Service.Enabled, "Cooldown bypass defaults off");
        check(!f.Service.TrySetDisableCooldowns(true), "Unavailable cooldown patches cannot be enabled");
        f.Service.SetAvailability(true);
        f.Settings.AlwaysLegendaryCrafting = true;
        f.Settings.EnabledColonistNeeds.Add("Food");
        check(f.Service.TrySetDisableCooldowns(true) && f.Durable!.DisableAbilityCooldowns && !f.EnabledDuringWrite,
            "Cooldown toggle persists before applying");
        check(f.Durable!.AlwaysLegendaryCrafting && f.Durable.EnabledColonistNeeds.Contains("Food"), "Cooldown toggle preserves other settings");
        var copy = f.Settings.Copy();
        check(copy.Matches(f.Settings), "Cooldown setting participates in copies");
        copy.DisableAbilityCooldowns = false;
        check(!copy.Matches(f.Settings), "Cooldown setting participates in verification");
        f.FailWrites = true;
        check(!f.Service.TrySetDisableCooldowns(false) && f.Service.DisableCooldowns && f.Service.PersistenceError.Length > 0,
            "Failed cooldown persistence retains durable state");

        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("player colonist", _ => { }, true),
            ("enemy", x => CraftingChecks.SetFaction(x.Pawn, Empty<Faction>()), false),
            ("neutral visitor", x => CraftingChecks.SetFaction(x.Pawn, Empty<Faction>()), false),
            ("factionless", x => CraftingChecks.SetFaction(x.Pawn, null), false),
            ("prisoner", x => x.Base.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", x => x.Base.SetGuest(GuestStatus.Slave), false),
            ("guest", x => { x.Base.SetGuest(GuestStatus.Guest); CraftingChecks.SetFaction(x.Pawn, Empty<Faction>()); }, false),
            ("colonist with inactive guest tracker", x => x.Base.SetGuest(GuestStatus.Guest), true),
            ("hosted colonist", x => { x.Base.SetGuest(GuestStatus.Guest); Set(x.Pawn.guest, "hostFactionInt", x.Player); }, false),
            ("quest lodger", x => x.Base.MakeLodger(), false),
            ("mental state", x => Set(x.Pawn.mindState.mentalStateHandler, "curStateInt", new TestMentalState { def = Empty<MentalStateDef>() }), false),
            ("unspawned/caravan", x => Set(x.Pawn, typeof(Thing), "mapIndexOrState", (sbyte)-1), false),
            ("suspended", x => x.Pawn.ForceSuspended = true, false),
            ("mechanoid", x => { x.Pawn.def.race.intelligence = Intelligence.ToolUser; Set(x.Pawn.def.race, "fleshType", FleshTypeDefOf.Mechanoid); }, false),
            ("destroyed", x => CraftingChecks.Destroy(x.Pawn), false),
            ("dead", x => Set(x.Pawn.health, "healthState", PawnHealthState.Dead), false)
        };
        foreach (var test in cases)
        {
            Current.Game.questManager.ExtraFactionQuestParts.Clear();
            f = new Fixture();
            f.Service.SetAvailability(true);
            f.Service.TrySetDisableCooldowns(true);
            test.Change(f);
            check(f.Service.IsEligible(f.Pawn) == test.Eligible && f.Service.CanBypass(f.Ability) == test.Eligible,
                "Dynamic cooldown eligibility: " + test.Name);
        }
        Current.Game.questManager.ExtraFactionQuestParts.Clear();
        f = new Fixture();
        f.Service.SetAvailability(true);
        f.Service.TrySetDisableCooldowns(true);
        check(f.Service.CanBypass(f.Ability), "Ordinary ability supported");
        var psycast = Empty<Psycast>();
        psycast.pawn = f.Pawn;
        psycast.def = f.Ability.def;
        check(f.Service.CanBypass(psycast), "Standard psycast cooldown supported");
        var custom = Empty<CustomAbility>();
        custom.pawn = f.Pawn;
        custom.def = f.Ability.def;
        check(!f.Service.CanBypass(custom), "Unverified derived ability category excluded");
        foreach (var change in new Action<Ability>[] { a => a.maxCharges = 2, a => a.def.charges = 2,
                     a => a.def.cooldownPerCharge = true, a => a.def.hasExternallyHandledCooldown = true })
        {
            var a = NewAbility(f.Pawn);
            change(a);
            check(!f.Service.CanBypass(a), "Charge/external cooldown category excluded");
        }
        ValidatePatterns(check);
    }

    private static void ValidateRitualContext(Fixture f, Action<bool, string> check)
    {
        var service = new RitualCooldownService(f.Service);
        foreach (var id in new[] { "LeaderSpeech", "Conversion" })
        {
            f.Ritual.def.defName = id;
            check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target, f.Pawn) == -1, "Player ritual bypass: " + id);
            check(f.Ritual.abilityOnCooldownUntilTick == 5000, "Shared deadline never changes: " + id);
        }
        f.Ritual.def.defName = "OtherRitual";
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000, "Other ritual categories unchanged");
        f.Ritual.def.defName = "LeaderSpeech";
        var npc = Empty<Faction>();
        npc.ideos = new FactionIdeosTracker(npc);
        Set(npc.ideos, "primaryIdeo", f.Ritual.ideo);
        CraftingChecks.SetFaction(f.Pawn, npc);
        check(npc.ideos.Has(f.Ritual.ideo) && service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000,
            "NPC role holder sharing the identical ideology retains deadline");
        CraftingChecks.SetFaction(f.Pawn, f.Player);
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == -1, "Role holder regaining ownership is reevaluated");
        Set(f.Map.info.parent, typeof(WorldObject), "factionInt", npc);
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000, "NPC-owned map sharing ideology retains deadline");
        Set(f.Map.info.parent, typeof(WorldObject), "factionInt", f.Player);
        f.Role.chosenPawn.pawn = null!;
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000, "Unassigned role never receives bypass");
        f.Role.chosenPawn.pawn = f.Pawn;
        var selected = Empty<Pawn>();
        CraftingChecks.SetFaction(selected, npc);
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target, selected) == 5000, "NPC selection never receives bypass");
        Set(f.Pawn, typeof(Thing), "mapIndexOrState", (sbyte)1);
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000, "Role holder on another map never receives bypass");
        Set(f.Pawn, typeof(Thing), "mapIndexOrState", (sbyte)0);
        f.Service.TrySetDisableCooldowns(false);
        check(service.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 5000 &&
            !service.CanConfirmPlayerDialog(f.Ritual, f.Target, f.Assignments, true), "Disable restores both ritual gates in an open dialog");
        f.Service.TrySetDisableCooldowns(true);
        f.Assigned["leader"][0] = selected;
        check(!service.CanConfirmPlayerDialog(f.Ritual, f.Target, f.Assignments, true), "Actual NPC assignment cannot inherit player bypass");
        f.Assigned["leader"][0] = f.Pawn;
        CraftingChecks.SetFaction(f.Pawn, npc);
        check(!service.CanConfirmPlayerDialog(f.Ritual, f.Target, f.Assignments, true), "Open dialog rejects lost player ownership");
    }

    private static void ValidatePatterns(Action<bool, string> check)
    {
        foreach (var target in new[] { AbilityCooldownPatch.CanCastGetter, AbilityCooldownPatch.CooldownTick })
        {
            var il = PatchProcessor.GetOriginalInstructions(target);
            check(AbilityCooldownPatch.TryRewrite(target, il, out var rewritten, out _), "Actual ability assembly pattern: " + target.Name);
            check(rewritten.Count == il.Count, "Ability rewrite preserves stack shape and instruction count");
            check(!AbilityCooldownPatch.TryRewrite(target, Array.Empty<CodeInstruction>(), out _, out _), "Reject missing ability IL");
            check(!AbilityCooldownPatch.TryRewrite(target, il.Concat(il).ToList(), out _, out _), "Reject ambiguous ability IL");
            check(!AbilityCooldownPatch.TryRewrite(target, rewritten, out _, out _), "Reject duplicate ability rewrite");
        }
        var iterator = RitualCooldownPatch.FindOptionsIterator();
        check(iterator?.ReturnType == typeof(bool), "Resolve ritual menu iterator through metadata");
        foreach (var target in new[] { RitualCooldownPatch.CommandTarget, iterator! })
        {
            var il = PatchProcessor.GetOriginalInstructions(target);
            check(RitualCooldownPatch.TryRewrite(target, il, out var rewritten, out _), "Actual ritual assembly pattern: " + target.Name);
            check(!rewritten.Any(i => i.LoadsField(RitualCooldownPatch.DeadlineField)), "Replace every contextual deadline read");
            check(!RitualCooldownPatch.TryRewrite(target, Array.Empty<CodeInstruction>(), out _, out _), "Reject missing ritual IL");
            check(!RitualCooldownPatch.TryRewrite(target, il.Concat(il).ToList(), out _, out _), "Reject ambiguous ritual IL");
            check(!RitualCooldownPatch.TryRewrite(target, rewritten, out _, out _), "Reject duplicate ritual rewrite");
        }
    }

    public static void RunNative(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture(true);
        AbilityCooldownPatch.Install(f.Service);
        check(f.Service.IsAvailable, "Install all native cooldown patches: " + f.Service.UnavailableReason);
        AbilityCooldownPatch.Install(f.Service);
        check(Harmony.GetPatchInfo(AbilityCooldownPatch.CanCastGetter).Transpilers.Count(p => p.owner == AbilityCooldownPatch.OwnerId) == 1,
            "Cooldown install is idempotent");
        f.Ability.StartCooldown(500);
        check(f.Ability.OnCooldown && f.Ability.CooldownTicksRemaining == 500 && !f.Ability.CanCast, "Disabled checkbox leaves native cooldown blocking");
        f.Service.TrySetDisableCooldowns(true);
        check(!f.Ability.OnCooldown && f.Ability.CooldownTicksRemaining == 0 && f.Ability.CanCast, "Existing player cooldown stops blocking immediately");
        check(AbilityCooldownPatch.RawTicksRemaining(f.Ability) == 500, "Bypass retains raw player deadline");
        AbilityCooldownPatch.CooldownTick.Invoke(f.Ability, null);
        check(AbilityCooldownPatch.RawOnCooldown(f.Ability) && AbilityCooldownPatch.RawTicksRemaining(f.Ability) == 500,
            "Vanilla tick uses raw time and cannot prematurely clear stored state");
        var npc = Empty<Faction>();
        CraftingChecks.SetFaction(f.Pawn, npc);
        check(f.Ability.OnCooldown && f.Ability.CooldownTicksRemaining == 500 && !f.Ability.CanCast, "Ownership transfer immediately restores NPC cooldown");
        CraftingChecks.SetFaction(f.Pawn, f.Player);
        f.Ability.comps = new() { new BlockingComp() };
        check(!f.Ability.CanCast, "Component/resource restrictions survive cooldown bypass");
        f.Ability.comps.Clear();
        f.Ability.StartCooldown(800);
        check(f.Ability.CanCast && AbilityCooldownPatch.RawTicksRemaining(f.Ability) == 800, "New cooldown assignments remain native while ignored for player");
        f.Service.TrySetDisableCooldowns(false);
        check(f.Ability.OnCooldown && f.Ability.CooldownTicksRemaining == 800 && !f.Ability.CanCast, "Disable immediately restores elapsed native cooldown");
        f.Service.TrySetDisableCooldowns(true);
        var charged = NewAbility(f.Pawn);
        charged.maxCharges = 2;
        charged.StartCooldown(500);
        check(charged.OnCooldown && charged.CooldownTicksRemaining == 500 && charged.RemainingCharges == 2 && !charged.CanCast,
            "Charged ability retains native cooldown and charges");
        var external = NewAbility(f.Pawn);
        external.def.hasExternallyHandledCooldown = true;
        external.StartCooldown(500);
        check(external.OnCooldown && external.CooldownTicksRemaining == 500 && !external.CanCast, "External cooldown remains untouched");

        // Source ability uses the same read policy; notifications still update the
        // identical shared precept and can never be suppressed for an NPC observer.
        f.Ability.Notify_GroupStartedCooldown(f.Ability.def.groupDef, 900);
        f.Ritual.Notify_CooldownFromAbilityStarted(900);
        var rituals = new RitualCooldownService(f.Service);
        check(f.Ritual.abilityOnCooldownUntilTick == 1000 && f.Ability.CanCast &&
            rituals.DeadlineForPlayerInteraction(f.Ritual, f.Target) == -1, "Player source-ability and precept gates both bypassed without suppressing assignments");
        check(rituals.CanConfirmPlayerDialog(f.Ritual, f.Target, f.Assignments, true), "Eligible assigned player role can confirm ritual");
        CraftingChecks.SetFaction(f.Pawn, npc);
        check(f.Ability.OnCooldown && f.Ability.CooldownTicksRemaining == 900 &&
            rituals.DeadlineForPlayerInteraction(f.Ritual, f.Target) == 1000, "NPC observes intact source and shared-precept cooldowns");
        CraftingChecks.SetFaction(f.Pawn, f.Player);
        f.Ritual.abilityOnCooldownUntilTick = 5000;
        ValidateRitualContext(f, check);
        ValidatePersistence(check);
    }

    private static void ValidatePersistence(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-cooldowns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            var settings = new CustomStackLimitSettings { ResearchSpeedMultiplier = 5 };
            var store = new CustomStackLimitSettingsStore(path);
            var service = new AbilityCooldownService(settings, store.Save);
            service.SetAvailability(true);
            foreach (var enabled in new[] { true, false })
            {
                check(service.TrySetDisableCooldowns(enabled), "Scribe persists cooldown toggle");
                check(CustomStackLimitSettingsStore.Read(path).Matches(settings), "Cooldown settings round-trip retains unrelated settings");
                Program.RunFreshProcess(path, enabled ? "--cooldowns-on" : "--cooldowns-off");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void VerifyRestart(string path, bool expected)
    {
        var settings = CustomStackLimitSettingsStore.Read(path);
        if (settings.DisableAbilityCooldowns != expected || settings.ResearchSpeedMultiplier != 5)
            throw new Exception("Cooldown setting did not survive a fresh process.");
        Console.WriteLine("Fresh-process cooldown settings passed.");
    }

    private sealed class Fixture
    {
        public readonly ColonistNeedsChecks.Fixture Base = new();
        public ColonistNeedsChecks.TestPawn Pawn => Base.Pawn;
        public Faction Player => Base.Player;
        public CustomStackLimitSettings Settings => Base.Settings;
        public readonly AbilityCooldownService Service;
        public readonly Ability Ability;
        public readonly Map Map = Empty<Map>();
        public readonly Precept_Ritual Ritual = Empty<Precept_Ritual>();
        public readonly Precept_RoleSingle Role = Empty<Precept_RoleSingle>();
        public readonly RitualRoleAssignments Assignments = Empty<RitualRoleAssignments>();
        public readonly Dictionary<string, List<Pawn>> Assigned = new();
        public CustomStackLimitSettings? Durable;
        public bool FailWrites;
        public bool EnabledDuringWrite;
        public TargetInfo Target => new TargetInfo(new IntVec3(1, 0, 1), Map);

        public Fixture(bool world = false)
        {
            Map.info = new MapInfo { isPocketMap = true };
            if (world)
            {
                Map.info.parent = Empty<MapParent>();
                Set(Map.info.parent, typeof(WorldObject), "tile", PlanetTile.Invalid);
                Set(Map.info.parent, typeof(WorldObject), "factionInt", Player);
            }
            Map.pocketTileInfo = new Tile { PrimaryBiome = Empty<BiomeDef>() };
            Set(Current.Game, "maps", new List<Map> { Map, Empty<Map>() });
            Pawn.mindState = Empty<Pawn_MindState>();
            Pawn.mindState.mentalStateHandler = new MentalStateHandler(Pawn);
            Pawn.jobs = Empty<Pawn_JobTracker>();
            Set(Pawn.jobs, "jobQueue", new JobQueue());
            Service = new AbilityCooldownService(Settings, candidate =>
            {
                if (FailWrites) throw new IOException("Simulated cooldown write failure");
                EnabledDuringWrite = Settings.DisableAbilityCooldowns;
                Durable = candidate.Copy();
            }, () => Player);
            Ability = NewAbility(Pawn);
            var ideo = Empty<Ideo>();
            Set(ideo, "precepts", new List<Precept> { Ritual, Role });
            Set(ideo, "cachedPossibleRoles", new List<Precept_Role> { Role });
            Pawn.ideo = Empty<Pawn_IdeoTracker>();
            Set(Pawn.ideo, "ideo", ideo);
            Player.ideos = new FactionIdeosTracker(Player);
            Set(Player.ideos, "primaryIdeo", ideo);
            Ritual.ideo = Role.ideo = ideo;
            Ritual.def = Empty<PreceptDef>();
            Ritual.def.defName = "LeaderSpeech";
            Ritual.def.sourceAbilityDef = Ability.def;
            Role.def = Empty<PreceptDef>();
            Role.def.defName = "IdeoRole_Leader";
            Ritual.def.sourcePawnRoleDef = Role.def;
            Role.chosenPawn = new IdeoRoleInstance(Role) { pawn = Pawn, abilities = new() { Ability } };
            Ability.sourcePrecept = Role;
            Ability.def.groupDef = Empty<AbilityGroupDef>();
            Ability.def.groupDef.cooldownTicks = 900;
            Ability.def.groupDef.ritualRoleIds = new() { "leader" };
            Ritual.def.useCooldownFromAbilityGroupDef = Ability.def.groupDef;
            Ritual.abilityOnCooldownUntilTick = 5000;
            Set(Assignments, "ritual", Ritual);
            Set(Assignments, "allRoles", new List<RitualRole> { new RitualRoleColonist { id = "leader" } });
            Assigned["leader"] = new() { Pawn };
            Set(Assignments, "assignedRoles", Assigned);
        }
    }

    private static Ability NewAbility(Pawn pawn)
    {
        var ability = Empty<Ability>();
        ability.pawn = pawn;
        ability.def = Empty<AbilityDef>();
        ability.def.charges = -1;
        ability.def.cooldownTicksRange = new IntRange(500, 500);
        ability.def.verbProperties = new VerbProperties { useableInPocketMaps = true, useableInVacuum = true };
        return ability;
    }

    private sealed class CustomAbility : Ability { }
    private sealed class BlockingComp : AbilityComp { public override bool CanCast => false; }
    private sealed class TestMentalState : MentalState { }

    private sealed class Scope : IDisposable
    {
        private readonly Game previous = Current.Game;
        private readonly FleshTypeDef previousNormal = FleshTypeDefOf.Normal;
        private readonly FleshTypeDef previousMech = FleshTypeDefOf.Mechanoid;
        public Scope()
        {
            FleshTypeDefOf.Normal = Empty<FleshTypeDef>();
            FleshTypeDefOf.Mechanoid = Empty<FleshTypeDef>();
            Current.Game = Empty<Game>();
            Current.Game.tickManager = Empty<TickManager>();
            Set(Current.Game.tickManager, "ticksGameInt", 100);
            Current.Game.questManager = new QuestManager();
        }
        public void Dispose()
        {
            Current.Game = previous;
            FleshTypeDefOf.Normal = previousNormal;
            FleshTypeDefOf.Mechanoid = previousMech;
        }
    }

    private static T Empty<T>() where T : class => CraftingChecks.Empty<T>();
    private static void Set(object value, string field, object content) => Set(value, value.GetType(), field, content);
    private static void Set(object value, Type type, string field, object content) => AccessTools.Field(type, field).SetValue(value, content);
}
