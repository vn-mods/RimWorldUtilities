using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

internal static class BiosculpterAgeReversalChecks
{
    private static long T(decimal years) => (long)(years * GenDate.TicksPerYear);

    public static void RunCore(Action<bool, string> check)
    {
        foreach (var (original, expected) in new[] { (80, 40), (60, 30), (40, 20), (30, 15), (26, 13), (20, 13), (13, 13) })
            check(BiosculpterCycleService.RejuvenatedAge(T(original), Math.Max(T(13), T(original - 1)), T(13)) == T(expected),
                $"Halve original biological ticks: {original} -> {expected}");
        foreach (var c in new (long Original, long Native, long Minimum, long Expected)[]
        {
            (T(80.5m), T(79.5m), T(13), T(40.25m)),
            (T(40) + 1, T(39) + 1, T(13), T(20)),
            (T(25.5m), T(24.5m), T(13), T(13)),
            (T(12.25m), T(13), T(13), T(12.25m)),
            (0, T(13), T(13), 0),
            (T(20), T(19), 0, T(13)),
            (T(30), T(29), T(18), T(18)),
            (T(30), T(29), T(18.25m), T(18.25m)),
            (T(20), T(24), T(24), T(20)),
            (T(80), T(30), T(13), T(30)),
            (T(20), T(10), 0, T(10)),
            (long.MaxValue, long.MaxValue - 1, T(13), long.MaxValue / 2)
        })
            check(BiosculpterCycleService.RejuvenatedAge(c.Original, c.Native, c.Minimum) == c.Expected,
                "Fractional ticks, minimum, stronger vanilla result and overflow-safe halving: " + c.Original);
        ValidateRewrite(check);
    }

