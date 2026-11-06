using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Patches;
using Verse;

internal static class ConstructionQualityChecks
{
    private static int generationCalls;
    private static Pawn? generatedBy;
    private static SkillDef? generatedSkill;
    private static bool consumedInspiration;

    public static void RunCore(Action<bool, string> check)
    {
        var fixture = new Fixture();
        fixture.Crafting.Worker.jobs = null!;
        check(fixture.Eligible(), "Player construction supports quality without a production bill");
        check(fixture.Quality() == QualityCategory.Good, "Disabled construction preserves native quality");
        check(!fixture.Crafting.Service.IsLegendaryAvailable, "Both quality patches start unavailable");
        fixture.Crafting.Service.SetConstructionAvailability(true);
        check(fixture.Crafting.Service.TrySetAlwaysLegendary(true), "Construction can enable the existing shared setting");
        check(fixture.Quality() == QualityCategory.Legendary && fixture.Crafting.Writes == 1 &&
            fixture.Crafting.Durable!.AlwaysLegendaryCrafting, "Construction reuses durable Always Legendary preference");
        check(fixture.Crafting.Service.TrySetAlwaysLegendary(false) && fixture.Quality() == QualityCategory.Good,
            "Construction disables immediately with the shared checkbox");

        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("foreign worker", f => CraftingChecks.SetFaction(f.Crafting.Worker, CraftingChecks.Empty<Faction>()), false),
            ("factionless worker", f => CraftingChecks.SetFaction(f.Crafting.Worker, null), false),
            ("missing player faction", f => f.Crafting.PlayerFaction = null, false),
            ("arrested player worker", f => f.Crafting.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", f => f.Crafting.SetGuest(GuestStatus.Slave), true),
            ("non-humanlike worker without skills", f =>
            {
                f.Crafting.Worker.def.race = CraftingChecks.Empty<RaceProperties>();
                f.Crafting.Worker.skills = null!;
            }, true),
            ("destroyed worker", f => CraftingChecks.Destroy(f.Crafting.Worker), false),
            ("foreign building faction", f => CraftingChecks.SetFaction(f.Frame, CraftingChecks.Empty<Faction>()), false),
            ("factionless building", f => CraftingChecks.SetFaction(f.Frame, null), false),
            ("no quality support", f => f.Building.comps.Clear(), false),
            ("missing comp definitions", f => f.Building.comps = null!, false),
            ("item rather than building", f => f.Building.category = ThingCategory.Item, false),
            ("terrain construction", f => f.Frame.def.entityDefToBuild = CraftingChecks.Empty<TerrainDef>(), false),
            ("missing build definition", f => f.Frame.def.entityDefToBuild = null!, false),
            ("missing frame definition", f => f.Frame.def = null!, false),
            ("already destroyed frame at native quality call", f => CraftingChecks.Destroy(f.Frame), true)
        };
        foreach (var test in cases)
        {
            fixture = new Fixture();
            fixture.Crafting.Settings.AlwaysLegendaryCrafting = true;
            fixture.Crafting.Service.SetConstructionAvailability(true);
            test.Change(fixture);
            check(fixture.Eligible() == test.Eligible, "Construction eligibility: " + test.Name);
            check(fixture.Quality() == (test.Eligible ? QualityCategory.Legendary : QualityCategory.Good),
                "Construction quality: " + test.Name);
        }

        fixture = new Fixture();
        fixture.Crafting.Service.SetAvailability(true);
        fixture.Crafting.Service.SetConstructionAvailability(true);
        check(fixture.Crafting.Service.TrySetAlwaysLegendary(true), "One checkbox enables both supported quality paths");
        check(fixture.Crafting.Quality() == QualityCategory.Legendary && fixture.Quality() == QualityCategory.Legendary,
            "Existing crafted-item quality works alongside construction");
        fixture.Crafting.Service.SetConstructionAvailability(false, "Construction mismatch");
        check(fixture.Quality() == QualityCategory.Good && fixture.Crafting.Quality() == QualityCategory.Legendary &&
            fixture.Crafting.Service.IsLegendaryAvailable && !fixture.Crafting.Service.IsLegendaryFullyAvailable,
            "Construction failure preserves crafting and exposes partial availability");
        check(fixture.Crafting.Service.LegendaryUnavailableReason == "Construction mismatch",
            "Partial availability explains the failed quality path");
        fixture.Crafting.Service.SetConstructionAvailability(true);
        fixture.Crafting.Service.SetAvailability(false, "Recipe mismatch");
        check(fixture.Quality() == QualityCategory.Legendary && fixture.Crafting.Quality() == QualityCategory.Good &&
            fixture.Crafting.Service.LegendaryUnavailableReason == "Recipe mismatch",
            "Recipe failure preserves construction");
        fixture.Crafting.Service.SetConstructionAvailability(false, "Construction mismatch");
        check(!fixture.Crafting.Service.IsLegendaryAvailable && !fixture.Crafting.Service.TrySetAlwaysLegendary(true),
            "Neither unavailable path can be enabled");
        ValidateRewrite(check);
    }

