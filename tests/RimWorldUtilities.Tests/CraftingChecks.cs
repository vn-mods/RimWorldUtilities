using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

internal static class CraftingChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int generationCalls;
    private static Pawn? generatedBy;
    private static SkillDef? generatedSkill;
    private static bool consumedInspiration;

    public static void RunCore(Action<bool, string> check)
    {
        var fixture = new Fixture();
        check(!fixture.Service.AlwaysLegendary, "Legendary crafting is opt-in");
        check(fixture.Service.IsEligible(fixture.Product, fixture.Recipe, fixture.Worker), "Eligible production context");
        check(fixture.Quality() == QualityCategory.Good, "Disabled feature preserves generated quality");
        check(!fixture.Service.TrySetAlwaysLegendary(true), "Cannot enable an unavailable patch");
        fixture.Service.SetAvailability(true);
        check(fixture.Service.TrySetAlwaysLegendary(true), "Enable available Legendary crafting");
        check(fixture.Writes == 1 && fixture.Durable!.AlwaysLegendaryCrafting && !fixture.EnabledDuringWrite,
            "Persist before enabling");
        check(fixture.Quality() == QualityCategory.Legendary, "Enabled feature overrides eligible quality");
        fixture.Service.SetAvailability(false, "Incompatible pattern");
        check(fixture.Quality() == QualityCategory.Good, "Unavailable patch cannot override quality");
        fixture.Service.SetAvailability(true);
        fixture.FailWrites = true;
        check(!fixture.Service.TrySetAlwaysLegendary(false) && fixture.Service.AlwaysLegendary,
            "Failed settings write retains previous enabled state");
        check(fixture.Service.PersistenceError.Contains("Simulated"), "Expose crafting persistence error");
        fixture.FailWrites = false;
        check(fixture.Service.TrySetAlwaysLegendary(false) && fixture.Quality() == QualityCategory.Good,
            "Disabling takes effect immediately after saving");
        check(fixture.Service.PersistenceError.Length == 0, "Successful write clears settings error");

        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("foreign worker", f => SetFaction(f.Worker, Empty<Faction>()), false),
            ("factionless worker", f => SetFaction(f.Worker, null), false),
            ("missing player faction", f => f.PlayerFaction = null, false),
            ("player prisoner", f => f.SetGuest(GuestStatus.Prisoner), false),
            ("foreign guest", f => { f.SetGuest(GuestStatus.Guest); SetFaction(f.Worker, Empty<Faction>()); }, false),
            ("player slave", f => f.SetGuest(GuestStatus.Slave), true),
            ("non-humanlike worker without skills", f => { f.Worker.def.race = Empty<RaceProperties>(); f.Worker.skills = null!; }, true),
            ("no active job", f => f.Worker.jobs.curJob = null!, false),
            ("no active driver", f => f.Worker.jobs.curDriver = null!, false),
            ("different driver job", f => f.Driver.job = Empty<Job>(), false),
            ("different driver pawn", f => f.Driver.pawn = Empty<Pawn>(), false),
            ("different recipe", f => f.Job.bill.recipe = Empty<RecipeDef>(), false),
            ("medical bill", f => f.SetBill(Empty<Bill_Medical>()), false),
            ("autonomous bill", f => f.SetBill(Empty<Bill_Autonomous>()), false),
            ("not a worktable", f => f.Job.targetA = new Thing(), false),
            ("surgery recipe", f => SetSurgery(f.Recipe, true), false),
            ("no work skill", f => f.Recipe.workSkill = null!, false),
            ("no product quality", f =>
            {
                f.Product.compQuality = null!;
                typeof(ThingWithComps).GetField("comps", PrivateInstance)!.SetValue(f.Product, null);
            }, false),
            ("destroyed product", f => Destroy(f.Product), false),
            ("destroyed worker", f => Destroy(f.Worker), false),
            ("destroyed table", f => Destroy(f.Table), false),
            ("destruction recipe", f => f.Recipe.products.Clear(), false),
            ("special-product recipe", f => { f.Recipe.products.Clear(); f.Recipe.specialProducts = new() { SpecialProductType.Smelted }; }, true)
        };
        foreach (var test in cases)
        {
            fixture = new Fixture();
            fixture.Settings.AlwaysLegendaryCrafting = true;
            fixture.Service.SetAvailability(true);
            test.Change(fixture);
            check(fixture.Service.IsEligible(fixture.Product, fixture.Recipe, fixture.Worker) == test.Eligible,
                "Crafting eligibility: " + test.Name);
            check(fixture.Quality() == (test.Eligible ? QualityCategory.Legendary : QualityCategory.Good),
                "Crafting override: " + test.Name);
        }

        fixture = new Fixture();
        fixture.Settings.EnabledLimits["Steel"] = 250000;
        fixture.Settings.OriginalLimits["Steel"] = 83;
        fixture.Settings.PendingResets.Add("WoodLog");
        fixture.Service.SetAvailability(true);
        check(fixture.Service.TrySetAlwaysLegendary(true), "Enable alongside existing stack settings");
        check(fixture.Durable!.EnabledLimits["Steel"] == 250000 && fixture.Durable.OriginalLimits["Steel"] == 83 &&
            fixture.Durable.PendingResets.Contains("WoodLog"), "Crafting writes retain all current stack settings");
        var copy = fixture.Settings.Copy();
        check(copy.Matches(fixture.Settings), "Settings copies include Legendary crafting");
        copy.AlwaysLegendaryCrafting = false;
        check(!copy.Matches(fixture.Settings), "Persistence verification compares Legendary crafting");
        fixture.Settings.CopyFrom(copy);
        check(!fixture.Service.AlwaysLegendary, "CopyFrom includes Legendary crafting");
        ValidateRewrite(check);
    }

    private static void ValidateRewrite(Action<bool, string> check)
    {
        check(LegendaryCraftingPatch.FindTarget() != null && LegendaryCraftingPatch.NativeQualityMethod != null,
            "Resolve exact local 1.7 method signatures");
        var label = new DynamicMethod("Labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        var before = new CodeInstruction(OpCodes.Nop);
        var call = new CodeInstruction(OpCodes.Call, LegendaryCraftingPatch.NativeQualityMethod);
        call.labels.Add(label);
        var begin = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
        var end = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        call.blocks.Add(begin);
        call.blocks.Add(end);
        var original = new[] { before, call, new CodeInstruction(OpCodes.Ret) };
        check(LegendaryCraftingPatch.TryRewrite(original, out var rewritten, out _), "Rewrite one exact quality call");
        check(rewritten.Count == original.Length + 2 && rewritten[1].opcode == OpCodes.Ldarg_0 &&
            rewritten[2].opcode == OpCodes.Ldarg_1 && Equals(rewritten[3].operand, LegendaryCraftingPatch.RedirectMethod),
            "Append product/recipe arguments in the helper's stack order");
        check(rewritten[1].labels.SequenceEqual(new[] { label }) && rewritten[3].labels.Count == 0,
            "Move call branch targets to the first inserted argument");
        check(rewritten[1].blocks.SequenceEqual(new[] { begin }) && rewritten[3].blocks.SequenceEqual(new[] { end }),
            "Preserve begin/end exception boundaries around the redirect");
        check(call.labels.Count == 1 && call.blocks.Count == 2 && Equals(call.operand, LegendaryCraftingPatch.NativeQualityMethod),
            "Rewriting never mutates the input instructions");
        check(rewritten.Count(i => Equals(i.operand, LegendaryCraftingPatch.RedirectMethod)) == 1 &&
            rewritten.All(i => !Equals(i.operand, LegendaryCraftingPatch.NativeQualityMethod)), "One replacement, no duplicate generation call");
        foreach (var incompatible in new[] { new[] { before }, new[] { call, call } })
        {
            check(!LegendaryCraftingPatch.TryRewrite(incompatible, out rewritten, out var reason) && reason.Length > 0,
                "Reject missing/ambiguous IL patterns");
            check(rewritten.SequenceEqual(incompatible), "Incompatible IL remains entirely unchanged");
        }
    }

    public static void RunNative(Action<bool, string> check)
    {
        var target = LegendaryCraftingPatch.FindTarget()!;
        var original = PatchProcessor.GetOriginalInstructions(target);
        check(LegendaryCraftingPatch.TryRewrite(original, out var rewritten, out _), "Rewrite actual RimWorld recipe IL");
        check(rewritten.Count(i => Equals(i.operand, LegendaryCraftingPatch.RedirectMethod)) == 1,
            "Actual game IL contains one redirect");
        var helper = PatchProcessor.GetOriginalInstructions(LegendaryCraftingPatch.RedirectMethod);
        check(helper.Count(i => Equals(i.operand, LegendaryCraftingPatch.NativeQualityMethod)) == 1,
            "Redirect calls native quality generation exactly once");

        var fixture = new Fixture();
        LegendaryCraftingPatch.Install(fixture.Service);
        check(fixture.Service.IsAvailable, "Install Harmony 3 patch: " + fixture.Service.UnavailableReason);
        LegendaryCraftingPatch.Install(fixture.Service);
        check(Harmony.GetPatchInfo(target).Transpilers.Count(p => p.owner == LegendaryCraftingPatch.OwnerId) == 1,
            "Register the production patch once");
        check(Harmony.GetPatchInfo(LegendaryCraftingPatch.NativeQualityMethod)?.Owners.Contains(
            LegendaryCraftingPatch.OwnerId) != true, "Never patch global quality generation");

        // Exercise emitted IL on an isolated method with the same arguments and call.
        // The real GenRecipe method is patched in memory but never run without a map.
        var probe = typeof(CraftingChecks).GetMethod(nameof(Probe), BindingFlags.NonPublic | BindingFlags.Static)!;
        // The native pawn generator initializes ModsConfig and the game's profile.
        // Stub only its call inside OUR helper in this test process, never the global
        // QualityUtility method. All eligibility and emitted redirect IL remain real.
        new Harmony(LegendaryCraftingPatch.OwnerId + ".tests.generator").Patch(
            LegendaryCraftingPatch.RedirectMethod,
            transpiler: new HarmonyMethod(typeof(CraftingChecks), nameof(StubNativeGeneration)));
        new Harmony(LegendaryCraftingPatch.OwnerId + ".tests").Patch(probe,
            transpiler: new HarmonyMethod(typeof(LegendaryCraftingPatch), "Transpiler"));
        foreach (var enabled in new[] { false, true, false })
        {
            check(fixture.Service.TrySetAlwaysLegendary(enabled), "Change live crafting setting");
            var callbacks = fixture.Product.QualityCallbacks;
            var calls = generationCalls;
            Probe(fixture.Product, fixture.Recipe, fixture.Worker, consumeInspiration: enabled);
            check(fixture.Product.compQuality.Quality == (enabled ? QualityCategory.Legendary : QualityCategory.Good),
                "Patched quality follows enabled/disabled setting");
            check(generationCalls == calls + 1 && ReferenceEquals(generatedBy, fixture.Worker) &&
                ReferenceEquals(generatedSkill, fixture.Recipe.workSkill) && consumedInspiration == enabled,
                "Redirect calls generation once with the original worker, skill and inspiration argument");
            check(fixture.Product.QualityCallbacks == callbacks + 1, "Native quality-setting callback runs once");
        }
        check(fixture.Service.TrySetAlwaysLegendary(true), "Enable for foreign-worker probe");
        SetFaction(fixture.Worker, Empty<Faction>());
        Probe(fixture.Product, fixture.Recipe, fixture.Worker);
        check(fixture.Product.compQuality.Quality == QualityCategory.Good, "Patched call leaves NPC quality unchanged");

        SetFaction(fixture.Worker, fixture.PlayerFaction);
        var transpiler = typeof(LegendaryCraftingPatch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var incompatible in new[]
        {
            new List<CodeInstruction>(),
            new List<CodeInstruction>
            {
                new(OpCodes.Call, LegendaryCraftingPatch.NativeQualityMethod),
                new(OpCodes.Call, LegendaryCraftingPatch.NativeQualityMethod)
            }
        })
        {
            var unchanged = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { incompatible })!;
            check(!fixture.Service.IsAvailable && fixture.Service.UnavailableReason.Contains("found") &&
                unchanged.SequenceEqual(incompatible), "Incompatible transpiler disables the feature without changing IL");
        }
        Probe(fixture.Product, fixture.Recipe, fixture.Worker);
        check(fixture.Product.compQuality.Quality == QualityCategory.Good,
            "Unavailable patch state blocks an already-emitted redirect");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Thing Probe(Thing product, RecipeDef recipe, Pawn worker,
        Precept_ThingStyle? precept = null, ThingStyleDef? style = null, int? graphicIndex = null,
        bool consumeInspiration = true)
    {
        var quality = QualityUtility.GenerateQualityCreatedByPawn(worker, recipe.workSkill, consumeInspiration);
        product.TryGetComp<CompQuality>().SetQuality(quality, source: null);
        return product;
    }

    private static IEnumerable<CodeInstruction> StubNativeGeneration(IEnumerable<CodeInstruction> instructions)
    {
        var stub = typeof(CraftingChecks).GetMethod(nameof(CountGeneration), BindingFlags.Static | BindingFlags.NonPublic)!;
        return instructions.Select(instruction =>
        {
            var copy = new CodeInstruction(instruction);
            if (copy.opcode == OpCodes.Call && Equals(copy.operand, LegendaryCraftingPatch.NativeQualityMethod))
            {
                copy.operand = stub;
            }
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

    internal sealed class Fixture
    {
        public readonly CustomStackLimitSettings Settings = new();
        public readonly Pawn Worker = Empty<Pawn>();
        public readonly CraftItem Product = new();
        public readonly RecipeDef Recipe = Empty<RecipeDef>();
        public readonly Building_WorkTable Table = Empty<Building_WorkTable>();
        public readonly Job Job = Empty<Job>();
        public readonly JobDriver_DoBill Driver = Empty<JobDriver_DoBill>();
        public readonly CraftingService Service;
        public Faction? PlayerFaction = Empty<Faction>();
        public CustomStackLimitSettings? Durable;
        public bool FailWrites;
        public bool EnabledDuringWrite;
        public int Writes;

        public Fixture()
        {
            Worker.def = Empty<ThingDef>();
            SetFaction(Worker, PlayerFaction);
            Product.def = Empty<ThingDef>();
            Product.def.category = ThingCategory.Item;
            Product.compQuality = new CompQuality { parent = Product };
            typeof(ThingWithComps).GetField("comps", PrivateInstance)!.SetValue(Product,
                new List<ThingComp> { Product.compQuality });
            Recipe.workSkill = Empty<SkillDef>();
            Recipe.products = new() { new ThingDefCountClass { thingDef = Product.def, count = 1 } };
            SetSurgery(Recipe, false);
            SetBill(Empty<Bill_Production>());
            Job.targetA = Table;
            Driver.pawn = Worker;
            Driver.job = Job;
            Worker.jobs = Empty<Pawn_JobTracker>();
            Worker.jobs.curJob = Job;
            Worker.jobs.curDriver = Driver;
            Service = new CraftingService(Settings, candidate =>
            {
                if (FailWrites) throw new IOException("Simulated crafting settings failure");
                EnabledDuringWrite = Settings.AlwaysLegendaryCrafting;
                Durable = candidate.Copy();
                Writes++;
            }, () => PlayerFaction);
        }

        public void SetBill(Bill bill) { bill.recipe = Recipe; Job.bill = bill; }
        public void SetGuest(GuestStatus status)
        {
            Worker.guest = Empty<Pawn_GuestTracker>();
            Worker.guest.guestStatusInt = status;
        }
        public QualityCategory Quality() => Service.ResolveQuality(QualityCategory.Good, Product, Recipe, Worker);
    }

    internal sealed class CraftItem : ThingWithComps
    {
        public int QualityCallbacks;
        public override void PostQualitySet() => QualityCallbacks++;
    }

    internal static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    internal static void SetFaction(Thing thing, Faction? faction) =>
        typeof(Thing).GetField("factionInt", PrivateInstance)!.SetValue(thing, faction);
    internal static void SetSurgery(RecipeDef recipe, bool value) =>
        typeof(RecipeDef).GetField("isSurgeryCached", PrivateInstance)!.SetValue(recipe, value);
    internal static void Destroy(Thing thing) =>
        typeof(Thing).GetField("mapIndexOrState", PrivateInstance)!.SetValue(thing, (sbyte)-2);
}