    private static void ValidateRewrite(Action<bool, string> check)
    {
        var target = BiosculpterAgeReversalPatch.FindTarget();
        check(target?.ReturnType == typeof(void) && target.DeclaringType == typeof(CompBiosculpterPod_AgeReversalCycle),
            "Resolve exact vanilla age-completion method");
        var original = PatchProcessor.GetOriginalInstructions(target!);
        check(BiosculpterAgeReversalPatch.TryRewrite(original, out var rewritten, out _), "Match actual native age completion IL");
        var setter = rewritten.FindIndex(i => i.Calls(BiosculpterAgeReversalPatch.AgeSetter));
        check(rewritten.Count == original.Count + 3 && rewritten[setter - 3].opcode == OpCodes.Ldarg_0 &&
            rewritten[setter - 2].opcode == OpCodes.Ldarg_1 && rewritten[setter - 1].Calls(BiosculpterAgeReversalPatch.AdjustTarget),
            "Adjust only setter value with live cycle and pawn context");
        var unchanged = rewritten.Where((_, index) => index < setter - 3 || index >= setter).ToList();
        check(original.Zip(unchanged).All(p => p.First.opcode == p.Second.opcode && Equals(p.First.operand, p.Second.operand)),
            "Preserve all native reduction, setter, reset and message instructions");
        check(rewritten.Count(i => i.Calls(BiosculpterAgeReversalPatch.AgeSetter)) == 1 &&
            rewritten.Count(i => i.Calls(BiosculpterAgeReversalPatch.ResetDemand)) == 1 &&
            rewritten.FindIndex(i => i.Calls(BiosculpterAgeReversalPatch.ResetDemand)) > setter,
            "One public setter precedes one native demand reset");
        foreach (var invalid in new[]
        {
            new List<CodeInstruction>(), original.Concat(original).ToList(), rewritten,
            original.Where(i => !i.Calls(BiosculpterAgeReversalPatch.ResetDemand)).ToList(),
            new List<CodeInstruction> { new(OpCodes.Call, BiosculpterAgeReversalPatch.ResetDemand), new(OpCodes.Call, BiosculpterAgeReversalPatch.AgeSetter) }
        })
            check(!BiosculpterAgeReversalPatch.TryRewrite(invalid, out var rejected, out var error) &&
                error.Length > 0 && rejected.SequenceEqual(invalid), "Reject missing, ambiguous, reordered or duplicate age patch without changing IL");

        var marked = original.Select(i => new CodeInstruction(i)).ToList();
        setter = marked.FindIndex(i => i.Calls(BiosculpterAgeReversalPatch.AgeSetter));
        var label = new DynamicMethod("AgeLabels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        marked[setter].labels.Add(label);
        var begin = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        var end = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        marked[setter].blocks.AddRange(new[] { begin, end });
        check(BiosculpterAgeReversalPatch.TryRewrite(marked, out rewritten, out _) &&
            rewritten[setter].labels.Contains(label) && rewritten[setter].blocks.Contains(begin) &&
            rewritten[setter + 3].blocks.Contains(end) && rewritten[setter + 3].labels.Count == 0 &&
            marked[setter].labels.Contains(label) && marked[setter].Calls(BiosculpterAgeReversalPatch.AgeSetter),
            "Preserve branch and exception metadata without mutating source IL");
    }

    private static int resets;
    private static int messages;
    private static int stageNotifications;
    private static int displayedDeadline;
    private static long ageAtReset;

    public static void RunNative(Action<bool, string> check)
    {
        using var scope = new BiosculpterCycleChecks.Scope();
        InitializeDefOf(typeof(PreceptDefOf));
        InitializeDefOf(typeof(MessageTypeDefOf));
        var oldDemand = PreceptDefOf.AgeReversal_Demanded;
        PreceptDefOf.AgeReversal_Demanded = Empty<PreceptDef>();
        var f = new BiosculpterCycleChecks.Fixture();
        var tests = new Harmony(BiosculpterAgeReversalPatch.OwnerId + ".tests");
        var target = BiosculpterAgeReversalPatch.FindTarget()!;
        try
        {
            // Real native age setter, growth/stage calculation, difficulty reduction
            // and demand reset execute. Only translations, UI messages and stage
            // callbacks that require a complete map/renderer are isolated.
            tests.Patch(target, transpiler: new HarmonyMethod(typeof(BiosculpterAgeReversalChecks), nameof(IsolateMessages)));
            tests.Patch(BiosculpterAgeReversalPatch.ResetDemand,
                prefix: new HarmonyMethod(typeof(BiosculpterAgeReversalChecks), nameof(ObserveReset)));
            tests.Patch(AccessTools.Method(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex"),
                transpiler: new HarmonyMethod(typeof(BiosculpterAgeReversalChecks), nameof(IsolateStageCallbacks)));
            f.Service.SetAvailability(true);
            BiosculpterAgeReversalPatch.Install(f.Service);
            BiosculpterAgeReversalPatch.Install(f.Service);
            check(f.Service.AgeReversalAvailable, "Install enhanced age reversal: " + f.Service.AgeReversalUnavailableReason);
            check(Harmony.GetPatchInfo(target).Transpilers.Count(p => p.owner == BiosculpterAgeReversalPatch.OwnerId) == 1,
                "Enhanced age reversal registers exactly once");
            foreach (var (original, expected) in new[] { (80, 40), (60, 30), (40, 20), (30, 15), (26, 13), (20, 13), (13, 13) })
            {
                Prepare(f, T(original), 13f, 1f);
                check(f.Pawn.ageTracker.AdultMinAge == 13f, "First Adult developmental stage supplies native minimum 13");
                Complete(f, T(expected), check, "native completion " + original);
            }
            foreach (var c in new (long Original, float Minimum, float Difficulty, long Expected)[]
            {
                (T(80.5m), 13f, 1f, T(40.25m)),
                (T(40) + 1, 13f, 1f, T(20)),
                (T(12.25m), 13f, 1f, T(12.25m)),
                (T(30), 18f, 1f, T(18)),
                (T(30), 18.25f, 1f, T(18.25m)),
                (T(20), 24f, 1f, T(20)),
                (T(80), 13f, 0f, T(40)),
                (T(80), 13f, 3f, T(40)),
                (T(80), 13f, 50f, T(30)),
                (T(20), 0f, 10f, T(10))
            })
            {
                Prepare(f, c.Original, c.Minimum, c.Difficulty);
                Complete(f, c.Expected, check, "minimum/fractional/difficulty " + c.Original);
            }
            ValidateExclusions(f, check);

            Prepare(f, T(80), 13f, 3f);
            f.Service.TrySetFastCycles(false);
            check(f.Pawn.ageTracker.AgeBiologicalTicks == T(80), "Disable during processing never changes age immediately");
            Complete(f, T(77), check, "disabled at completion follows native difficulty reduction");
            var finished = f.Pawn.ageTracker.AgeBiologicalTicks;
            f.Service.TrySetFastCycles(true);
            f.Service.TrySetFastCycles(false);
            check(f.Pawn.ageTracker.AgeBiologicalTicks == finished, "Toggling cannot undo completed rejuvenation");
            Prepare(f, T(80), 13f, 1f);
            f.Service.TrySetFastCycles(false);
            f.Service.TrySetFastCycles(true);
            check(f.Pawn.ageTracker.AgeBiologicalTicks == T(80), "Enable during processing waits for completion");
            Complete(f, T(40), check, "enabled at completion");
            check(Harmony.GetPatchInfo(BiosculpterAgeReversalPatch.AgeSetter)?.Owners.Contains(BiosculpterAgeReversalPatch.OwnerId) != true &&
                Harmony.GetPatchInfo(BiosculpterAgeReversalPatch.ResetDemand)?.Owners.Contains(BiosculpterAgeReversalPatch.OwnerId) != true,
                "No global age setter or scheduling patches");
            foreach (var type in new[] { typeof(CompBiosculpterPod_MedicCycle), typeof(CompBiosculpterPod_RegenerationCycle),
                typeof(CompBiosculpterPod_HealingCycle), typeof(CompBiosculpterPod_PleasureCycle), typeof(CompBiosculpterPod) })
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    check(Harmony.GetPatchInfo(method)?.Owners.Contains(BiosculpterAgeReversalPatch.OwnerId) != true,
                        "Age enhancement does not patch other effects, progress or common completion: " + method.Name);
            f.Service.SetAgeReversalAvailability(false, "Test failure");
            check(f.Service.SpeedFor(f.Pod, f.Pawn, 1f) == 192f,
                "Unavailable age enhancement does not disable existing one-hour speed");
        }
        finally
        {
            tests.UnpatchAll(tests.Id);
            new Harmony(BiosculpterAgeReversalPatch.OwnerId).UnpatchAll(BiosculpterAgeReversalPatch.OwnerId);
            PreceptDefOf.AgeReversal_Demanded = oldDemand;
        }
    }

    private static void ValidateExclusions(BiosculpterCycleChecks.Fixture f, Action<bool, string> check)
    {
        var cases = new (string Name, Action Change)[]
        {
            ("enemy", () => CraftingChecks.SetFaction(f.Pawn, Empty<Faction>())),
            ("neutral visitor", () => { CraftingChecks.SetFaction(f.Pawn, Empty<Faction>()); f.Base.SetGuest(GuestStatus.Guest); }),
            ("prisoner", () => f.Base.SetGuest(GuestStatus.Prisoner)),
            ("slave", () => f.Base.SetGuest(GuestStatus.Slave)),
            ("quest lodger", () => f.Base.MakeLodger()),
            ("NPC pod", () => CraftingChecks.SetFaction(f.Parent, Empty<Faction>())),
            ("NPC map", () => Set(f.Map.info.parent, "factionInt", Empty<Faction>())),
            ("missing containment", () => f.Pod.GetDirectlyHeldThings().Remove(f.Pawn)),
            ("missing parent", () => f.Pod.CurrentCycle.parent = null!),
            ("mental state", () => Set(f.Pawn.mindState.mentalStateHandler, "curStateInt", new TestMentalState { def = Empty<MentalStateDef>() })),
            ("DLC disabled", () => f.Ideology = false),
            ("enhancement unavailable", () => f.Service.SetAgeReversalAvailability(false)),
            ("speed feature unavailable", () => f.Service.SetAvailability(false))
        };
        foreach (var c in cases)
        {
            Prepare(f, T(80), 13f, 1f);
            var cycle = (CompBiosculpterPod_AgeReversalCycle)f.Pod.CurrentCycle;
            c.Change();
            Complete(f, T(79), check, "excluded " + c.Name, cycle);
        }
        Prepare(f, T(80), 13f, 1f);
        var otherCycle = new CompBiosculpterPod_AgeReversalCycle { parent = f.Parent };
        Complete(f, T(79), check, "unrelated cycle instance", otherCycle);
        Prepare(f, T(80), 13f, 1f);
        f.Start((typeof(CustomAgeCycle), "customAge", 8f));
        Complete(f, T(79), check, "custom derived cycle");
        Prepare(f, T(80), 13f, 1f);
        f.Start((typeof(CompBiosculpterPod_AgeReversalCycle), "customAge", 8f));
        Complete(f, T(79), check, "custom cycle key using vanilla class");
    }

    private static void Prepare(BiosculpterCycleChecks.Fixture f, long age, float minimum, float difficulty)
    {
        Current.Game.questManager.ExtraFactionQuestParts.Clear();
        f.Ideology = true;
        f.Service.SetAvailability(true);
        f.Service.SetAgeReversalAvailability(true);
        f.Service.TrySetFastCycles(true);
        f.Pawn.guest = null!;
        Set(f.Pawn.mindState.mentalStateHandler, "curStateInt", null!);
        CraftingChecks.SetFaction(f.Pawn, f.Player);
        CraftingChecks.SetFaction(f.Parent, f.Player);
        Set(f.Map.info.parent, "factionInt", f.Player);
        f.Start((typeof(CompBiosculpterPod_AgeReversalCycle), "ageReversal", 8f));
        f.Pawn.def.race.lifeStageAges = new List<LifeStageAge>
        {
            new() { minAge = 0f, def = new LifeStageDef { developmentalStage = DevelopmentalStage.Child } },
            new() { minAge = minimum, def = new LifeStageDef() }
        };
        // Like installed humans, a later stage named adult is not necessarily the
        // first stage carrying the Adult developmental flag used by AdultMinAge.
        if (minimum < 18f) f.Pawn.def.race.lifeStageAges.Add(new LifeStageAge
            { minAge = 18f, def = new LifeStageDef() });
        f.Pawn.ageTracker = new Pawn_AgeTracker(f.Pawn);
        Set(f.Pawn.ageTracker, "cachedLifeStageIndex", StageIndex(f.Pawn, age));
        f.Pawn.ageTracker.AgeBiologicalTicks = age;
        f.Pawn.ageTracker.BirthAbsTicks = -T(100);
        Set(Current.Game.tickManager, "gameStartAbsTick", 1);
        Current.Game.storyteller = Empty<Storyteller>();
        Current.Game.storyteller.difficulty = new Difficulty { adultAgingRate = difficulty };
        var ideo = Empty<Ideo>();
        Set(ideo, "allPreceptDefs", new HashSet<PreceptDef> { PreceptDefOf.AgeReversal_Demanded });
        f.Pawn.ideo = Empty<Pawn_IdeoTracker>();
        Set(f.Pawn.ideo, "ideo", ideo);
        resets = messages = stageNotifications = 0;
        displayedDeadline = -1;
    }

    private static void Complete(BiosculpterCycleChecks.Fixture f, long expected, Action<bool, string> check,
        string name, CompBiosculpterPod_AgeReversalCycle? cycle = null)
    {
        cycle ??= (CompBiosculpterPod_AgeReversalCycle)f.Pod.CurrentCycle;
        var tracker = f.Pawn.ageTracker;
        var chronological = tracker.AgeChronologicalTicks;
        var birth = tracker.BirthAbsTicks;
        var oldStage = tracker.CurLifeStageIndex;
        var work = f.Work;
        cycle.CycleCompleted(f.Pawn);
        check(tracker.AgeBiologicalTicks == expected, "Native age assignment uses original biological ticks: " + name);
        check(tracker.AgeChronologicalTicks == chronological && tracker.BirthAbsTicks == birth, "Chronological age unchanged: " + name);
        check(resets == 1 && ageAtReset == expected && tracker.LastAgeReversalReason == Pawn_AgeTracker.AgeReversalReason.ViaTreatment,
            "Native demand reset observes final age exactly once: " + name);
        var deadline = Math.Max(expected, T(25)) + GenDate.TicksPerYear - expected;
        check(tracker.AgeReversalDemandedDeadlineTicks == deadline && displayedDeadline == deadline / GenDate.TicksPerDay && messages == 1,
            "Native deadline and single completion notification agree with final age: " + name);
        var expectedGrowth = Math.Clamp((float)expected / GenDate.TicksPerYear / f.Pawn.RaceProps.lifeStageAges.Last().minAge, 0f, 1f);
        check(tracker.CurLifeStageIndex == StageIndex(f.Pawn, expected) && Math.Abs(tracker.Growth - expectedGrowth) < 0.00001f,
            "Public setter retains native life-stage/growth invariants: " + name);
        check(stageNotifications == (oldStage == tracker.CurLifeStageIndex ? 0 : 1),
            "Native life-stage transition notification occurs once when needed: " + name);
        check(f.Work == work, "Age completion does not change processing work: " + name);
    }

    private static int StageIndex(Pawn pawn, long age)
    {
        for (var i = pawn.RaceProps.lifeStageAges.Count - 1; i >= 0; i--)
            if (pawn.RaceProps.lifeStageAges[i].minAge <= age / GenDate.TicksPerYear + 1E-06f) return i;
        return 0;
    }
    private static void ObserveReset(Pawn_AgeTracker __instance) { resets++; ageAtReset = __instance.AgeBiologicalTicks; }
    private static TaggedString CompletionText(string key, NamedArgument pawn) => key;
    private static TaggedString DeadlineText(string key, NamedArgument pawn, NamedArgument deadline)
    {
        displayedDeadline = (int)deadline.arg;
        return key;
    }
    private static void CaptureMessage(string text, LookTargets targets, MessageTypeDef def, bool historical) => messages++;
    private static bool NoBiotechGraphics() => false;
    private static bool NoMapComponents(Thing pawn) => false;
    private static void ObserveStage(LifeStageWorker worker, Pawn pawn, LifeStageDef previous) => stageNotifications++;

    private static IEnumerable<CodeInstruction> IsolateMessages(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo method && method.DeclaringType == typeof(TranslatorFormattedStringExtensions) && method.Name == "Translate")
                copy.operand = AccessTools.Method(typeof(BiosculpterAgeReversalChecks), method.GetParameters().Length == 2 ? nameof(CompletionText) : nameof(DeadlineText));
            if (copy.operand is MethodInfo message && message.DeclaringType == typeof(Messages) && message.Name == nameof(Messages.Message))
                copy.operand = AccessTools.Method(typeof(BiosculpterAgeReversalChecks), nameof(CaptureMessage));
            yield return copy;
        }
    }
    private static IEnumerable<CodeInstruction> IsolateStageCallbacks(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var copy = new CodeInstruction(instruction);
            var replacement = copy.Calls(AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.BiotechActive))) ? nameof(NoBiotechGraphics) :
                copy.Calls(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.SpawnedOrAnyParentSpawned))) ? nameof(NoMapComponents) :
                copy.Calls(AccessTools.Method(typeof(LifeStageWorker), nameof(LifeStageWorker.Notify_LifeStageStarted))) ? nameof(ObserveStage) : null;
            if (replacement != null)
            {
                copy.opcode = OpCodes.Call;
                copy.operand = AccessTools.Method(typeof(BiosculpterAgeReversalChecks), replacement);
            }
            yield return copy;
        }
    }
    private static void InitializeDefOf(Type type)
    {
        var field = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
        var previous = field.GetValue(null);
        try { field.SetValue(null, true); RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
        finally { field.SetValue(null, previous); }
    }
    private sealed class CustomAgeCycle : CompBiosculpterPod_AgeReversalCycle { }
    private sealed class TestMentalState : MentalState { }
    private static TClass Empty<TClass>() where TClass : class => CraftingChecks.Empty<TClass>();
    private static void Set(object instance, string field, object value) => AccessTools.Field(instance.GetType(), field).SetValue(instance, value);
}
