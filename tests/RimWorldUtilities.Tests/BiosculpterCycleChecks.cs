using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldUtilities;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

internal static class BiosculpterCycleChecks
{
    private static readonly FieldInfo Remaining = AccessTools.Field(typeof(CompBiosculpterPod), "currentCycleTicksRemaining");
    private static readonly FieldInfo CycleKey = AccessTools.Field(typeof(CompBiosculpterPod), "currentCycleKey");
    private static readonly (Type Type, string Key, float Days)[] Cycles =
    {
        (typeof(CompBiosculpterPod_MedicCycle), "medic", 6f),
        (typeof(CompBiosculpterPod_RegenerationCycle), "bioregeneration", 25f),
        (typeof(CompBiosculpterPod_AgeReversalCycle), "ageReversal", 8f),
        (typeof(CompBiosculpterPod_PleasureCycle), "pleasure", 4f)
    };

    public static void RunCore(Action<bool, string> check)
    {
        var settings = new CustomStackLimitSettings { DisableAbilityCooldowns = true };
        CustomStackLimitSettings? durable = null;
        var failWrites = false;
        var enabledDuringWrite = false;
        var ideology = true;
        var service = new BiosculpterCycleService(settings, candidate =>
        {
            if (failWrites) throw new IOException("Simulated biosculpter write failure");
            enabledDuringWrite = settings.FastBiosculpterCycles;
            durable = candidate.Copy();
        }, () => null, () => ideology);
        check(!service.FastCycles && !service.Enabled, "Biosculpter defaults off");
        check(!service.TrySetFastCycles(true), "Unavailable biosculpter patch cannot enable");
        service.SetAvailability(true);
        check(service.TrySetFastCycles(true) && durable!.FastBiosculpterCycles && !enabledDuringWrite && durable.DisableAbilityCooldowns,
            "Biosculpter saves before applying and preserves cooldown setting");
        var copy = settings.Copy();
        check(copy.Matches(settings), "Biosculpter setting participates in copies");
        copy.FastBiosculpterCycles = false;
        check(!copy.Matches(settings), "Biosculpter setting participates in verification");
        settings.CopyFrom(copy);
        check(!service.FastCycles && settings.DisableAbilityCooldowns, "CopyFrom keeps settings independent");
        service.TrySetFastCycles(true);
        failWrites = true;
        check(!service.TrySetFastCycles(false) && service.FastCycles && service.PersistenceError.Length > 0,
            "Failed persistence leaves the saved biosculpter setting active");
        failWrites = false;
        ideology = false;
        check(!service.Enabled && !service.TrySetFastCycles(true) && service.TrySetFastCycles(false),
            "DLC gate blocks saved preference and enables but allows disabling");
        foreach (var description in Cycles)
        {
            var cycle = (CompBiosculpterPod_Cycle)RuntimeHelpers.GetUninitializedObject(description.Type);
            cycle.props = new CompProperties_BiosculpterPod_HealingCycle { durationDays = description.Days };
            foreach (var original in new[] { 0.1f, 0.7f, 1f, 2.5f, 1000f })
            {
                var speed = BiosculpterCycleService.SpeedForCycle(cycle, original);
                check(speed == description.Days * GenDate.TicksPerDay / 2500f && CountTicks(description.Days * GenDate.TicksPerDay, speed) == 2500,
                    "All vanilla cycle rates establish one hour: " + description.Key);
            }
            cycle.Props.durationDays = 6.12345f;
            check(Math.Abs(CountTicks(cycle.Props.durationDays * GenDate.TicksPerDay,
                BiosculpterCycleService.SpeedForCycle(cycle, 1f)) - 2500) <= 1, "Fractional duration permits float rounding: " + description.Key);
            foreach (var days in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
            {
                cycle.Props.durationDays = days;
                check(BiosculpterCycleService.SpeedForCycle(cycle, 1.75f) == 1.75f, "Invalid definition falls back to native: " + days);
            }
        }
        check(BiosculpterCycleService.SpeedForCycle(new CustomCycle(), 1.75f) == 1.75f &&
            BiosculpterCycleService.SpeedForCycle(null, 1.75f) == 1.75f, "Missing and custom cycles retain native rate");
    }

    private static void ValidateEligibilityAndRates(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        check(!f.Service.FastCycles && !f.Service.Enabled, "Biosculpter defaults off");
        check(!f.Service.TrySetFastCycles(true), "Unavailable biosculpter patch cannot enable");
        f.Service.SetAvailability(true);
        f.Settings.DisableAbilityCooldowns = true;
        check(f.Service.TrySetFastCycles(true) && f.Durable!.FastBiosculpterCycles && !f.EnabledDuringWrite,
            "Biosculpter persists before applying");
        check(f.Durable!.DisableAbilityCooldowns, "Biosculpter write preserves independent cooldown setting");
        var copy = f.Settings.Copy();
        check(copy.Matches(f.Settings), "Biosculpter participates in settings copy");
        copy.FastBiosculpterCycles = false;
        check(!copy.Matches(f.Settings), "Biosculpter participates in settings verification");
        f.Settings.CopyFrom(copy);
        check(!f.Service.FastCycles && f.Settings.DisableAbilityCooldowns, "CopyFrom restores independent biosculpter value");
        f.Service.TrySetFastCycles(true);
        f.FailWrites = true;
        check(!f.Service.TrySetFastCycles(false) && f.Service.FastCycles && f.Service.PersistenceError.Length > 0,
            "Failed biosculpter write retains durable setting");

        foreach (var cycle in Cycles)
        {
            f = new Fixture();
            f.Start(cycle);
            f.Service.SetAvailability(true);
            f.Service.TrySetFastCycles(true);
            check(!f.Pawn.Spawned && f.Pawn.Suspended && f.Service.IsEligible(f.Pod, f.Pawn),
                "Actual contained, despawned and suspended colonist qualifies: " + cycle.Key);
            foreach (var original in new[] { 0.1f, 0.7f, 1f, 2.5f, 1000f })
            {
                var speed = f.Service.SpeedFor(f.Pod, f.Pawn, original);
                check(speed == cycle.Days * GenDate.TicksPerDay / 2500f && CountTicks(f.Work, speed) == 2500,
                    "Fixed hour overrides native bonuses, including faster rates: " + cycle.Key);
            }
            var progress = f.Work * 0.375f;
            f.Work = progress;
            f.Service.TrySetFastCycles(false);
            check(f.Work == progress && f.Service.SpeedFor(f.Pod, f.Pawn, 1.25f) == 1.25f,
                "Disable preserves partial work and restores native rate: " + cycle.Key);
            f.Settings.DisableAbilityCooldowns = false;
            f.Service.TrySetFastCycles(true);
            check(f.Work == progress && CountTicks(progress, f.Service.SpeedFor(f.Pod, f.Pawn, 1f)) is >= 937 and <= 938,
                "Enable accelerates only remaining work independently of cooldowns: " + cycle.Key);
        }

        var cases = new (string Name, Action<Fixture> Change)[]
        {
            ("NPC in player pod", x => CraftingChecks.SetFaction(x.Pawn, Empty<Faction>())),
            ("factionless occupant", x => CraftingChecks.SetFaction(x.Pawn, null)),
            ("prisoner in player pod", x => x.Base.SetGuest(GuestStatus.Prisoner)),
            ("slave in player pod", x => x.Base.SetGuest(GuestStatus.Slave)),
            ("hosted visitor", x => { x.Base.SetGuest(GuestStatus.Guest); Set(x.Pawn.guest, "hostFactionInt", x.Player); }),
            ("quest lodger", x => x.Base.MakeLodger()),
            ("quest-hosted pawn", x => Current.Game.questManager.ExtraFactionQuestParts.Add(new QuestPart_ExtraFaction
                { quest = Empty<Quest>(), affectedPawns = new() { x.Pawn }, extraFaction = new ExtraFaction(x.Player, ExtraFactionType.HostFaction) })),
            ("mental state", x => Set(x.Pawn.mindState.mentalStateHandler, "curStateInt", new TestMentalState { def = Empty<MentalStateDef>() })),
            ("animal", x => x.Pawn.def.race.intelligence = Intelligence.Animal),
            ("mechanoid", x => Set(x.Pawn.def.race, "fleshType", FleshTypeDefOf.Mechanoid)),
            ("dead occupant", x => Set(x.Pawn.health, "healthState", PawnHealthState.Dead)),
            ("destroyed occupant", x => CraftingChecks.Destroy(x.Pawn)),
            ("NPC pod", x => CraftingChecks.SetFaction(x.Parent, Empty<Faction>())),
            ("unowned pod", x => CraftingChecks.SetFaction(x.Parent, null)),
            ("NPC map", x => Set(x.Map.info.parent, "factionInt", Empty<Faction>())),
            ("unowned map", x => Set(x.Map.info.parent, "factionInt", null!)),
            ("despawned pod", x => Set(x.Parent, "mapIndexOrState", (sbyte)-1)),
            ("destroyed pod", x => CraftingChecks.Destroy(x.Parent)),
            ("stale occupant reference", x => x.Pawn.holdingOwner = new ThingOwner<Thing>()),
            ("entry transition without containment", x => { x.Pod.GetDirectlyHeldThings().Remove(x.Pawn); Set(x.Pod, "pawnEnteringBiosculpter", x.Pawn); }),
            ("unavailable DLC", x => x.Ideology = false),
            ("unavailable patch", x => x.Service.SetAvailability(false)),
            ("disabled setting", x => x.Settings.FastBiosculpterCycles = false)
        };
        foreach (var test in cases)
        {
            Current.Game.questManager.ExtraFactionQuestParts.Clear();
            f = new Fixture();
            f.Service.SetAvailability(true);
            f.Service.TrySetFastCycles(true);
            test.Change(f);
            foreach (var original in new[] { 0.7f, -0f, float.PositiveInfinity, BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234)) })
                check(BitConverter.SingleToInt32Bits(f.Service.SpeedFor(f.Pod, f.Pawn, original)) == BitConverter.SingleToInt32Bits(original),
                    "Ineligible factor remains bit-for-bit unchanged: " + test.Name);
        }
        Current.Game.questManager.ExtraFactionQuestParts.Clear();
        f = new Fixture();
        f.Service.SetAvailability(true);
        f.Ideology = false;
        check(!f.Service.TrySetFastCycles(true), "Cannot enable biosculpter without Ideology");
        f.Settings.FastBiosculpterCycles = true;
        check(f.Service.TrySetFastCycles(false), "Unavailable saved preference can be disabled");
        f.Ideology = true;
        f.Service.TrySetFastCycles(true);
        f.Base.SetGuest(GuestStatus.Guest);
        check(f.Service.IsEligible(f.Pod, f.Pawn), "Inactive guest tracker does not exclude a colonist");
        Set(f.Pawn.health, "healthState", PawnHealthState.Down);
        check(f.Service.IsEligible(f.Pod, f.Pawn), "Downed free colonist remains eligible");
        foreach (var days in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
        {
            f.Pod.CurrentCycle.Props.durationDays = days;
            check(f.Service.SpeedFor(f.Pod, f.Pawn, 1.75f) == 1.75f, "Invalid duration falls back to native: " + days);
        }
        f.Start((typeof(CustomCycle), "custom", 10f));
        check(f.Service.SpeedFor(f.Pod, f.Pawn, 1.75f) == 1.75f, "Custom cycle remains native");
        CycleKey.SetValue(f.Pod, null);
        check(f.Service.SpeedFor(f.Pod, f.Pawn, 1.75f) == 1.75f, "No active cycle remains native");
        check(f.Service.SpeedFor(null, f.Pawn, 1.75f) == 1.75f && f.Service.SpeedFor(f.Pod, null, 1.75f) == 1.75f,
            "Missing pod or pawn remains native");
        f.Start((typeof(CompBiosculpterPod_MedicCycle), "medic", 6.12345f));
        check(Math.Abs(CountTicks(f.Work, f.Service.SpeedFor(f.Pod, f.Pawn, 1f)) - 2500) <= 1,
            "Fractional definition duration tolerates native float rounding");
        var target = BiosculpterCyclePatch.FindTarget();
        check(target?.ReturnType == typeof(float) && !target.IsStatic, "Verified shared speed API");
        var getter = AccessTools.PropertyGetter(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CycleSpeedFactor));
        check(PatchProcessor.GetOriginalInstructions(getter).Any(i => i.Calls(target!)), "Occupied speed getter delegates to shared helper");
        var tick = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompTick)));
        check(tick.Count(i => i.Calls(getter)) == 1 && tick.Any(i => i.StoresField(Remaining)), "Native tick consumes speed and owns remaining work");
        check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompInspectStringExtra)))
            .Any(i => i.Calls(getter)), "Active estimate uses the same speed getter");
    }

    private static int completions;
    private static float podFactor = 1f;
    private static float pawnFactor = 1f;

    public static void RunNative(Action<bool, string> check)
    {
        ValidateEligibilityAndRates(check);
        using var scope = new Scope();
        var f = new Fixture();
        var target = BiosculpterCyclePatch.FindTarget()!;
        var tests = new Harmony(BiosculpterCyclePatch.OwnerId + ".tests");
        var tick = AccessTools.Method(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompTick));
        try
        {
            // Preserve the real calculation, processing/power branches and counters.
            // Isolate stat providers, visual work and completion side effects from
            // unavailable maps/definitions. This does not execute treatment effects.
            tests.Patch(target, transpiler: new HarmonyMethod(typeof(BiosculpterCycleChecks), nameof(StubStats)));
            tests.Patch(AccessTools.PropertyGetter(typeof(CompBiosculpterPod), "CleanlinessSpeedFactor"),
                transpiler: new HarmonyMethod(typeof(BiosculpterCycleChecks), nameof(StubStats)));
            tests.Patch(tick, transpiler: new HarmonyMethod(typeof(BiosculpterCycleChecks), nameof(IsolateTick)));
            tests.Patch(AccessTools.Method(typeof(CompBiosculpterPod), "CycleCompleted"),
                prefix: new HarmonyMethod(typeof(BiosculpterCycleChecks), nameof(CaptureCompletion)));
            BiosculpterCyclePatch.Install(f.Service);
            BiosculpterCyclePatch.Install(f.Service);
            check(f.Service.IsAvailable, "Install actual biosculpter Postfix: " + f.Service.UnavailableReason);
            check(Harmony.GetPatchInfo(target).Postfixes.Count(p => p.owner == BiosculpterCyclePatch.OwnerId) == 1,
                "Exactly one production speed Postfix");
            foreach (var cycle in Cycles)
            {
                foreach (var factors in new[] { (0.7f, 1f, 1f), (1.25f, 2f, 1.25f) })
                {
                    f.Start(cycle);
                    f.Service.TrySetFastCycles(true);
                    podFactor = factors.Item1;
                    pawnFactor = factors.Item2;
                    Set(f.Pod, "biotunedTo", factors.Item3 == 1f ? null! : f.Pawn);
                    completions = 0;
                    var ticks = 0;
                    while (f.Pod.Occupant != null && ticks++ < 2502) f.Pod.CompTick();
                    check(ticks == 2500 && completions == 1 && f.Work == 0f, "Native processing reaches completion once in one hour: " + cycle.Key);
                    f.Pod.CompTick();
                    check(completions == 1, "Native next tick cannot double-complete: " + cycle.Key);
                }
            }
            f.Start(Cycles[0]);
            podFactor = 0.9f;
            pawnFactor = 2f;
            Set(f.Pod, "biotunedTo", f.Pawn);
            var work = f.Work;
            f.SetPower(false);
            for (var i = 0; i < 100; i++) f.Pod.CompTick();
            check(f.Work == work && (int)AccessTools.Field(typeof(CompBiosculpterPod), "currentCyclePowerCutTicks").GetValue(f.Pod)! == 100,
                "Unpowered native ticks pause work and retain outage counter");
            f.SetPower(true);
            f.Pod.CompTick();
            check(f.Work == work - 144f && f.Power.PowerOutput == -200f,
                "Powered processing resumes without changing native power draw");
            check(f.Work / f.Pod.CycleSpeedFactor == 2499f, "Active remaining-time calculation uses the fixed rate");
            f.Service.TrySetFastCycles(false);
            var native = f.Pod.GetCycleSpeedFactorForPawn(f.Pawn);
            check(native == 0.9f * 1.25f * 2f, "Disable restores actual cleanliness/biotuning/occupant calculation");
            work = f.Work;
            f.Pod.CompTick();
            check(f.Work == work - native, "Disable preserves progress and immediately restores native ticking");
            f.Service.TrySetFastCycles(true);
            f.Base.SetGuest(GuestStatus.Prisoner);
            check(f.Pod.GetCycleSpeedFactorForPawn(f.Pawn) == native, "Actual prisoner inside player pod retains native result");
            work = f.Work;
            f.Pod.CompTick();
            check(f.Work == work - native, "Actual prisoner processing retains the native decrement");
            f.Pawn.guest = null!;
            CraftingChecks.SetFaction(f.Pawn, Empty<Faction>());
            check(f.Pod.GetCycleSpeedFactorForPawn(f.Pawn) == native, "Actual NPC occupant retains native result");
            CraftingChecks.SetFaction(f.Pawn, f.Player);
            check(f.Pod.GetCycleSpeedFactorForPawn(f.Pawn) == 144f, "Faction restoration is reevaluated dynamically");
            CraftingChecks.SetFaction(f.Parent, Empty<Faction>());
            check(f.Pod.GetCycleSpeedFactorForPawn(f.Pawn) == native, "Pod ownership transfer restores native rate");
            CraftingChecks.SetFaction(f.Parent, f.Player);
            check(f.Pod.GetCycleSpeedFactorForPawn(f.Pawn) == 144f, "Pod ownership restoration is dynamic");
            foreach (var type in Cycles.Select(c => c.Type).Append(typeof(CompBiosculpterPod)).Append(typeof(CompBiosculpterPod_HealingCycle)))
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    check(method == target || Harmony.GetPatchInfo(method)?.Owners.Contains(BiosculpterCyclePatch.OwnerId) != true,
                        "No production patch on native resources, effects, timers or serialization: " + method.Name);
            ValidatePersistence(f, check);
        }
        finally
        {
            tests.UnpatchAll(tests.Id);
            new Harmony(BiosculpterCyclePatch.OwnerId).UnpatchAll(BiosculpterCyclePatch.OwnerId);
        }
    }

    private static void ValidatePersistence(Fixture f, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-biosculpter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            var store = new CustomStackLimitSettingsStore(path);
            foreach (var enabled in new[] { true, false })
            {
                var settings = new CustomStackLimitSettings { FastBiosculpterCycles = enabled, DisableAbilityCooldowns = !enabled };
                store.Save(settings);
                check(CustomStackLimitSettingsStore.Read(path).Matches(settings), "Biosculpter and independent cooldown setting round-trip");
                Program.RunFreshProcess(path, enabled ? "--biosculpter-on" : "--biosculpter-off");
            }
            File.WriteAllText(path, "<SettingsBlock><ModSettings /></SettingsBlock>");
            check(!CustomStackLimitSettingsStore.Read(path).FastBiosculpterCycles, "Legacy settings load biosculpter disabled");

            // Native pod serialization, with an empty container to avoid pretending
            // that a minimal test pawn is a fully saveable gameplay pawn.
            f.Pod.GetDirectlyHeldThings().Remove(f.Pawn);
            f.Start(Cycles[1]);
            f.Pod.GetDirectlyHeldThings().Remove(f.Pawn);
            Set(f.Pod, "biotunedTo", null!);
            Set(f.Pod, "allowedNutritionSettings", Empty<StorageSettings>());
            f.Work = 712345.5f;
            var record = new PodRecord(f.Pod);
            path = Path.Combine(directory, "pod.xml");
            Scribe.saver.InitSaving(path, "BiosculpterTest");
            Scribe_Deep.Look(ref record, "pod");
            Scribe.saver.FinalizeSaving();
            record = null!;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref record, "pod");
            Scribe.loader.FinalizeLoading();
            check(record != null && (float)Remaining.GetValue(record.Pod)! == 712345.5f &&
                (string)CycleKey.GetValue(record.Pod)! == "bioregeneration", "Native Scribe retains cycle key and exact fractional remaining work");
            // Reattach the minimal fixture pawn after loading the pod record; the
            // pawn's own deep serialization is deliberately outside this fixture.
            record!.Pod.parent = f.Parent;
            record.Pod.props = f.Pod.props;
            Set(record.Pod, "cachedAvailableCycles", new List<CompBiosculpterPod_Cycle> { f.Pod.CurrentCycle });
            check(record.Pod.GetDirectlyHeldThings().TryAdd(f.Pawn, false), "Reattach fixture occupant to loaded native container");
            f.Service.TrySetFastCycles(true);
            check(record.Pod.CycleSpeedFactor == 600f && (float)Remaining.GetValue(record.Pod)! == 712345.5f,
                "Loaded cycle queries use fast rate without rescaling saved work");
            record.Pod.CompTick();
            check((float)Remaining.GetValue(record.Pod)! == 711745.5f, "Loaded cycle resumes native processing from saved work");
            f.Service.TrySetFastCycles(false);
            check(record.Pod.CycleSpeedFactor == podFactor * pawnFactor && (float)Remaining.GetValue(record.Pod)! == 711745.5f,
                "Disable after load restores native rate without resetting progress");
        }
        finally
        {
            if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void VerifyRestart(string path, bool expected)
    {
        var settings = CustomStackLimitSettingsStore.Read(path);
        if (settings.FastBiosculpterCycles != expected || settings.DisableAbilityCooldowns == expected)
            throw new Exception("Biosculpter setting or cooldown independence did not survive a fresh process.");
        Console.WriteLine("Fresh-process biosculpter settings passed.");
    }

    private static int CountTicks(float remaining, float speed)
    {
        var ticks = 0;
        while (remaining > 0f && ticks < 2502) { remaining -= speed; ticks++; }
        return ticks;
    }

    private static IEnumerable<CodeInstruction> StubStats(IEnumerable<CodeInstruction> instructions)
    {
        var getStat = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue),
            new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
        foreach (var instruction in instructions)
            yield return instruction.Calls(getStat)
                ? new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = AccessTools.Method(typeof(BiosculpterCycleChecks), nameof(ReadStat)) }
                : instruction;
    }
    private static float ReadStat(Thing thing, StatDef stat, bool postProcess, int staleTicks) => thing is Pawn ? pawnFactor : podFactor;
    private static bool Installed(string feature) => true;
    private static float SkipVisuals(CompBiosculpterPod pod) => 0f;

    private static IEnumerable<CodeInstruction> IsolateTick(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.Select(i => new CodeInstruction(i)).ToList();
        var skipped = 0;
        for (var i = 0; i < il.Count; i++)
        {
            if (il[i].Calls(AccessTools.Method(typeof(ModLister), nameof(ModLister.CheckIdeology))))
                il[i].operand = AccessTools.Method(typeof(BiosculpterCycleChecks), nameof(Installed));
            if (il[i].LoadsField(Remaining) && i + 2 < il.Count && il[i + 1].opcode == OpCodes.Ldc_R4 &&
                Equals(il[i + 1].operand, 0f) && (il[i + 2].opcode == OpCodes.Ble_Un || il[i + 2].opcode == OpCodes.Ble_Un_S))
            {
                il[i].opcode = OpCodes.Call;
                il[i].operand = AccessTools.Method(typeof(BiosculpterCycleChecks), nameof(SkipVisuals));
                skipped++;
            }
        }
        if (skipped != 1) throw new Exception("Expected one native post-processing visual gate.");
        return il;
    }
    private static bool CaptureCompletion(CompBiosculpterPod __instance)
    {
        completions++;
        __instance.GetDirectlyHeldThings().Remove(__instance.Occupant);
        CycleKey.SetValue(__instance, null);
        Remaining.SetValue(__instance, 0f);
        return false;
    }

    internal sealed class Fixture
    {
        public readonly ColonistNeedsChecks.Fixture Base = new();
        public ColonistNeedsChecks.TestPawn Pawn => Base.Pawn;
        public Faction Player => Base.Player;
        public CustomStackLimitSettings Settings => Base.Settings;
        public readonly ThingWithComps Parent = Empty<ThingWithComps>();
        public readonly CompBiosculpterPod Pod = new();
        public readonly CompPowerTrader Power = new();
        public readonly Map Map = Empty<Map>();
        public readonly BiosculpterCycleService Service;
        public bool Ideology = true;
        public bool FailWrites;
        public bool EnabledDuringWrite;
        public CustomStackLimitSettings? Durable;
        public float Work { get => (float)Remaining.GetValue(Pod)!; set => Remaining.SetValue(Pod, value); }

        public Fixture()
        {
            Map.info = new MapInfo { parent = Empty<MapParent>() };
            Map.designationManager = Empty<DesignationManager>();
            Set(Map.designationManager, "thingDesignations", new Dictionary<Thing, List<Designation>>());
            Set(Map.info.parent, "factionInt", Player);
            Set(Current.Game, "maps", new List<Map> { Map });
            Parent.def = new ThingDef { building = new BuildingProperties() };
            CraftingChecks.SetFaction(Parent, Player);
            Set(Parent, "mapIndexOrState", (sbyte)0);
            Pod.parent = Parent;
            Pod.props = new CompProperties_BiosculpterPod { biotunedCycleSpeedFactor = 1.25f };
            Power.parent = Parent;
            Power.props = new CompProperties_Power { idlePowerDraw = 50f };
            Set(Power.props, "basePowerConsumption", 200f);
            Set(Parent, "comps", new List<ThingComp> { Pod, Power });
            SetPower(true);
            Pawn.mindState = Empty<Pawn_MindState>();
            Pawn.mindState.mentalStateHandler = new MentalStateHandler(Pawn);
            Pawn.ForceSuspended = true;
            Pawn.stackCount = 1;
            Set(Pawn, "mapIndexOrState", (sbyte)-1);
            Service = new BiosculpterCycleService(Settings, candidate =>
            {
                if (FailWrites) throw new IOException("Simulated biosculpter write failure");
                EnabledDuringWrite = Settings.FastBiosculpterCycles;
                Durable = candidate.Copy();
            }, () => Player, () => Ideology);
            Start(Cycles[0]);
        }
        public void SetPower(bool enabled) => Set(Power, "powerOnInt", enabled);
        public void Start((Type Type, string Key, float Days) description)
        {
            var cycle = (CompBiosculpterPod_Cycle)RuntimeHelpers.GetUninitializedObject(description.Type);
            cycle.parent = Parent;
            cycle.props = new CompProperties_BiosculpterPod_HealingCycle { key = description.Key, durationDays = description.Days };
            Set(Pod, "cachedAvailableCycles", new List<CompBiosculpterPod_Cycle> { cycle });
            CycleKey.SetValue(Pod, description.Key);
            if (!Pod.GetDirectlyHeldThings().Contains(Pawn) && !Pod.GetDirectlyHeldThings().TryAdd(Pawn, false))
                throw new Exception("Could not contain test pawn.");
            Work = description.Days * GenDate.TicksPerDay;
            Set(Pod, "currentCyclePowerCutTicks", 0);
        }
    }

    private sealed class PodRecord : IExposable
    {
        public readonly CompBiosculpterPod Pod;
        public PodRecord() : this(new CompBiosculpterPod())
        {
            Pod.parent = Empty<ThingWithComps>();
            Pod.parent.def = new ThingDef { building = new BuildingProperties() };
        }
        public PodRecord(CompBiosculpterPod pod) => Pod = pod;
        public void ExposeData() => Pod.PostExposeData();
    }
    private sealed class CustomCycle : CompBiosculpterPod_Cycle { public override void CycleCompleted(Pawn occupant) { } }
    private sealed class TestMentalState : MentalState { }

    internal sealed class Scope : IDisposable
    {
        private readonly Game previous = Current.Game;
        private readonly FleshTypeDef normal;
        private readonly FleshTypeDef mech;
        public Scope()
        {
            var binding = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
            var old = binding.GetValue(null);
            try
            {
                binding.SetValue(null, true);
                RuntimeHelpers.RunClassConstructor(typeof(FleshTypeDefOf).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(StatDefOf).TypeHandle);
            }
            finally { binding.SetValue(null, old); }
            normal = FleshTypeDefOf.Normal;
            mech = FleshTypeDefOf.Mechanoid;
            FleshTypeDefOf.Normal = Empty<FleshTypeDef>();
            FleshTypeDefOf.Mechanoid = Empty<FleshTypeDef>();
            Current.Game = Empty<Game>();
            Current.Game.tickManager = Empty<TickManager>();
            Current.Game.questManager = new QuestManager();
        }
        public void Dispose()
        {
            Current.Game = previous;
            FleshTypeDefOf.Normal = normal;
            FleshTypeDefOf.Mechanoid = mech;
        }
    }
    private static T Empty<T>() where T : class => CraftingChecks.Empty<T>();
    private static void Set(object instance, string field, object content) => AccessTools.Field(instance.GetType(), field).SetValue(instance, content);
}
