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

        ValidateAmounts();
        ValidateEnabling();
        ValidateFailures();
        ValidateMonotonicLimits();
        if (!string.IsNullOrEmpty(gameDirectory))
        {
            ValidatePersistence();
        }
        else
        {
            Console.WriteLine("Native Scribe/restart checks skipped: set RIMWORLD_GAME_DIR to the installed game directory.");
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
        service.Initialize(name => name == "Steel" ? steel : wood);
        Check(steel.stackLimit == 83 && !service.IsEnabled(steel), "Opt-in default");
        Check(service.GetOriginalLimit(steel) == 83, "Read original limit from the definition");
        Check(!service.TryEnable(wood, out _), "Reject unsupported types");
        Check(service.TryEnable(steel, out _), "Enable Steel");
        Check(steel.stackLimit == 100000 && wood.stackLimit == 75, "Change only Steel's limit");
        Check(writes == 1 && refreshes == 1, "One durable write and refresh");
        Check(service.TryPrepareForStackEdit(steel, out _), "Allow a persisted limit");
        Check(writes == 1, "No per-edit writes when nothing changed");
        service.Initialize(_ => steel);
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
        service.Initialize(_ => steel);
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
        service.Initialize(_ => steel);
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
        restarted.Initialize(name => name == "Steel" ? steel : wood);
        Check(wood.stackLimit == 75 && !restarted.IsSupported(wood), "Ignore unsupported settings entries");

        var higherSteel = Definition("Steel", 300000);
        var higherSettings = EnabledSettings(100000);
        var startupWrites = 0;
        var higherService = new CustomStackLimitService(higherSettings, candidate =>
        {
            Check(candidate.EnabledLimits["Steel"] == 300000, "Persist the observed startup floor");
            startupWrites++;
        }, _ => { });
        higherService.Initialize(_ => higherSteel);
        Check(startupWrites == 1 && higherSettings.EnabledLimits["Steel"] == 300000,
            "Preserve a higher startup limit even without editing a stack");
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
            service.Initialize(_ => steel);
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

            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--restart");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            Check(process.WaitForExit(30000) && process.ExitCode == 0, "Fresh-process restoration: " + errors);
            Console.Write(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyRestart(string path)
    {
        var steel = Definition("Steel", 75);
        var settings = CustomStackLimitSettingsStore.Read(path);
        var service = new CustomStackLimitService(settings,
            _ => throw new Exception("Restoration should not need a write"), _ => { });
        service.Initialize(_ => steel);
        Check(steel.stackLimit == 100000 && service.IsEnabled(steel), "Restore limit before any map objects exist");
        Check(service.GetOriginalLimit(steel) == 75, "Restore original metadata");
        Check(service.TryPrepareForStackEdit(steel, out _), "Restored state is durable");
        Console.WriteLine("Fresh-process settings load and limit restoration passed.");
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
