using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using RimWorldUtilities;
using RimWorldUtilities.Services;
using Verse;

internal static class Program
{
    private static int checks;

    private static void Main(string[] args)
    {
        var gameDirectory = Environment.GetEnvironmentVariable("RIMWORLD_GAME_DIR");
        if (!string.IsNullOrEmpty(gameDirectory))
        {
            // Scribe's logging dependency is not among the four compile-only reference DLLs.
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var path = Path.Combine(gameDirectory, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            Prefs.UseDefaultsWithoutFile(); // Scribe's profiler reads preferences; no user files are loaded.
        }

        if (args.Length == 2 && args[0] == "--restart")
        {
            VerifyRestart(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--reset-restart")
        {
            VerifyResetRestart(args[1]);
            return;
        }
        if (args.Length == 2 && (args[0] == "--crafting-on" || args[0] == "--crafting-off" || args[0] == "--crafting-normal"))
        {
            VerifyCraftingRestart(args[1], args[0] == "--crafting-on",
                args[0] == "--crafting-on" ? 20 : args[0] == "--crafting-off" ? 3 : 1);
            return;
        }

        ValidateAmounts();
        ValidateEnabling();
        ValidateFailures();
        ValidateMonotonicLimits();
        ValidateMultipleDefinitions();
        ValidateEligibility();
        ValidateResets();
        ValidateResetFailures();
        ItemPropertiesChecks.RunCore(Check);
        CraftingChecks.RunCore(Check);
        CraftingSpeedChecks.RunCore(Check);
        if (!string.IsNullOrEmpty(gameDirectory))
        {
            ItemPropertiesChecks.RunNative(Check);
            CraftingChecks.RunNative(Check);
            CraftingSpeedChecks.RunNative(Check);
            ValidatePersistence();
            ValidateSteelMigration();
            ValidateResetPersistence();
            ValidateCraftingPersistence();
        }
        else
        {
            Console.WriteLine("Native property/Scribe/restart checks skipped: set RIMWORLD_GAME_DIR to the installed game directory.");
        }

        Console.WriteLine($"Passed {checks} assertions.");
    }

    private static void ValidateAmounts()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var cases = new (string Text, int Limit, bool Valid, int Amount)[]
        {
            ("1", 75, true, 1), ("75", 75, true, 75), ("42", 75, true, 42),
            (" 12 ", 75, true, 12), ("+2", 75, true, 2), ("0002", 75, true, 2),
            ("", 75, false, 0), (" ", 75, false, 0), ("0", 75, false, 0),
            ("-1", 75, false, 0), ("76", 75, false, 0), ("1.5", 75, false, 0),
            ("1,5", 75, false, 0), ("1e1", 75, false, 0), ("abc", 75, false, 0),
            ("2147483648", int.MaxValue, false, 0), ("999999999999999999999", 75, false, 0),
            ("1", 1, false, 0), ("1", 0, false, 0), ("1", -1, false, 0),
            ("50000", 75, false, 0), ("50000", 100000, true, 50000),
            ("100000", 100000, true, 100000), ("100001", 100000, false, 0)
        };
        foreach (var test in cases)
        {
            var valid = ItemEditorService.TryParseStackCount(test.Text, test.Limit, out var amount, out var error);
            Check(valid == test.Valid, $"Validity of '{test.Text}' with limit {test.Limit}");
            Check(!valid || amount == test.Amount, "Parsed count");
            Check(valid == (error.Length == 0), "Validation message");
        }
    }

    private static void ValidateEnabling()
    {
        var steel = Definition("Steel", 83); // Deliberately not vanilla's usual value.
        var wood = Definition("WoodLog", 75);
        var settings = new CustomStackLimitSettings();
        var writes = 0;
        var refreshes = 0;
        var service = new CustomStackLimitService(settings, candidate =>
        {
            Check(steel.stackLimit == 83, "Persist before raising the definition");
            Check(candidate.EnabledLimits["Steel"] == 100000, "Persist the enabled limit");
            Check(candidate.OriginalLimits["Steel"] == 83, "Persist the resolved original");
            writes++;
        }, definition =>
        {
            Check(ReferenceEquals(definition, steel), "Refresh only the enabled definition");
            refreshes++;
        });
        service.Initialize(new[] { steel, wood });
        Check(steel.stackLimit == 83 && !service.IsEnabled(steel), "Opt-in default");
        Check(service.GetOriginalLimit(steel) == 83, "Read original limit from the definition");
        Check(service.IsSupported(wood) && !service.IsEnabled(wood), "Eligible types remain opt-in");
        Check(service.TryEnable(steel, out _), "Enable Steel");
        Check(steel.stackLimit == 100000 && wood.stackLimit == 75, "Change only Steel's limit");
        Check(writes == 1 && refreshes == 1, "One durable write and refresh");
        Check(service.TryPrepareForStackEdit(steel, out _), "Allow a persisted limit");
        Check(writes == 1, "No per-edit writes when nothing changed");
        service.Initialize(new[] { steel });
        Check(service.GetOriginalLimit(steel) == 83, "Initialization does not overwrite original metadata");
    }

    private static void ValidateFailures()
    {
        var steel = Definition("Steel", 75);
        var settings = new CustomStackLimitSettings();
        var refreshes = 0;
        var service = new CustomStackLimitService(settings,
            _ => throw new IOException("Simulated write failure"), _ => refreshes++);
        Check(!service.TryPrepareForStackEdit(steel, out _), "Block edits before initialization");
        service.Initialize(new[] { steel });
        Check(!service.TryEnable(steel, out var error), "Failed persistence blocks enabling");
        Check(error.Contains("Simulated write failure"), "Report persistence failures");
        Check(steel.stackLimit == 75 && !service.IsEnabled(steel), "No partial enable on failure");
        Check(settings.EnabledLimits.Count == 0 && refreshes == 0, "No failed-write side effects");

        steel.saveCompressible = true;
        Check(!service.TryEnable(steel, out _), "Reject unsafe compressed definitions");
    }

    private static void ValidateMonotonicLimits()
    {
        var steel = Definition("Steel", 200000);
        var settings = EnabledSettings(100000);
        var writes = 0;
        var failWrites = true;
        var service = new CustomStackLimitService(settings, candidate =>
        {
            if (failWrites)
            {
                throw new IOException("Simulated upgrade write failure");
            }

            Check(candidate.EnabledLimits["Steel"] == 200000, "Persist another mod's higher limit");
            writes++;
        }, _ => { });
        service.Initialize(new[] { steel });
        Check(steel.stackLimit == 200000, "Never lower another mod's higher limit");
        Check(service.PersistenceError.Length > 0, "Report failed startup limit upgrades");
        Check(service.GetOriginalLimit(steel) == 75, "Keep saved original metadata");
        Check(!service.TryPrepareForStackEdit(steel, out _), "Block edits until a higher limit is durable");
        Check(settings.EnabledLimits["Steel"] == 100000, "Keep old durable state on upgrade failure");
        failWrites = false;
        Check(service.TryPrepareForStackEdit(steel, out _), "Permit the durable higher limit");
        Check(writes == 1, "One successful upgrade write");
        steel.stackLimit = 75;
        Check(service.TryPrepareForStackEdit(steel, out _), "Restore the persisted floor before editing");
        Check(steel.stackLimit == 200000 && writes == 1, "Never lower a previously raised limit");
        steel.saveCompressible = true;
        Check(!service.TryPrepareForStackEdit(steel, out _), "Block edits if compression is enabled later");
        steel.saveCompressible = false;

        var wood = Definition("WoodLog", 75);
        settings.EnabledLimits["WoodLog"] = 500000;
        var restarted = new CustomStackLimitService(settings, _ => { }, _ => { });
        restarted.Initialize(new[] { steel, wood });
        Check(wood.stackLimit == 500000 && restarted.IsEnabled(wood), "Restore saved limits for additional keys");

        var higherSteel = Definition("Steel", 300000);
        var higherSettings = EnabledSettings(100000);
        var startupWrites = 0;
        var higherService = new CustomStackLimitService(higherSettings, candidate =>
        {
            Check(candidate.EnabledLimits["Steel"] == 300000, "Persist the observed startup floor");
            startupWrites++;
        }, _ => { });
        higherService.Initialize(new[] { higherSteel });
        Check(startupWrites == 1 && higherSettings.EnabledLimits["Steel"] == 300000,
            "Preserve a higher startup limit even without editing a stack");
    }

    private static void ValidateMultipleDefinitions()
    {
        var steel = Definition("Steel", 75);
        var wood = Definition("WoodLog", 60);
        var medicine = Definition("MedicineIndustrial", 25);
        var untouched = Definition("Silver", 500);
        var settings = new CustomStackLimitSettings();
        var writes = 0;
        var service = new CustomStackLimitService(settings, candidate =>
        {
            Check(candidate.EnabledLimits.Count == writes + 1, "Each enable adds exactly one key");
            writes++;
        }, _ => { });
        service.Initialize(new[] { steel, wood, medicine, untouched });
        Check(settings.OriginalLimits.Count == 0, "Do not persist metadata for unrelated definitions");
        Check(service.TryEnable(steel, out _), "Enable the first type");
        Check(wood.stackLimit == 60 && medicine.stackLimit == 25, "Other types are unchanged");
        Check(service.TryEnable(wood, out _), "Enable an arbitrary eligible type");
        Check(service.TryEnable(medicine, out _), "Enable a third type");
        Check(writes == 3 && settings.EnabledLimits.Count == 3, "Persist independent entries");
        Check(settings.OriginalLimits["WoodLog"] == 60 && settings.OriginalLimits["MedicineIndustrial"] == 25,
            "Keep per-definition resolved baselines");
        Check(untouched.stackLimit == 500 && !settings.EnabledLimits.ContainsKey("Silver"), "Unenabled types untouched");
        Check(service.TryEnable(wood, out _) && writes == 3, "Re-enabling never resets limits or rewrites settings");

        var restoredSteel = Definition("Steel", 75);
        var restoredWood = Definition("WoodLog", 60);
        var restoredMedicine = Definition("MedicineIndustrial", 25);
        var restored = new CustomStackLimitService(settings.Copy(), _ => throw new Exception("Unexpected rewrite"), _ => { });
        restored.Initialize(new[] { restoredSteel, restoredWood, restoredMedicine });
        Check(restoredSteel.stackLimit == 100000 && restoredWood.stackLimit == 100000 &&
            restoredMedicine.stackLimit == 100000, "Restore all enabled types before spawning");
        Check(restored.GetOriginalLimit(restoredWood) == 60 && restored.GetOriginalLimit(restoredMedicine) == 25,
            "Restoration does not replace original metadata with effective limits");

        settings.EnabledLimits["MissingModItem"] = 200000;
        settings.OriginalLimits["MissingModItem"] = 20;
        var missing = new CustomStackLimitService(settings, _ => { }, _ => { });
        missing.Initialize(new[] { steel, wood, medicine });
        Check(settings.EnabledLimits["MissingModItem"] == 200000 && settings.OriginalLimits["MissingModItem"] == 20,
            "Retain unresolved definitions for future restarts");
    }

    private static void ValidateEligibility()
    {
        var definitions = new[]
        {
            Definition("Single", 1), Definition("Compressed", 75), Definition("Building", 75),
            Definition("Pawn", 75), Definition("Corpse", 75), Definition("Minified", 75),
            Definition("WrongClass", 75), Definition("OtherCategory", 75)
        };
        definitions[1].saveCompressible = true;
        definitions[2].thingClass = typeof(Building);
        definitions[3].thingClass = typeof(Pawn);
        definitions[4].thingClass = typeof(Corpse);
        definitions[5].thingClass = typeof(RimWorld.MinifiedThing);
        definitions[6].thingClass = typeof(string);
        definitions[7].category = ThingCategory.Plant;
        var service = new CustomStackLimitService(new CustomStackLimitSettings(),
            _ => throw new Exception("Ineligible definitions must not write settings"), _ => { });
        service.Initialize(definitions);
        foreach (var definition in definitions)
        {
            var previous = definition.stackLimit;
            Check(!service.IsSupported(definition) && !service.TryEnable(definition, out _),
                "Reject ineligible definition " + definition.defName);
            Check(definition.stackLimit == previous, "Ineligible limit unchanged");
        }

        var steel = Definition("Steel", 75);
        var wood = Definition("WoodLog", 75);
        var perType = new CustomStackLimitService(new CustomStackLimitSettings(), candidate =>
        {
            if (candidate.EnabledLimits.ContainsKey("Steel")) throw new IOException("Steel settings failure");
        }, _ => { });
        perType.Initialize(new[] { steel, wood });
        Check(!perType.TryEnable(steel, out _), "Keep the failed type disabled");
        Check(perType.TryEnable(wood, out _), "Other eligible types can still enable");
        Check(perType.GetPersistenceError(steel).Length > 0 && perType.GetPersistenceError(wood).Length == 0,
            "One type's success does not hide another type's persistence error");
    }

    private static void ValidateResets()
    {
        var steel = Definition("Steel", 83);
        var wood = Definition("WoodLog", 60);
        var settings = new CustomStackLimitSettings();
        var writes = 0;
        var refreshes = 0;
        var service = new CustomStackLimitService(settings, _ => writes++, _ => refreshes++);
        service.Initialize(new[] { steel, wood });
        Check(!service.TryScheduleReset(steel, out _), "Cannot reset a disabled type");
        Check(service.TryEnable(steel, out _) && service.TryEnable(wood, out _), "Enable two reset fixtures");
        var item = new Thing { def = steel, stackCount = 50000 };
        var previousRefreshes = refreshes;
        Check(service.TryScheduleReset(steel, out _), "Schedule reset");
        Check(service.IsResetPending(steel) && service.IsEnabled(steel), "Reset remains active until restart");
        Check(settings.PendingResets.SetEquals(new[] { "Steel" }) && !service.IsResetPending(wood),
            "Persist only the chosen type's reset");
        Check(steel.stackLimit == 100000 && item.stackCount == 50000 && refreshes == previousRefreshes,
            "Scheduling changes no active limit, count, or map state");
        service.Initialize(new[] { steel, wood });
        Check(steel.stackLimit == 100000 && service.IsResetPending(steel), "Repeat initialization is not a restart");
        Check(service.TryPrepareForStackEdit(steel, out _), "Editing remains allowed before restart");
        Check(settings.PendingResets.Contains("Steel"), "Preparing an edit must not cancel reset");
        steel.stackLimit = 250000;
        Check(service.TryPrepareForStackEdit(steel, out _) && settings.EnabledLimits["Steel"] == 250000,
            "Preserve higher active limits while a reset is pending");
        Check(settings.PendingResets.Contains("Steel"), "Higher-limit persistence preserves reset marker");
        Check(service.TryCancelReset(steel, out _), "Cancel pending reset");
        Check(!service.IsResetPending(steel) && service.IsEnabled(steel) && steel.stackLimit == 250000,
            "Cancellation preserves the active higher limit");
        Check(!service.TryCancelReset(steel, out _), "No second cancellation without a pending reset");
        Check(service.TryScheduleReset(steel, out _), "Reschedule reset");
        var beforeRestart = settings.Copy();
        var isolated = settings.Copy();
        isolated.PendingResets.Clear();
        Check(!settings.Matches(isolated) && settings.PendingResets.Contains("Steel"),
            "Copies and verification include independent reset state");

        var restartedSteel = Definition("Steel", 75);
        var restartedWood = Definition("WoodLog", 60);
        var restarted = new CustomStackLimitService(settings, candidate =>
        {
            Check(restartedSteel.stackLimit == 75 && !candidate.EnabledLimits.ContainsKey("Steel"),
                "Startup removes only our override without changing the resolved definition");
            writes++;
        }, _ => throw new Exception("Reset must not notify a runtime limit change"));
        restarted.Initialize(new[] { restartedSteel, restartedWood });
        Check(restartedSteel.stackLimit == 75 && !restarted.IsEnabled(restartedSteel) &&
            !restarted.IsResetPending(restartedSteel), "Fresh startup completes reset and clears restart status");
        Check(restarted.GetOriginalLimit(restartedSteel) == 83 && settings.OriginalLimits["Steel"] == 83,
            "Keep historical original metadata without assigning a stale value");
        Check(restartedWood.stackLimit == 100000 && restarted.IsEnabled(restartedWood),
            "Reset leaves other enabled types restored");
        Check(!settings.EnabledLimits.ContainsKey("Steel") && settings.PendingResets.Count == 0,
            "Persist reset completion");
        Check(!restarted.TryCancelReset(restartedSteel, out _), "Completed resets cannot be cancelled");
        Check(restarted.TryPrepareForStackEdit(restartedSteel, out _) && restartedSteel.stackLimit == 75,
            "Editing after restart does not resurrect the removed override");

        var higherSteel = Definition("Steel", 300000);
        var higher = new CustomStackLimitService(beforeRestart, _ => { }, _ => { });
        higher.Initialize(new[] { higherSteel });
        Check(higherSteel.stackLimit == 300000 && !higher.IsEnabled(higherSteel),
            "Reset respects another mod's higher resolved limit");
        Check(higher.TryEnable(higherSteel, out _) && higher.IsEnabled(higherSteel),
            "Explicit enabling works again after reset");
        Check(higherSteel.stackLimit == 300000 && beforeRestart.EnabledLimits["Steel"] == 300000,
            "Re-enabling never lowers a higher limit");
        Check(writes == 7, "Enable, schedule, upgrade, cancel, reschedule, and complete write once each");
    }

    private static void ValidateResetFailures()
    {
        var settings = EnabledSettings(100000);
        var steel = Definition("Steel", 75);
        var fail = true;
        var service = new CustomStackLimitService(settings, _ =>
        {
            if (fail) throw new IOException("Reset settings failure");
        }, _ => { });
        service.Initialize(new[] { steel });
        Check(!service.TryScheduleReset(steel, out var error) && error.Contains("Reset settings failure"),
            "Report scheduling write failure");
        Check(!service.IsResetPending(steel) && settings.PendingResets.Count == 0 && steel.stackLimit == 100000,
            "Failed scheduling changes neither durable intent nor active limit");
        fail = false;
        Check(service.TryScheduleReset(steel, out _), "Retry scheduling after write failure");
        fail = true;
        Check(!service.TryCancelReset(steel, out _), "Report cancellation write failure");
        Check(service.IsResetPending(steel) && settings.PendingResets.Contains("Steel"),
            "Failed cancellation leaves reset pending");

        // Simulate two successive starts with an unwritable settings file. The marker
        // must suppress restoration on both, even though cleanup could not be saved.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            steel = Definition("Steel", 75);
            service = new CustomStackLimitService(settings, _ =>
            {
                if (fail) throw new IOException("Reset cleanup failure");
            }, _ => { });
            service.Initialize(new[] { steel });
            Check(steel.stackLimit == 75 && !service.IsEnabled(steel) && !service.IsResetPending(steel),
                "Saved reset applies despite failed cleanup");
            Check(settings.PendingResets.Contains("Steel") && service.PersistenceError.Length > 0,
                "Retain durable reset marker and report cleanup failure");
            Check(service.TryPrepareForStackEdit(steel, out _) && steel.stackLimit == 75,
                "An edit cannot reapply a suppressed override");
        }
        Check(!service.TryEnable(steel, out _) && steel.stackLimit == 75,
            "Re-enabling after failed cleanup requires successful persistence");
        fail = false;
        Check(service.TryEnable(steel, out _) && service.IsEnabled(steel) && !service.IsResetPending(steel),
            "Explicit enabling clears a previously applied reset");
        Check(steel.stackLimit == 100000 && settings.PendingResets.Count == 0, "Persist re-enable before raising");
    }

    private static void ValidatePersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            var store = new CustomStackLimitSettingsStore(path);
            var steel = Definition("Steel", 75);
            var service = new CustomStackLimitService(new CustomStackLimitSettings(), candidate =>
            {
                try
                {
                    store.Save(candidate);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    throw;
                }
            }, _ => { });
            service.Initialize(new[] { steel });
            Check(service.TryEnable(steel, out var error), "Actual Scribe persistence: " + error);
            Check(File.Exists(path), "Settings file exists before enabling completes");
            var saved = File.ReadAllBytes(path);
            Check(CustomStackLimitSettingsStore.Read(path).Matches(EnabledSettings(100000)), "Scribe round trip");

            Scribe.mode = LoadSaveMode.Saving;
            try
            {
                var blocked = false;
                try
                {
                    store.Save(EnabledSettings(200000));
                }
                catch (InvalidOperationException)
                {
                    blocked = true;
                }

                Check(blocked, "Do not interfere with an active game save/load");
                Check(Scribe.mode == LoadSaveMode.Saving, "Do not force-stop an unrelated save/load");
            }
            finally
            {
                Scribe.ForceStop();
            }

            Check(saved.SequenceEqual(File.ReadAllBytes(path)), "Failed writes preserve the existing settings");
            Check(Directory.GetFiles(directory).Length == 1, "No leftover temporary settings");

            RunFreshProcess(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidateSteelMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            // Exact dictionary tags used by the Steel-only release. No schema rewrite or renaming.
            File.WriteAllText(path, """
                <SettingsBlock><ModSettings>
                  <enabledStackLimits><keys><li>Steel</li></keys><values><li>250000</li></values></enabledStackLimits>
                  <originalStackLimits><keys><li>Steel</li></keys><values><li>83</li></values></originalStackLimits>
                </ModSettings></SettingsBlock>
                """);
            var originalFile = File.ReadAllBytes(path);
            var settings = CustomStackLimitSettingsStore.Read(path);
            Check(!settings.AlwaysLegendaryCrafting, "Legacy Steel settings default crafting to disabled");
            Check(settings.CraftingSpeedMultiplier == 1, "Legacy settings default to normal crafting speed");
            var store = new CustomStackLimitSettingsStore(path);
            var steel = Definition("Steel", 75);
            var wood = Definition("WoodLog", 60);
            var service = new CustomStackLimitService(settings, store.Save, _ => { });
            service.Initialize(new[] { steel, wood });
            Check(steel.stackLimit == 250000 && service.IsEnabled(steel), "Migrate existing Steel without lowering its limit");
            Check(service.GetOriginalLimit(steel) == 83, "Keep legacy Steel baseline metadata");
            Check(originalFile.SequenceEqual(File.ReadAllBytes(path)), "Legacy restoration requires no file rewrite");
            Check(wood.stackLimit == 60 && !service.IsEnabled(wood), "Migration does not enable unrelated types");
            Check(service.TryEnable(wood, out _), "Add another type to migrated settings");
            var roundTrip = CustomStackLimitSettingsStore.Read(path);
            Check(roundTrip.EnabledLimits["Steel"] == 250000 && roundTrip.EnabledLimits["WoodLog"] == 100000,
                "Native Scribe persists multiple type keys");
            Check(roundTrip.OriginalLimits["Steel"] == 83 && roundTrip.OriginalLimits["WoodLog"] == 60,
                "Native Scribe preserves both original limits");
            RunFreshProcess(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidateResetPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            var store = new CustomStackLimitSettingsStore(path);
            var settings = EnabledSettings(250000);
            settings.OriginalLimits["Steel"] = 83;
            settings.EnabledLimits["WoodLog"] = 100000;
            settings.OriginalLimits["WoodLog"] = 60;
            store.Save(settings);
            var steel = Definition("Steel", 75);
            var service = new CustomStackLimitService(settings, store.Save, _ => { });
            service.Initialize(new[] { steel, Definition("WoodLog", 60) });
            Check(service.TryScheduleReset(steel, out _), "Persist reset with native Scribe");
            Check(CustomStackLimitSettingsStore.Read(path).Matches(settings), "Pending reset Scribe round trip");
            Check(service.TryCancelReset(steel, out _), "Persist cancellation with native Scribe");
            Check(CustomStackLimitSettingsStore.Read(path).PendingResets.Count == 0, "Cancellation is durable");
            RunFreshProcess(path); // A cancelled reset must retain the old 250,000 floor.
            Check(service.TryScheduleReset(steel, out _), "Persist reset again");
            RunFreshProcess(path, "--reset-restart");
            var completed = CustomStackLimitSettingsStore.Read(path);
            Check(!completed.EnabledLimits.ContainsKey("Steel") && completed.PendingResets.Count == 0,
                "Fresh process persisted reset completion");
            RunFreshProcess(path, "--reset-restart"); // It must stay off on subsequent starts.
            Check(Directory.GetFiles(directory).Length == 1, "Reset writes leave no temporary settings");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidateCraftingPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RimWorldUtilities-crafting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.xml");
            var store = new CustomStackLimitSettingsStore(path);
            var settings = EnabledSettings(250000);
            var steel = Definition("Steel", 75);
            var wood = Definition("WoodLog", 60);
            store.Save(settings);
            var stacks = new CustomStackLimitService(settings, store.Save, _ => { });
            stacks.Initialize(new[] { steel, wood });
            var crafting = new CraftingService(settings, store.Save);
            crafting.SetAvailability(true);
            crafting.SetSpeedAvailability(true);
            Check(crafting.TrySetAlwaysLegendary(true), "Persist crafting enable using native Scribe");
            Check(crafting.TrySetSpeedMultiplier(20), "Persist speed independently using native Scribe");
            Check(CustomStackLimitSettingsStore.Read(path).Matches(settings), "Verify crafting and stack settings together");
            RunFreshProcess(path, "--crafting-on");
            Check(stacks.TryEnable(wood, out _), "Write stack settings after enabling crafting");
            Check(CustomStackLimitSettingsStore.Read(path).AlwaysLegendaryCrafting, "Stack writes preserve crafting enable");
            Check(CustomStackLimitSettingsStore.Read(path).CraftingSpeedMultiplier == 20, "Stack writes preserve speed");
            Check(crafting.TrySetAlwaysLegendary(false), "Persist crafting disable using native Scribe");
            Check(crafting.SpeedMultiplier == 20, "Disabling Legendary preserves speed");
            Check(crafting.TrySetSpeedMultiplier(3), "Persist speed while Legendary is disabled");
            Check(CustomStackLimitSettingsStore.Read(path).EnabledLimits["WoodLog"] == 100000,
                "Crafting writes preserve later stack changes");
            RunFreshProcess(path, "--crafting-off");
            Check(crafting.TrySetSpeedMultiplier(1), "Persist normal speed");
            RunFreshProcess(path, "--crafting-normal");
            Check(Directory.GetFiles(directory).Length == 1, "Crafting persistence leaves no temporary settings");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void VerifyCraftingRestart(string path, bool enabled, int expectedSpeed)
    {
        var settings = CustomStackLimitSettingsStore.Read(path);
        var crafting = new CraftingService(settings, _ => throw new Exception("Reading settings must not save"));
        Check(crafting.AlwaysLegendary == enabled, "Restore crafting setting in a fresh process");
        Check(!crafting.IsAvailable, "Saved enable alone cannot bypass patch availability");
        Check(crafting.SpeedMultiplier == expectedSpeed && !crafting.IsSpeedAvailable,
            "Restore speed without bypassing its patch availability");
        crafting.SetAvailability(true);
        crafting.SetSpeedAvailability(true);
        Check(crafting.AlwaysLegendary == enabled && crafting.IsAvailable, "Patch availability preserves saved preference");
        Check(crafting.IsSpeedAvailable && crafting.SpeedMultiplier == expectedSpeed, "Speed preference survives a fresh process");
        Check(settings.EnabledLimits["Steel"] == 250000 && settings.OriginalLimits["Steel"] == 75,
            "Crafting restart retains raised Steel configuration");
        Console.WriteLine($"Fresh-process crafting settings passed ({checks} assertions).");
    }

    private static void RunFreshProcess(string path, string mode = "--restart")
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        Check(process.WaitForExit(30000) && process.ExitCode == 0, "Fresh-process restoration: " + errors);
        Console.Write(output);
    }

    private static void VerifyResetRestart(string path)
    {
        var settings = CustomStackLimitSettingsStore.Read(path);
        var steel = Definition("Steel", 75);
        var wood = Definition("WoodLog", 60);
        var store = new CustomStackLimitSettingsStore(path);
        var service = new CustomStackLimitService(settings, store.Save, _ => { });
        service.Initialize(new[] { steel, wood });
        Check(steel.stackLimit == 75 && !service.IsEnabled(steel), "Do not reapply reset type at startup");
        Check(!service.IsResetPending(steel), "Restart status clears in fresh process");
        Check(service.GetOriginalLimit(steel) == 83, "Keep legacy original metadata after reset");
        Check(wood.stackLimit == 100000 && service.IsEnabled(wood), "Other types survive a reset restart");
        Check(service.TryPrepareForStackEdit(steel, out _) && steel.stackLimit == 75,
            "Quantity editing cannot revive the removed limit");
        Check(!ItemEditorService.TryParseStackCount("50000", steel.stackLimit, out _, out _),
            "Post-restart quantity validation uses the restored effective limit");
        var durable = CustomStackLimitSettingsStore.Read(path);
        Check(durable.Matches(settings) && !durable.EnabledLimits.ContainsKey("Steel") && durable.PendingResets.Count == 0,
            "Reset completion survives another process restart");
        Console.WriteLine($"Fresh-process reset passed ({checks} assertions).");
    }

    private static void VerifyRestart(string path)
    {
        var steel = Definition("Steel", 75);
        var wood = Definition("WoodLog", 60);
        var settings = CustomStackLimitSettingsStore.Read(path);
        var service = new CustomStackLimitService(settings,
            _ => throw new Exception("Restoration should not need a write"), _ => { });
        service.Initialize(new[] { steel, wood });
        Check(steel.stackLimit == settings.EnabledLimits["Steel"] && service.IsEnabled(steel),
            "Restore Steel limit before any map objects exist");
        Check(service.GetOriginalLimit(steel) == settings.OriginalLimits["Steel"], "Restore original metadata");
        Check(service.TryPrepareForStackEdit(steel, out _), "Restored state is durable");
        if (settings.EnabledLimits.TryGetValue("WoodLog", out var woodLimit))
        {
            Check(wood.stackLimit == woodLimit && service.GetOriginalLimit(wood) == 60,
                "Restore the second definition independently");
            Check(service.TryPrepareForStackEdit(wood, out _), "Restored second definition is durable");
        }
        else
        {
            Check(wood.stackLimit == 60 && !service.IsEnabled(wood), "Keep other types disabled after restart");
        }
        Console.WriteLine($"Fresh-process settings restoration passed ({checks} assertions).");
    }

    private static CustomStackLimitSettings EnabledSettings(int limit) => new()
    {
        EnabledLimits = new() { ["Steel"] = limit },
        OriginalLimits = new() { ["Steel"] = 75 }
    };

    private static ThingDef Definition(string name, int limit)
    {
        // Isolated metadata fixtures: no global DefDatabase, game, map, or engine startup.
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = name;
        definition.category = ThingCategory.Item;
        definition.thingClass = typeof(Thing);
        definition.stackLimit = limit;
        return definition;
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