    private static void ValidateRewrite(Action<bool, string> check)
    {
        var target = LegendaryConstructionPatch.FindTarget();
        check(target?.DeclaringType == typeof(Frame) && !target.IsStatic && target.ReturnType == typeof(void) &&
            target.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Pawn) }),
            "Resolve exact Frame.CompleteConstruction(Pawn) signature");
        var label = new DynamicMethod("ConstructionLabels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        var call = new CodeInstruction(OpCodes.Call, LegendaryConstructionPatch.NativeQualityMethod);
        call.labels.Add(label);
        var begin = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        var end = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        call.blocks.AddRange(new[] { begin, end });
        var original = new[] { new CodeInstruction(OpCodes.Nop), call, new CodeInstruction(OpCodes.Ret) };
        check(LegendaryConstructionPatch.TryRewrite(original, out var rewritten, out _), "Rewrite one construction quality call");
        check(rewritten.Count == original.Length + 1 && rewritten[1].opcode == OpCodes.Ldarg_0 &&
            Equals(rewritten[2].operand, LegendaryConstructionPatch.RedirectMethod), "Append the frame receiver after native arguments");
        check(rewritten[1].labels.SequenceEqual(new[] { label }) && rewritten[2].labels.Count == 0 &&
            rewritten[1].blocks.SequenceEqual(new[] { begin }) && rewritten[2].blocks.SequenceEqual(new[] { end }),
            "Construction rewrite preserves branch and exception boundaries");
        check(call.labels.Count == 1 && call.blocks.Count == 2 &&
            Equals(call.operand, LegendaryConstructionPatch.NativeQualityMethod), "Construction rewrite never mutates input metadata");
        foreach (var incompatible in new[]
        {
            new[] { new CodeInstruction(OpCodes.Nop) },
            new[] { call, call },
            new[] { call, new CodeInstruction(OpCodes.Call, LegendaryConstructionPatch.RedirectMethod) }
        })
        {
            check(!LegendaryConstructionPatch.TryRewrite(incompatible, out rewritten, out var error) && error.Length > 0 &&
                rewritten.SequenceEqual(incompatible), "Missing, ambiguous or already redirected construction IL stays unchanged");
        }
    }

    public static void RunNative(Action<bool, string> check)
    {
        var target = LegendaryConstructionPatch.FindTarget()!;
        var original = PatchProcessor.GetOriginalInstructions(target);
        check(LegendaryConstructionPatch.TryRewrite(original, out var rewritten, out _), "Rewrite actual construction game IL");
        check(rewritten.Count(i => Equals(i.operand, LegendaryConstructionPatch.RedirectMethod)) == 1,
            "Actual construction IL has exactly one scoped redirect");
        var remainingCalls = new Func<IEnumerable<CodeInstruction>, IEnumerable<object?>>(instructions => instructions
            .Where(i => i.operand is MethodBase && !Equals(i.operand, LegendaryConstructionPatch.NativeQualityMethod) &&
                !Equals(i.operand, LegendaryConstructionPatch.RedirectMethod)).Select(i => i.operand));
        check(remainingCalls(original).SequenceEqual(remainingCalls(rewritten)),
            "Construction assignment, art, notification, spawn and other calls remain in native order");
        var setter = typeof(CompQuality).GetMethod(nameof(CompQuality.SetQuality))!;
        check(original.Count(i => Equals(i.operand, setter)) == 1 && rewritten.Count(i => Equals(i.operand, setter)) == 1,
            "Construction retains exactly one native quality assignment");
        var helper = PatchProcessor.GetOriginalInstructions(LegendaryConstructionPatch.RedirectMethod);
        check(helper.Count(i => Equals(i.operand, LegendaryConstructionPatch.NativeQualityMethod)) == 1,
            "Construction helper retains exactly one native generation call");

        var fixture = new Fixture();
        fixture.Crafting.Service.SetAvailability(true);
        LegendaryConstructionPatch.Install(fixture.Crafting.Service);
        check(fixture.Crafting.Service.IsConstructionAvailable,
            "Install Harmony 3 construction patch: " + fixture.Crafting.Service.ConstructionUnavailableReason);
        LegendaryConstructionPatch.Install(fixture.Crafting.Service);
        check(Harmony.GetPatchInfo(target).Transpilers.Count(p => p.owner == LegendaryConstructionPatch.OwnerId) == 1,
            "Construction patch registers once");
        check(Harmony.GetPatchInfo(LegendaryConstructionPatch.NativeQualityMethod)?.Owners.Contains(
            LegendaryConstructionPatch.OwnerId) != true, "Construction never patches the global quality generator");
        check(Harmony.GetPatchInfo(LegendaryCraftingPatch.FindTarget()).Transpilers.Count(
            p => p.owner == LegendaryCraftingPatch.OwnerId) == 1, "Construction leaves the existing recipe patch installed");
        var cube = typeof(JobDriver_BuildCubeSculpture).GetMethod("PlaceAndFinish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var install = typeof(Blueprint_Install).GetMethod(nameof(Blueprint_Install.TryReplaceWithSolidThing))!;
        check(new[] { cube, install }.All(method => Harmony.GetPatchInfo(method)?.Owners.Contains(
            LegendaryConstructionPatch.OwnerId) != true), "Cube obsession and furniture installation are not patched");

        // Exercise the emitted redirect without starting a map or initializing the
        // player's profile. The probe's first argument stands in for Frame's this.
        new Harmony(LegendaryConstructionPatch.OwnerId + ".tests.generator").Patch(
            LegendaryConstructionPatch.RedirectMethod,
            transpiler: new HarmonyMethod(typeof(ConstructionQualityChecks), nameof(StubNativeGeneration)));
        var probe = typeof(ConstructionQualityChecks).GetMethod(nameof(Probe), BindingFlags.Static | BindingFlags.NonPublic)!;
        new Harmony(LegendaryConstructionPatch.OwnerId + ".tests").Patch(probe,
            transpiler: new HarmonyMethod(typeof(LegendaryConstructionPatch), "Transpiler"));
        var existingFurniture = new CraftingChecks.Fixture().Product;
        existingFurniture.def.category = ThingCategory.Building;
        existingFurniture.compQuality.SetQuality(QualityCategory.Poor, source: null);
        var existingCallbacks = existingFurniture.QualityCallbacks;
        foreach (var enabled in new[] { false, true, false })
        {
            check(fixture.Crafting.Service.TrySetAlwaysLegendary(enabled), "Change construction through the shared setting");
            var calls = generationCalls;
            var callbacks = fixture.Crafting.Product.QualityCallbacks;
            Probe(fixture.Frame, fixture.Crafting.Worker, fixture.Crafting.Product, fixture.Crafting.Recipe.workSkill, enabled);
            check(fixture.Crafting.Product.compQuality.Quality == (enabled ? QualityCategory.Legendary : QualityCategory.Good),
                "Patched construction follows enabled/disabled setting");
            check(generationCalls == calls + 1 && ReferenceEquals(generatedBy, fixture.Crafting.Worker) &&
                ReferenceEquals(generatedSkill, fixture.Crafting.Recipe.workSkill) && consumedInspiration == enabled,
                "Construction forwards original generation and inspiration arguments exactly once");
            check(fixture.Crafting.Product.QualityCallbacks == callbacks + 1,
                "Constructed product receives exactly one native quality callback");
        }
        check(existingFurniture.compQuality.Quality == QualityCategory.Poor && existingFurniture.QualityCallbacks == existingCallbacks,
            "Toggling and constructing furniture never rewrites an existing building");
        check(fixture.Crafting.Service.TrySetAlwaysLegendary(true), "Enable construction exclusion probes");
        CraftingChecks.SetFaction(fixture.Crafting.Worker, CraftingChecks.Empty<Faction>());
        Probe(fixture.Frame, fixture.Crafting.Worker, fixture.Crafting.Product, fixture.Crafting.Recipe.workSkill);
        check(fixture.Crafting.Product.compQuality.Quality == QualityCategory.Good, "Patched NPC construction retains native quality");
        CraftingChecks.SetFaction(fixture.Crafting.Worker, fixture.Crafting.PlayerFaction);
        fixture.Crafting.SetGuest(GuestStatus.Prisoner);
        Probe(fixture.Frame, fixture.Crafting.Worker, fixture.Crafting.Product, fixture.Crafting.Recipe.workSkill);
        check(fixture.Crafting.Product.compQuality.Quality == QualityCategory.Good, "Patched player prisoner retains native construction quality");
        fixture.Crafting.Worker.guest = null!;
        CraftingChecks.Destroy(fixture.Frame);
        Probe(fixture.Frame, fixture.Crafting.Worker, fixture.Crafting.Product, fixture.Crafting.Recipe.workSkill);
        check(fixture.Crafting.Product.compQuality.Quality == QualityCategory.Legendary,
            "Destroyed native frame still supplies construction context");

        var noQuality = new Thing();
        var before = generationCalls;
        Probe(fixture.Frame, fixture.Crafting.Worker, noQuality, fixture.Crafting.Recipe.workSkill);
        check(generationCalls == before && noQuality.TryGetComp<CompQuality>() == null,
            "Construction without a quality component never generates or adds quality");

        var transpiler = typeof(LegendaryConstructionPatch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var incompatible = new List<CodeInstruction> { new(OpCodes.Nop) };
        var unchanged = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { incompatible })!;
        check(!fixture.Crafting.Service.IsConstructionAvailable && fixture.Crafting.Service.IsAvailable &&
            fixture.Crafting.Service.ConstructionUnavailableReason.Contains("found") && unchanged.SequenceEqual(incompatible),
            "Construction mismatch fails closed without disabling recipe quality");
        Probe(fixture.Frame, fixture.Crafting.Worker, fixture.Crafting.Product, fixture.Crafting.Recipe.workSkill);
        check(fixture.Crafting.Product.compQuality.Quality == QualityCategory.Good,
            "An already-emitted construction redirect falls back after incompatibility");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Probe(Thing frame, Pawn worker, Thing product, SkillDef skill, bool consumeInspiration = true)
    {
        var quality = product.TryGetComp<CompQuality>();
        if (quality != null)
        {
            quality.SetQuality(QualityUtility.GenerateQualityCreatedByPawn(worker, skill, consumeInspiration), source: null);
        }
    }

    private static IEnumerable<CodeInstruction> StubNativeGeneration(IEnumerable<CodeInstruction> instructions)
    {
        var stub = typeof(ConstructionQualityChecks).GetMethod(nameof(CountGeneration), BindingFlags.Static | BindingFlags.NonPublic)!;
        return instructions.Select(instruction =>
        {
            var copy = new CodeInstruction(instruction);
            if (copy.opcode == OpCodes.Call && Equals(copy.operand, LegendaryConstructionPatch.NativeQualityMethod)) copy.operand = stub;
            return copy;
        });
    }

    private static QualityCategory CountGeneration(Pawn worker, SkillDef skill, bool consumeInspiration)
    {
        generationCalls++;
        generatedBy = worker;
        generatedSkill = skill;
        consumedInspiration = consumeInspiration;
        return QualityCategory.Good;
    }

    private sealed class Fixture
    {
        public readonly CraftingChecks.Fixture Crafting = new();
        public readonly Thing Frame = new() { def = CraftingChecks.Empty<ThingDef>() };
        public readonly ThingDef Building = CraftingChecks.Empty<ThingDef>();

        public Fixture()
        {
            Building.category = ThingCategory.Building;
            Building.comps = new() { new CompProperties { compClass = typeof(CompQuality) } };
            Frame.def.entityDefToBuild = Building;
            CraftingChecks.SetFaction(Frame, Crafting.PlayerFaction);
        }

        public bool Eligible() => Crafting.Service.IsEligibleConstruction(Frame, Crafting.Worker);
        public QualityCategory Quality() => Crafting.Service.ResolveConstructionQuality(QualityCategory.Good, Frame, Crafting.Worker);
    }
}
