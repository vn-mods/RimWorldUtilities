using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

internal static class CraftingSpeedChecks
{
    private const BindingFlags StaticPrivate = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo WorkLeft = typeof(JobDriver_DoBill).GetField(nameof(JobDriver_DoBill.workLeft))!;
    private static float unfinishedWork;
    private static int xpTicks;
    private static int completions;

    public static void RunCore(Action<bool, string> check)
    {
        var f = new CraftingChecks.Fixture();
        check(f.Service.SpeedMultiplier == 1 && f.Settings.CraftingSpeedMultiplier == 1, "Default speed is 1x");
        check(!f.Service.IsSpeedAvailable && !f.Service.TrySetSpeedMultiplier(20), "Unavailable speed cannot be enabled");
        f.Service.SetSpeedAvailability(true);
        check(CraftingService.SpeedMultipliers.SequenceEqual(new[] { 1, 2, 3, 5, 10, 20 }), "Exact speed options");
        foreach (var multiplier in CraftingService.SpeedMultipliers)
        {
            check(f.Service.TrySetSpeedMultiplier(multiplier), "Select speed " + multiplier);
            check(f.Service.ScaleWork(6.25f, f.Driver) == 6.25f * multiplier, "Scale only work at " + multiplier);
            check(!f.Service.AlwaysLegendary, "Speed does not enable Legendary");
        }
        foreach (var invalid in new[] { -1, 0, 4, 7, 21, int.MaxValue })
            check(!f.Service.TrySetSpeedMultiplier(invalid) && f.Service.SpeedMultiplier == 20, "Reject invalid speed " + invalid);
        f.FailWrites = true;
        check(!f.Service.TrySetSpeedMultiplier(2) && f.Service.SpeedMultiplier == 20, "Failed write keeps active speed");
        check(f.Service.PersistenceError.Length > 0, "Expose speed persistence errors");
        f.FailWrites = false;
        check(f.Service.TrySetSpeedMultiplier(1) && f.Service.PersistenceError.Length == 0, "Persist return to normal speed");
        foreach (var work in new[] { 0f, -0f, 1.2345f, float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity })
            check(BitConverter.SingleToInt32Bits(f.Service.ScaleWork(work, null!)) == BitConverter.SingleToInt32Bits(work),
                "1x returns native work without arithmetic or context lookup");

        f.Settings.CraftingSpeedMultiplier = 99;
        check(f.Service.SpeedMultiplier == 1 && f.Service.ScaleWork(5f, f.Driver) == 5f, "Invalid saved speed falls back to 1x");
        check(f.Service.TrySetSpeedMultiplier(1) && f.Settings.CraftingSpeedMultiplier == 1, "Explicit normal speed repairs invalid preference");

        var cases = new (string Name, Action<CraftingChecks.Fixture> Change, bool Eligible)[]
        {
            ("foreign faction", x => CraftingChecks.SetFaction(x.Worker, CraftingChecks.Empty<Faction>()), false),
            ("factionless worker", x => CraftingChecks.SetFaction(x.Worker, null), false),
            ("missing player faction", x => x.PlayerFaction = null, false),
            ("player prisoner", x => x.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", x => x.SetGuest(GuestStatus.Slave), true),
            ("worker without skills", x => x.Worker.skills = null!, true),
            ("medical", x => x.SetBill(CraftingChecks.Empty<Bill_Medical>()), false),
            ("surgery", x => CraftingChecks.SetSurgery(x.Recipe, true), false),
            ("autonomous", x => x.SetBill(CraftingChecks.Empty<Bill_Autonomous>()), false),
            ("unrelated target", x => x.Job.targetA = new Thing(), false),
            ("missing job", x => x.Worker.jobs.curJob = null!, false),
            ("missing driver", x => x.Worker.jobs.curDriver = null!, false),
            ("stale driver", x =>
            {
                var driver = CraftingChecks.Empty<JobDriver_DoBill>();
                driver.pawn = x.Worker;
                driver.job = x.Job;
                x.Worker.jobs.curDriver = driver;
            }, false),
            ("different pawn", x => x.Driver.pawn = CraftingChecks.Empty<Pawn>(), false),
            ("different job", x => x.Driver.job = CraftingChecks.Empty<Job>(), false),
            ("destroyed pawn", x => CraftingChecks.Destroy(x.Worker), false),
            ("destroyed table", x => CraftingChecks.Destroy(x.Table), false),
            ("no products", x => x.Recipe.products.Clear(), false),
            ("special products", x => { x.Recipe.products.Clear(); x.Recipe.specialProducts = new() { SpecialProductType.Butchery }; }, true),
            ("no quality component", x => x.Product.compQuality = null!, true),
            ("no work skill", x => x.Recipe.workSkill = null!, true)
        };
        foreach (var test in cases)
        {
            f = new CraftingChecks.Fixture();
            f.Service.SetSpeedAvailability(true);
            f.Service.TrySetSpeedMultiplier(20);
            test.Change(f);
            check(f.Service.IsEligibleProgress(f.Driver) == test.Eligible, "Speed eligibility: " + test.Name);
            check(f.Service.ScaleWork(3f, f.Driver) == (test.Eligible ? 60f : 3f), "Speed effect: " + test.Name);
        }

        f = new CraftingChecks.Fixture();
        f.Service.SetSpeedAvailability(true);
        f.Service.TrySetSpeedMultiplier(5);
        foreach (var skill in new[] { "Crafting", "Cooking", "Artistic", "Smithing", "Tailoring" })
        {
            f.Recipe.workSkill.defName = skill;
            check(f.Service.ScaleWork(2f, f.Driver) == 10f, "No work-skill whitelist: " + skill);
        }
        f.Service.SetAvailability(true);
        f.Service.TrySetAlwaysLegendary(true);
        f.Service.SetAvailability(false, "Legendary failed");
        check(f.Service.ScaleWork(2f, f.Driver) == 10f, "Speed survives a Legendary patch failure");
        f.Service.SetAvailability(true);
        f.Service.SetSpeedAvailability(false, "Speed failed");
        check(f.Service.ScaleWork(2f, f.Driver) == 2f && f.Quality() == QualityCategory.Legendary,
            "Speed failure leaves Legendary working");

        var copy = f.Settings.Copy();
        check(copy.Matches(f.Settings) && copy.CraftingSpeedMultiplier == 5, "Copy speed preference");
        copy.CraftingSpeedMultiplier = 10;
        check(!copy.Matches(f.Settings), "Persistence verification includes speed");
        f.Settings.CopyFrom(copy);
        check(f.Service.SpeedMultiplier == 10 && f.Service.AlwaysLegendary, "CopyFrom preserves independent crafting preferences");
        ValidatePatterns(check);
    }

    private static void ValidatePatterns(Action<bool, string> check)
    {
        var interval = typeof(CraftingSpeedChecks).GetMethod(nameof(IntervalShape), StaticPrivate)!;
        var factory = new List<CodeInstruction>
        {
            new(OpCodes.Ldftn, interval),
            new(OpCodes.Newobj, typeof(Action<int>).GetConstructor(new[] { typeof(object), typeof(IntPtr) })!),
            new(OpCodes.Stfld, typeof(Toil).GetField(nameof(Toil.tickIntervalAction))!)
        };
        check(CraftingSpeedPatch.TryResolveCallback(factory, out var found, out _) && Equals(found, interval),
            "Resolve interval assignment by field and delegate signature");
        check(!CraftingSpeedPatch.TryResolveCallback(Array.Empty<CodeInstruction>(), out _, out _), "Reject missing interval assignment");
        check(!CraftingSpeedPatch.TryResolveCallback(factory.Concat(factory).ToList(), out _, out _), "Reject ambiguous interval assignments");
        var wrong = factory.Select(i => new CodeInstruction(i)).ToList();
        wrong[0].operand = typeof(CraftingSpeedChecks).GetMethod(nameof(Probe), StaticPrivate);
        check(!CraftingSpeedPatch.TryResolveCallback(wrong, out _, out _), "Validate callback signature");

        var pattern = Pattern();
        var generator = new DynamicMethod("SpeedLabels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var start = generator.DefineLabel();
        var subtraction = generator.DefineLabel();
        pattern[0].labels.Add(start);
        pattern[7].labels.Add(subtraction);
        var begin = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        var end = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        pattern[7].blocks.Add(begin);
        pattern[7].blocks.Add(end);
        check(CraftingSpeedPatch.TryRewrite(pattern, out var rewritten, out _), "Match work progress once");
        check(rewritten.Count == pattern.Count + 2 && rewritten[7].opcode == OpCodes.Ldarg_0 &&
            Equals(rewritten[8].operand, CraftingSpeedPatch.ScaleMethod) && rewritten[9].opcode == OpCodes.Sub,
            "Scale only work before the original subtraction");
        check(rewritten[0].labels.SequenceEqual(new[] { start }) && rewritten[7].labels.SequenceEqual(new[] { subtraction }),
            "Preserve original receiver labels; move subtraction branch targets");
        check(rewritten[7].blocks.SequenceEqual(new[] { begin }) && rewritten[9].blocks.SequenceEqual(new[] { end }),
            "Preserve exception boundaries across inserted instructions");
        check(pattern[7].opcode == OpCodes.Sub && pattern[7].labels.Count == 1 && pattern[7].blocks.Count == 2,
            "Original instruction metadata remains unchanged");
        var localPattern = Pattern();
        var driverLocal = generator.DeclareLocal(typeof(JobDriver_DoBill));
        localPattern[0] = new CodeInstruction(OpCodes.Ldloc_S, driverLocal);
        check(CraftingSpeedPatch.TryRewrite(localPattern, out rewritten, out _) &&
            rewritten[7].opcode == OpCodes.Ldloc_S && Equals(rewritten[7].operand, driverLocal),
            "Reload the actual driver operand without fixed local indices");
        var changed = Pattern();
        changed[6].opcode = OpCodes.Add;
        foreach (var incompatible in new[] { new List<CodeInstruction>(), Pattern().Concat(Pattern()).ToList(), changed, rewritten })
        {
            check(!CraftingSpeedPatch.TryRewrite(incompatible, out var unchanged, out var error) && error.Length > 0,
                "Reject missing, duplicate, changed or already-scaled patterns");
            check(unchanged.SequenceEqual(incompatible), "No partial rewrite on incompatible IL");
        }
    }

    public static void RunNative(Action<bool, string> check)
    {
        var factoryMethod = typeof(Toils_Recipe).GetMethod(nameof(Toils_Recipe.DoRecipeWork))!;
        check(CraftingSpeedPatch.TryResolveCallback(PatchProcessor.GetOriginalInstructions(factoryMethod), out var target, out _),
            "Resolve the actual 1.7 callback without creating a Toil");
        check(CraftingSpeedPatch.TryRewrite(PatchProcessor.GetOriginalInstructions(target!), out var rewritten, out _),
            "Match the actual native work decrement");
        check(rewritten.Count(i => Equals(i.operand, CraftingSpeedPatch.ScaleMethod)) == 1, "Insert speed scaling exactly once");
        var f = new CraftingChecks.Fixture();
        f.Service.SetAvailability(true);
        f.Service.TrySetAlwaysLegendary(true);
        CraftingSpeedPatch.Install(f.Service);
        check(f.Service.IsSpeedAvailable, "Install speed patch: " + f.Service.SpeedUnavailableReason);
        CraftingSpeedPatch.Install(f.Service);
        check(Harmony.GetPatchInfo(target).Transpilers.Count(p => p.owner == CraftingSpeedPatch.OwnerId) == 1,
            "Register speed patch once");
        check(Harmony.GetPatchInfo(factoryMethod)?.Owners.Contains(CraftingSpeedPatch.OwnerId) != true,
            "Patch the interval method, not the factory or global driver tick");

        var probe = typeof(CraftingSpeedChecks).GetMethod(nameof(Probe), StaticPrivate)!;
        new Harmony(CraftingSpeedPatch.OwnerId + ".tests").Patch(probe,
            transpiler: new HarmonyMethod(typeof(CraftingSpeedPatch), "Transpiler"));
        foreach (var multiplier in CraftingService.SpeedMultipliers)
        {
            check(f.Service.TrySetSpeedMultiplier(multiplier), "Persist live probe speed");
            f.Driver.workLeft = 40f;
            f.Driver.ticksSpentDoingRecipeWork = 0;
            xpTicks = completions = 0;
            Probe(f.Driver, 4, 2f);
            var expected = 40f - 8f * multiplier;
            check(f.Driver.workLeft == expected && unfinishedWork == expected, "Emitted IL scales and syncs work at " + multiplier);
            check(xpTicks == 4 && f.Driver.ticksSpentDoingRecipeWork == 4, "Elapsed/XP ticks stay unchanged at " + multiplier);
            check(completions == (expected <= 0f ? 1 : 0), "Native completion branch observes scaled work once");
        }
        CraftingChecks.SetFaction(f.Worker, CraftingChecks.Empty<Faction>());
        f.Driver.workLeft = 40f;
        Probe(f.Driver, 4, 2f);
        check(f.Driver.workLeft == 32f, "Patched interval leaves NPC work unchanged");

        CraftingChecks.SetFaction(f.Worker, f.PlayerFaction);
        var transpiler = typeof(CraftingSpeedPatch).GetMethod("Transpiler", StaticPrivate)!;
        foreach (var incompatible in new[] { new List<CodeInstruction>(), Pattern().Concat(Pattern()).ToList() })
        {
            var untouched = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { incompatible })!;
            check(!f.Service.IsSpeedAvailable && untouched.SequenceEqual(incompatible), "Failed matching disables speed without rewriting");
            check(f.Service.IsAvailable && f.Service.AlwaysLegendary, "Failed speed pattern does not disable Legendary");
        }
        f.Driver.workLeft = 40f;
        Probe(f.Driver, 4, 2f);
        check(f.Driver.workLeft == 32f, "Unavailable state gates an existing emitted speed redirect");
    }

    private static List<CodeInstruction> Pattern() => new()
    {
        new(OpCodes.Ldarg_0), new(OpCodes.Dup), new(OpCodes.Ldfld, WorkLeft),
        new(OpCodes.Ldarg_2), new(OpCodes.Ldarg_1), new(OpCodes.Conv_R4),
        new(OpCodes.Mul), new(OpCodes.Sub), new(OpCodes.Stfld, WorkLeft)
    };

    private static void IntervalShape(int delta) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Probe(JobDriver_DoBill driver, int delta, float rate)
    {
        xpTicks += delta;
        driver.ticksSpentDoingRecipeWork += delta;
        driver.workLeft -= rate * (float)delta;
        unfinishedWork = driver.workLeft;
        if (driver.workLeft <= 0f) completions++;
    }
}
