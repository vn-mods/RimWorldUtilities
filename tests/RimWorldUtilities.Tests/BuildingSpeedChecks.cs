using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;
using Verse.AI;

internal static class BuildingSpeedChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static float rate, material, success, roll, probability, xp;
    private static int xpTicks, completions, failures;

    public static void RunCore(Action<bool, string> check)
    {
        using var scope = new MapScope();
        var f = new Fixture();
        check(f.Service.BuildingSpeedMultiplier == 1 && f.Crafting.Settings.BuildingSpeedMultiplier == 1, "Building defaults to 1x");
        check(!f.Service.IsBuildingSpeedAvailable && !f.Service.TrySetBuildingSpeedMultiplier(20), "Unavailable building speed cannot enable");
        f.Service.SetBuildingSpeedAvailability(true);
        f.Service.SetSpeedAvailability(true);
        f.Service.SetConstructionAvailability(true);
        f.Service.TrySetAlwaysLegendary(true);
        f.Service.TrySetSpeedMultiplier(3);
        f.Crafting.Settings.EnabledColonistNeeds.UnionWith(new[] { "Food", "Learning", "Play" });
        f.Crafting.Settings.EnabledLimits["Steel"] = 250000;
        f.Crafting.Settings.OriginalLimits["Steel"] = 75;
        f.Crafting.Settings.PendingResets.Add("WoodLog");
        foreach (var multiplier in CraftingService.SpeedMultipliers)
        {
            check(f.Service.TrySetBuildingSpeedMultiplier(multiplier), "Select building speed " + multiplier);
            check(f.Service.ScaleBuildingWork(6.25f, f.Worker, f.Frame) == 6.25f * multiplier, "Building calculation " + multiplier);
            check(f.Service.SpeedMultiplier == 3 && f.Service.AlwaysLegendary, "Building speed leaves crafting and quality independent");
        }
        check(f.Crafting.Durable!.EnabledColonistNeeds.SetEquals(new[] { "Food", "Learning", "Play" }) &&
            f.Crafting.Durable.EnabledLimits["Steel"] == 250000 && f.Crafting.Durable.OriginalLimits["Steel"] == 75 &&
            f.Crafting.Durable.PendingResets.Contains("WoodLog"), "Building writes preserve all shared settings");
        foreach (var invalid in new[] { -1, 0, 4, 7, 21, int.MaxValue })
            check(!f.Service.TrySetBuildingSpeedMultiplier(invalid) && f.Service.BuildingSpeedMultiplier == 20, "Reject building speed " + invalid);
        f.Crafting.FailWrites = true;
        check(!f.Service.TrySetBuildingSpeedMultiplier(2) && f.Service.BuildingSpeedMultiplier == 20 && f.Service.PersistenceError.Length > 0,
            "Failed building write keeps old preference");
        f.Crafting.FailWrites = false;
        check(f.Service.TrySetBuildingSpeedMultiplier(1) && f.Service.PersistenceError.Length == 0, "Save return to normal building speed");
        foreach (var work in new[] { 0f, -0f, 1.2345f, float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity })
            check(BitConverter.SingleToInt32Bits(f.Service.ScaleBuildingWork(work, null!, null!)) == BitConverter.SingleToInt32Bits(work),
                "Building 1x preserves native bits without context lookup");
        f.Crafting.Settings.BuildingSpeedMultiplier = 99;
        check(f.Service.BuildingSpeedMultiplier == 1 && f.Service.ScaleBuildingWork(5f, null!, null!) == 5f, "Invalid saved building speed falls back to 1x");
        f.Service.TrySetBuildingSpeedMultiplier(1);
        check(f.Crafting.Settings.BuildingSpeedMultiplier == 1, "Explicit 1x repairs invalid saved preference");

        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("normal frame", _ => { }, true),
            ("foreign faction", x => CraftingChecks.SetFaction(x.Worker, CraftingChecks.Empty<Faction>()), false),
            ("factionless", x => CraftingChecks.SetFaction(x.Worker, null), false),
            ("no player faction", x => x.Crafting.PlayerFaction = null, false),
            ("prisoner", x => x.Crafting.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", x => x.Crafting.SetGuest(GuestStatus.Slave), true),
            ("player mechanoid", x =>
            {
                x.Worker.def.race = CraftingChecks.Empty<RaceProperties>();
                typeof(RaceProperties).GetField("fleshType", PrivateInstance)!.SetValue(x.Worker.def.race, FleshTypeDefOf.Mechanoid);
            }, true),
            ("no humanlike or skills requirement", x => { x.Worker.def.race = CraftingChecks.Empty<RaceProperties>(); x.Worker.skills = null!; }, true),
            ("destroyed worker", x => CraftingChecks.Destroy(x.Worker), false),
            ("destroyed frame", x => CraftingChecks.Destroy(x.Frame), false),
            ("despawned frame", x => Set(x.Frame, "mapIndexOrState", (sbyte)-1), false),
            ("foreign frame", x => CraftingChecks.SetFaction(x.Frame, CraftingChecks.Empty<Faction>()), false),
            ("terrain frame", x => x.Frame.def.entityDefToBuild = CraftingChecks.Empty<TerrainDef>(), true),
            ("unsupported definition", x =>
            {
                var item = CraftingChecks.Empty<ThingDef>();
                item.category = ThingCategory.Item;
                x.Frame.def.entityDefToBuild = item;
            }, false),
            ("missing definition", x => x.Frame.def = null!, false),
            ("changed target", x => x.Driver.job.targetA = CraftingChecks.Empty<Frame>(), false),
            ("missing job", x => x.Worker.jobs.curJob = null!, false),
            ("different job", x => x.Driver.job = CraftingChecks.Empty<Job>(), false),
            ("different worker", x => x.Driver.pawn = CraftingChecks.Empty<Pawn>(), false),
            ("missing driver", x => x.Worker.jobs.curDriver = null!, false),
            ("no job tracker", x => x.Worker.jobs = null!, false),
            ("production", x => x.Worker.jobs.curDriver = x.Crafting.Driver, false),
            ("repair", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_Repair>(), false),
            ("deconstruction", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_Deconstruct>(), false),
            ("smoothing", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_SmoothFloor>(), false),
            ("roof", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_BuildRoof>(), false),
            ("special subclass", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<SpecialDriver>(), false)
        };
        foreach (var test in cases)
        {
            f = new Fixture();
            f.Service.SetBuildingSpeedAvailability(true);
            f.Service.TrySetBuildingSpeedMultiplier(20);
            test.Change(f);
            check(f.Service.IsEligibleBuildingProgress(f.Worker, f.Frame) == test.Eligible, "Building eligibility: " + test.Name);
            check(f.Service.ScaleBuildingWork(2f, f.Worker, f.Frame) == (test.Eligible ? 40f : 2f), "Building effect: " + test.Name);
        }

        f = new Fixture();
        f.Service.SetBuildingSpeedAvailability(true);
        f.Service.TrySetBuildingSpeedMultiplier(5);
        f.Service.SetConstructionAvailability(true);
        f.Service.SetAvailability(true);
        f.Service.SetSpeedAvailability(true);
        f.Service.TrySetSpeedMultiplier(3);
        f.Service.TrySetAlwaysLegendary(true);
        f.Service.SetBuildingSpeedAvailability(false, "Building mismatch");
        check(f.Service.ScaleBuildingWork(2f, f.Worker, f.Frame) == 2f &&
            f.Service.ResolveConstructionQuality(QualityCategory.Good, f.Frame, f.Worker) == QualityCategory.Legendary &&
            f.Service.IsAvailable && f.Service.IsSpeedAvailable && f.Service.SpeedMultiplier == 3,
            "Building failure preserves both quality patches and crafting speed");
        f.Service.SetBuildingSpeedAvailability(true);
        f.Service.SetConstructionAvailability(false, "Quality mismatch");
        f.Service.SetAvailability(false, "Recipe mismatch");
        f.Service.SetSpeedAvailability(false, "Crafting speed mismatch");
        check(f.Service.ScaleBuildingWork(2f, f.Worker, f.Frame) == 10f, "Building speed survives independent feature failures");
        var copy = f.Crafting.Settings.Copy();
        check(copy.Matches(f.Crafting.Settings) && copy.BuildingSpeedMultiplier == 5, "Copy includes building speed");
        copy.BuildingSpeedMultiplier = 10;
        check(!copy.Matches(f.Crafting.Settings), "Verification includes building speed");
        f.Crafting.Settings.CopyFrom(copy);
        check(f.Service.BuildingSpeedMultiplier == 10 && f.Service.SpeedMultiplier == 3 && f.Service.AlwaysLegendary,
            "CopyFrom preserves independent preferences");
        ValidatePatterns(check);
    }

    private static MethodInfo Resolve(Action<bool, string> check)
    {
        var iterator = BuildingSpeedPatch.FindIterator();
        check(iterator?.ReturnType == typeof(bool), "Resolve construction iterator without generated names");
        var il = PatchProcessor.GetOriginalInstructions(iterator!);
        check(CraftingSpeedPatch.TryResolveCallback(il, out var callback, out _), "Resolve construction interval assignment");
        check(!CraftingSpeedPatch.TryResolveCallback(il.Concat(il).ToList(), out _, out _), "Reject duplicate construction delegates");
        return callback!;
    }

    private static void ValidatePatterns(Action<bool, string> check)
    {
        var callback = Resolve(check);
        var original = PatchProcessor.GetOriginalInstructions(callback);
        check(BuildingSpeedPatch.TryRewrite(original, out var rewritten, out var error), "Match installed construction IL: " + error);
        check(rewritten.Count == original.Count + 5 && rewritten.Count(i => Equals(i.operand, BuildingSpeedPatch.ScaleMethod)) == 1,
            "Insert one scale call and one store before both work consumers");
        var pivot = original.FindIndex(i => i.operand is MethodInfo m && m.Name == "get_WorkToBuild") - 1;
        check(rewritten.Take(pivot).Concat(rewritten.Skip(pivot + 5)).Zip(original)
                .All(pair => pair.First.opcode == pair.Second.opcode && Equals(pair.First.operand, pair.Second.operand)),
            "Retain every native XP/stat/RNG/resource/completion instruction in original order");
        var label = original[pivot].labels.Single();
        var begin = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        var end = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        original[pivot].blocks.AddRange(new[] { begin, end });
        check(BuildingSpeedPatch.TryRewrite(original, out rewritten, out _), "Retain construction branch and exception metadata");
        check(rewritten[pivot].labels.Contains(label) && rewritten[pivot].blocks.SequenceEqual(new[] { begin }) &&
            rewritten[pivot + 5].blocks.SequenceEqual(new[] { end }) && rewritten[pivot + 5].labels.Count == 0,
            "Both material branches run scaling; exception boundaries preserved");
        check(original[pivot].labels.Contains(label) && original[pivot].blocks.Count == 2, "Do not mutate source instruction metadata");
        var moved = original.Select(i => new CodeInstruction(i)).ToList();
        foreach (var instruction in moved)
        {
            if (instruction.opcode == OpCodes.Ldloc_2) { instruction.opcode = OpCodes.Ldloc_S; instruction.operand = (byte)7; }
            if (instruction.opcode == OpCodes.Stloc_2) { instruction.opcode = OpCodes.Stloc_S; instruction.operand = (byte)7; }
        }
        check(BuildingSpeedPatch.TryRewrite(moved, out var relocated, out _) &&
            relocated[pivot].opcode == OpCodes.Ldloc_S && Equals(relocated[pivot].operand, (byte)7), "Discover shared work local without a fixed slot");
        var materialChanged = original.Select(i => new CodeInstruction(i)).ToList();
        materialChanged.First(i => i.operand is FieldInfo f && f.Name == "ConstructionSpeedFactor").opcode = OpCodes.Nop;
        var missingPow = original.Select(i => new CodeInstruction(i)).ToList();
        missingPow.First(i => i.operand is MethodInfo m && m.Name == "Pow").opcode = OpCodes.Nop;
        var extraWrite = original.Select(i => new CodeInstruction(i)).ToList();
        extraWrite.Insert(pivot, new CodeInstruction(OpCodes.Stloc_2));
        var wrongProbability = original.Select(i => new CodeInstruction(i)).ToList();
        var pow = wrongProbability.FindIndex(i => i.operand is MethodInfo m && m.Name == "Pow");
        wrongProbability[pow - 3] = new CodeInstruction(OpCodes.Ldloc_3);
        foreach (var invalid in new[] { new List<CodeInstruction>(), original.Concat(original).ToList(), materialChanged,
                     missingPow, extraWrite, wrongProbability, rewritten })
        {
            check(!BuildingSpeedPatch.TryRewrite(invalid, out var unchanged, out error) && error.Length > 0, "Reject altered/ambiguous/already scaled construction IL");
            check(unchanged.SequenceEqual(invalid), "Failed construction match returns every original instruction untouched");
        }
    }

    public static void RunNative(Action<bool, string> check)
    {
        using var scope = new MapScope();
        var callback = Resolve(check);
        var f = new Fixture();
        f.Service.SetConstructionAvailability(true);
        f.Service.SetSpeedAvailability(true);
        f.Service.TrySetAlwaysLegendary(true);
        BuildingSpeedPatch.Install(f.Service);
        check(f.Service.IsBuildingSpeedAvailable, "Install real construction patch: " + f.Service.BuildingSpeedUnavailableReason);
        BuildingSpeedPatch.Install(f.Service);
        check(Harmony.GetPatchInfo(callback).Transpilers.Count(p => p.owner == BuildingSpeedPatch.OwnerId) == 1, "Register building speed once");
        var complete = typeof(Frame).GetMethod(nameof(Frame.CompleteConstruction))!;
        check(Harmony.GetPatchInfo(complete).Transpilers.Any(p => p.owner == LegendaryConstructionPatch.OwnerId) &&
            !Harmony.GetPatchInfo(complete).Owners.Contains(BuildingSpeedPatch.OwnerId), "Building speed leaves existing construction-quality patch in place");

        var probe = typeof(ProbeRunner).GetMethod(nameof(ProbeRunner.Step))!;
        new Harmony(BuildingSpeedPatch.OwnerId + ".tests").Patch(probe,
            transpiler: new HarmonyMethod(typeof(BuildingSpeedChecks), nameof(ProbeTranspiler)));
        var runner = new ProbeRunner { Actor = f.Worker, Target = f.Frame };
        rate = 2f; success = 0.81f; roll = 1f;
        foreach (var multiplier in CraftingService.SpeedMultipliers)
        foreach (var factor in new[] { 0.5f, 1.5f })
        {
            material = factor;
            Set(f.Frame, "stuffInt", CraftingChecks.Empty<ThingDef>());
            f.Service.TrySetBuildingSpeedMultiplier(multiplier);
            f.Frame.workDone = xp = 0f;
            xpTicks = completions = failures = 0;
            runner.Step(4);
            var work = rate * 1.7f * 4f * factor * multiplier;
            check(f.Frame.workDone == work, "Emitted construction scales after material: " + multiplier + "/" + factor);
            check(probability == 1f - Mathf.Pow(success, work / 200f), "Same scaled work drives native botch formula");
            check(xpTicks == 4 && xp == 1f, "XP and elapsed ticks unchanged at " + multiplier);
            check(completions == (work >= 200f ? 1 : 0) && failures == 0, "Native completion branch remains single");
        }
        Set(f.Frame, "stuffInt", null);
        f.Frame.workDone = 0f;
        runner.Step(4);
        check(f.Frame.workDone == rate * 1.7f * 4f * 20f, "Frames without material also scale exactly once");
        f.Frame.workDone = 0f;
        roll = 0f;
        completions = failures = 0;
        runner.Step(4);
        check(failures == 1 && completions == 0 && f.Frame.workDone == 0f, "Botch branch prevents progress and completion");
        roll = 1f;
        CraftingChecks.SetFaction(f.Worker, CraftingChecks.Empty<Faction>());
        runner.Step(4);
        check(f.Frame.workDone == rate * 1.7f * 4f, "Patched progress leaves foreign workers unchanged");
        CraftingChecks.SetFaction(f.Worker, f.Crafting.PlayerFaction);
        var invalid = new List<CodeInstruction>();
        var transpiler = typeof(BuildingSpeedPatch).GetMethod("Transpiler", PrivateStatic)!;
        var untouched = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { invalid })!;
        check(!f.Service.IsBuildingSpeedAvailable && untouched.SequenceEqual(invalid) && f.Service.BuildingSpeedUnavailableReason.Length > 0,
            "Incompatible Harmony rebuild disables only building speed");
        check(f.Service.IsConstructionAvailable && f.Service.AlwaysLegendary && f.Service.IsSpeedAvailable,
            "Failure preserves quality and crafting availability");
        f.Frame.workDone = 0f;
        runner.Step(4);
        check(f.Frame.workDone == rate * 1.7f * 4f, "Unavailable feature gates an already emitted helper");
    }

    private static IEnumerable<CodeInstruction> ProbeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        if (!BuildingSpeedPatch.TryRewrite(instructions.ToList(), out var result, out var error)) throw new Exception(error);
        // Test-only stand-ins avoid initializing StatDefOf, the player profile,
        // map spawning and RNG. All calculation/branch instructions remain native-shaped.
        var stat = "";
        foreach (var instruction in result)
        {
            if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.DeclaringType == typeof(StatDefOf))
            {
                stat = field.Name;
                instruction.opcode = OpCodes.Ldnull;
                instruction.operand = null;
            }
            if (instruction.operand is not MethodInfo method) continue;
            var replacement = method.DeclaringType == typeof(StatExtension) ? method.Name switch
            {
                "GetStatValue" => stat == "ConstructionSpeed" ? nameof(ProbeRate) : nameof(ProbeSuccess),
                "GetStatValueAbstract" => nameof(ProbeMaterial),
                _ => null
            } : method.DeclaringType == typeof(Frame) ? method.Name switch
            {
                "get_WorkToBuild" => nameof(ProbeTotal),
                "CompleteConstruction" => nameof(ProbeComplete),
                "FailConstruction" => nameof(ProbeFail),
                _ => null
            } : null;
            if (replacement == null) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = typeof(BuildingSpeedChecks).GetMethod(replacement, PrivateStatic)!;
        }
        return result;
    }
    private static float ProbeRate(Thing thing, StatDef stat, bool post, int stale) => rate;
    private static float ProbeSuccess(Thing thing, StatDef stat, bool post, int stale) => success;
    private static float ProbeMaterial(BuildableDef def, StatDef stat, ThingDef stuff) => material;
    private static float ProbeTotal(Frame frame) => 200f;
    private static void ProbeComplete(Frame frame, Pawn actor) => completions++;
    private static void ProbeFail(Frame frame, Pawn actor) => failures++;

    private sealed class ProbeRunner
    {
        public Pawn Actor = null!;
        public Frame Target = null!;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Step(int delta)
        {
            var actor = Actor;
            var frame = Target;
            xpTicks += delta;
            xp += 0.25f * delta;
            float work = actor.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f * (float)delta;
            if (frame.Stuff != null) work *= frame.Stuff.GetStatValueAbstract(StatDefOf.ConstructionSpeedFactor);
            float total = frame.WorkToBuild;
            var chance = actor.GetStatValue(StatDefOf.ConstructSuccessChance);
            probability = 1f - Mathf.Pow(chance, work / total);
            if (roll < probability) { frame.FailConstruction(actor); return; }
            frame.workDone += work;
            if (frame.workDone >= total) frame.CompleteConstruction(actor);
        }
    }

    private sealed class Fixture
    {
        public readonly CraftingChecks.Fixture Crafting = new();
        public CraftingService Service => Crafting.Service;
        public Pawn Worker => Crafting.Worker;
        public readonly Frame Frame = CraftingChecks.Empty<Frame>();
        public readonly JobDriver_ConstructFinishFrame Driver = CraftingChecks.Empty<JobDriver_ConstructFinishFrame>();
        public Fixture()
        {
            Frame.def = CraftingChecks.Empty<ThingDef>();
            var building = CraftingChecks.Empty<ThingDef>();
            building.category = ThingCategory.Building;
            building.comps = new() { new CompProperties { compClass = typeof(CompQuality) } };
            Frame.def.entityDefToBuild = building;
            CraftingChecks.SetFaction(Frame, Crafting.PlayerFaction);
            Set(Frame, "mapIndexOrState", (sbyte)0);
            Crafting.Job.targetA = Frame;
            Driver.job = Crafting.Job;
            Driver.pawn = Worker;
            Worker.jobs.curDriver = Driver;
        }
    }
    private sealed class SpecialDriver : JobDriver_ConstructFinishFrame { }
    private sealed class MapScope : IDisposable
    {
        private readonly Game previous = Current.Game;
        private readonly FleshTypeDef previousMech;
        public MapScope()
        {
            var binding = typeof(DefOfHelper).GetField("bindingNow", PrivateStatic)!;
            var previousBinding = binding.GetValue(null);
            try { binding.SetValue(null, true); RuntimeHelpers.RunClassConstructor(typeof(FleshTypeDefOf).TypeHandle); }
            finally { binding.SetValue(null, previousBinding); }
            previousMech = FleshTypeDefOf.Mechanoid;
            FleshTypeDefOf.Mechanoid = CraftingChecks.Empty<FleshTypeDef>();
            var game = CraftingChecks.Empty<Game>();
            typeof(Game).GetField("maps", PrivateInstance)!.SetValue(game, new List<Map> { CraftingChecks.Empty<Map>() });
            Current.Game = game;
        }
        public void Dispose()
        {
            Current.Game = previous;
            FleshTypeDefOf.Mechanoid = previousMech;
        }
    }
    private static void Set(Thing thing, string field, object? value) => typeof(Thing).GetField(field, PrivateInstance)!.SetValue(thing, value);
}
