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

internal static class ResearchSpeedChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static int xpTicks, recordCalls, completions;
    private static float xp, recordedPoints;
    private static bool completionDialog;
    private static Pawn? finisher;

    public static void RunCore(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        check(f.Service.SpeedMultiplier == 1 && f.Crafting.Settings.ResearchSpeedMultiplier == 1, "Research defaults to 1x");
        check(!f.Service.IsAvailable && !f.Service.TrySetSpeedMultiplier(20), "Unavailable research cannot enable");
        f.Service.SetAvailability(true);
        check(ResearchSpeedService.SpeedMultipliers.SequenceEqual(new[] { 1, 2, 3, 5, 10, 20 }), "Exact research choices");
        foreach (var multiplier in ResearchSpeedService.SpeedMultipliers)
        {
            check(f.Service.TrySetSpeedMultiplier(multiplier), "Select research multiplier " + multiplier);
            check(f.Service.ScaleWork(6.25f, f.Manager, f.Worker) == 6.25f * multiplier, "Research work scaling " + multiplier);
        }
        foreach (var invalid in new[] { -1, 0, 4, 7, 21, int.MaxValue })
            check(!f.Service.TrySetSpeedMultiplier(invalid) && f.Service.SpeedMultiplier == 20, "Reject research multiplier " + invalid);
        f.Crafting.FailWrites = true;
        check(!f.Service.TrySetSpeedMultiplier(2) && f.Service.SpeedMultiplier == 20 && f.Service.PersistenceError.Length > 0,
            "Failed research save retains active preference");
        f.Crafting.FailWrites = false;
        check(f.Service.TrySetSpeedMultiplier(1) && f.Service.PersistenceError.Length == 0, "Return research to normal");
        foreach (var work in new[] { 0f, -0f, 1.2345f, float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity })
            check(BitConverter.SingleToInt32Bits(f.Service.ScaleWork(work, null!, null!)) == BitConverter.SingleToInt32Bits(work),
                "Research 1x preserves native float bits without context lookup");
        f.Crafting.Settings.ResearchSpeedMultiplier = 99;
        check(f.Service.SpeedMultiplier == 1 && f.Service.ScaleWork(3f, null!, null!) == 3f, "Invalid saved research value falls back to 1x");
        f.Service.TrySetSpeedMultiplier(1);
        check(f.Crafting.Settings.ResearchSpeedMultiplier == 1, "Explicit 1x repairs invalid research preference");

        var cases = new (string Name, Action<Fixture> Change, bool Eligible)[]
        {
            ("ordinary player research", _ => { }, true),
            ("foreign faction", x => CraftingChecks.SetFaction(x.Worker, CraftingChecks.Empty<Faction>()), false),
            ("factionless", x => CraftingChecks.SetFaction(x.Worker, null), false),
            ("missing player faction", x => x.Crafting.PlayerFaction = null, false),
            ("prisoner", x => x.Crafting.SetGuest(GuestStatus.Prisoner), false),
            ("player slave", x => x.Crafting.SetGuest(GuestStatus.Slave), true),
            ("worker without humanlike/skill requirement", x => x.Worker.skills = null!, true),
            ("destroyed worker", x => CraftingChecks.Destroy(x.Worker), false),
            ("no project", x => SetProject(x.Manager, null), false),
            ("Anomaly category", x => x.Project.knowledgeCategory = CraftingChecks.Empty<KnowledgeCategoryDef>(), false),
            ("knowledge cost", x => x.Project.knowledgeCost = 10f, false),
            ("non-work project", x => x.Project.baseCost = 0f, false),
            ("destroyed bench", x => CraftingChecks.Destroy(x.Bench), false),
            ("unspawned bench", x => SetThing(x.Bench, "mapIndexOrState", (sbyte)-1), false),
            ("unrelated target", x => x.Driver.job.targetA = CraftingChecks.Empty<Building_WorkTable>(), false),
            ("no current job", x => x.Worker.jobs.curJob = null!, false),
            ("changed job", x => x.Driver.job = CraftingChecks.Empty<Job>(), false),
            ("different worker", x => x.Driver.pawn = CraftingChecks.Empty<Pawn>(), false),
            ("no driver", x => x.Worker.jobs.curDriver = null!, false),
            ("no jobs", x => x.Worker.jobs = null!, false),
            ("crafting", x => x.Worker.jobs.curDriver = x.Crafting.Driver, false),
            ("study item", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_StudyItem>(), false),
            ("study entity", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_StudyInteract>(), false),
            ("use schematic", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<JobDriver_UseItemResearchBench>(), false),
            ("special research subclass", x => x.Worker.jobs.curDriver = CraftingChecks.Empty<SpecialDriver>(), false)
        };
        foreach (var test in cases)
        {
            f = new Fixture();
            f.Service.SetAvailability(true);
            f.Service.TrySetSpeedMultiplier(20);
            test.Change(f);
            check(f.Service.IsEligible(f.Manager, f.Worker) == test.Eligible, "Research eligibility: " + test.Name);
            check(f.Service.ScaleWork(2f, f.Manager, f.Worker) == (test.Eligible ? 40f : 2f), "Research effect: " + test.Name);
        }
        f = new Fixture();
        f.Crafting.Service.SetAvailability(true);
        f.Crafting.Service.SetSpeedAvailability(true);
        f.Crafting.Service.SetBuildingSpeedAvailability(true);
        f.Crafting.Service.TrySetAlwaysLegendary(true);
        f.Crafting.Service.TrySetSpeedMultiplier(3);
        f.Crafting.Service.TrySetBuildingSpeedMultiplier(5);
        f.Crafting.Settings.EnabledColonistNeeds.UnionWith(new[] { "Food", "Learning", "Play" });
        f.Crafting.Settings.EnabledLimits["Steel"] = 250000;
        f.Crafting.Settings.OriginalLimits["Steel"] = 75;
        f.Crafting.Settings.PendingResets.Add("WoodLog");
        f.Service.SetAvailability(true);
        f.Service.TrySetSpeedMultiplier(10);
        check(f.Durable!.AlwaysLegendaryCrafting && f.Durable.CraftingSpeedMultiplier == 3 && f.Durable.BuildingSpeedMultiplier == 5 &&
            f.Durable.EnabledColonistNeeds.SetEquals(new[] { "Food", "Learning", "Play" }) && f.Durable.EnabledLimits["Steel"] == 250000 &&
            f.Durable.OriginalLimits["Steel"] == 75 && f.Durable.PendingResets.Contains("WoodLog"), "Research writes retain all existing settings");
        var copy = f.Crafting.Settings.Copy();
        check(copy.Matches(f.Crafting.Settings) && copy.ResearchSpeedMultiplier == 10, "Research included in settings copy");
        copy.ResearchSpeedMultiplier = 20;
        check(!copy.Matches(f.Crafting.Settings), "Research included in persistence verification");
        f.Crafting.Settings.CopyFrom(copy);
        check(f.Service.SpeedMultiplier == 20, "CopyFrom restores research preference");
        f.Crafting.Service.SetAvailability(false);
        f.Crafting.Service.SetSpeedAvailability(false);
        f.Crafting.Service.SetBuildingSpeedAvailability(false);
        check(f.Service.ScaleWork(2f, f.Manager, f.Worker) == 40f, "Research unaffected by other patch failures");
        f.Crafting.Service.SetAvailability(true);
        f.Crafting.Service.SetSpeedAvailability(true);
        f.Crafting.Service.SetBuildingSpeedAvailability(true);
        f.Service.SetAvailability(false, "Research mismatch");
        check(f.Service.ScaleWork(2f, f.Manager, f.Worker) == 2f && f.Crafting.Service.AlwaysLegendary &&
            f.Crafting.Service.SpeedMultiplier == 3 && f.Crafting.Service.BuildingSpeedMultiplier == 5 &&
            f.Crafting.Service.IsAvailable && f.Crafting.Service.IsSpeedAvailable && f.Crafting.Service.IsBuildingSpeedAvailable,
            "Research failure retains other preferences and availability");
        ValidatePatterns(check);
    }

    private static MethodInfo Resolve(Action<bool, string> check)
    {
        var iterator = ResearchSpeedPatch.FindIterator();
        check(iterator?.ReturnType == typeof(bool), "Resolve research iterator without generated names");
        var il = PatchProcessor.GetOriginalInstructions(iterator!);
        check(CraftingSpeedPatch.TryResolveCallback(il, out var callback, out _), "Resolve research interval delegate");
        check(!CraftingSpeedPatch.TryResolveCallback(il.Concat(il).ToList(), out _, out _), "Reject ambiguous research delegates");
        return callback!;
    }

    private static void ValidatePatterns(Action<bool, string> check)
    {
        var target = Resolve(check);
        var original = PatchProcessor.GetOriginalInstructions(target);
        check(HasNativeResearchAndXp(original), "Native research callback includes contribution and Intellectual XP calls");
        check(ResearchSpeedPatch.TryRewrite(original, out var rewritten, out var error), "Match installed research call: " + error);
        check(rewritten.Count == original.Count && rewritten.Count(i => i.Calls(ResearchSpeedPatch.RedirectMethod)) == 1,
            "One same-stack research call redirect");
        check(original.Zip(rewritten).All(pair => pair.First.Calls(ResearchSpeedPatch.NativeMethod!) ||
            (pair.First.opcode == pair.Second.opcode && Equals(pair.First.operand, pair.Second.operand))), "Preserve all native stats/delta/XP/comfort instructions");
        var label = new DynamicMethod("ResearchLabels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        var call = new CodeInstruction(OpCodes.Callvirt, ResearchSpeedPatch.NativeMethod);
        call.labels.Add(label);
        call.blocks.AddRange(new[] { new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock), new ExceptionBlock(ExceptionBlockType.EndExceptionBlock) });
        check(ResearchSpeedPatch.TryRewrite(new[] { call }, out rewritten, out _) && rewritten[0].labels.SequenceEqual(call.labels) &&
            rewritten[0].blocks.SequenceEqual(call.blocks) && call.opcode == OpCodes.Callvirt, "Preserve branch/EH metadata and source instructions");
        foreach (var invalid in new[] { new List<CodeInstruction>(), original.Concat(original).ToList(), rewritten })
        {
            check(!ResearchSpeedPatch.TryRewrite(invalid, out var unchanged, out error) && error.Length > 0, "Reject absent/duplicate/already redirected research calls");
            check(unchanged.SequenceEqual(invalid), "Leave rejected research IL entirely unchanged");
        }
    }
    private static bool HasNativeResearchAndXp(List<CodeInstruction> il) => il.Any(i => i.Calls(ResearchSpeedPatch.NativeMethod!)) &&
        il.Any(i => i.operand is MethodInfo m && m.DeclaringType == typeof(Pawn_SkillTracker) && m.Name == nameof(Pawn_SkillTracker.Learn));

    public static void RunNative(Action<bool, string> check)
    {
        using var scope = new Scope();
        var f = new Fixture();
        Current.Game.researchManager = f.Manager;
        ResearchSpeedPatch.Install(f.Service);
        check(f.Service.IsAvailable, "Install actual research callback patch: " + f.Service.UnavailableReason);
        ResearchSpeedPatch.Install(f.Service);
        var target = Resolve(check);
        check(Harmony.GetPatchInfo(target).Transpilers.Count(p => p.owner == ResearchSpeedPatch.OwnerId) == 1, "Register research once");
        check(Harmony.GetPatchInfo(ResearchSpeedPatch.NativeMethod!)?.Owners.Contains(ResearchSpeedPatch.OwnerId) != true,
            "No global ResearchPerformed prefix or transpiler");
        foreach (var method in typeof(ResearchManager).GetMethods().Where(m => m.Name is "AddProgress" or "ApplyKnowledge" or "FinishProject"))
            check(Harmony.GetPatchInfo(method)?.Owners.Contains(ResearchSpeedPatch.OwnerId) != true, "Non-work/specialized research API untouched: " + method.Name);
        var probe = typeof(ResearchSpeedChecks).GetMethod(nameof(WorkProbe), PrivateStatic)!;
        var harmony = new Harmony(ResearchSpeedPatch.OwnerId + ".tests");
        var records = typeof(Pawn_RecordsTracker).GetMethod(nameof(Pawn_RecordsTracker.AddTo))!;
        var finish = typeof(ResearchManager).GetMethod(nameof(ResearchManager.FinishProject))!;
        harmony.Patch(records, prefix: new HarmonyMethod(typeof(ResearchSpeedChecks), nameof(CaptureRecord)));
        harmony.Patch(finish, prefix: new HarmonyMethod(typeof(ResearchSpeedChecks), nameof(CaptureFinish)));
        harmony.Patch(probe, transpiler: new HarmonyMethod(typeof(ResearchSpeedPatch), "Transpiler"));
        try
        {
            foreach (var multiplier in ResearchSpeedService.SpeedMultipliers)
            {
                Reset(f);
                f.Service.TrySetSpeedMultiplier(multiplier);
                WorkProbe(f.Manager, 8f, f.Worker, 4);
                var expected = 8f * multiplier * 0.00825f * 1.5f;
                check(Close(f.Manager.GetProgress(f.Project), expected) && recordCalls == 1 && Close(recordedPoints, expected),
                    "One native ledger/record contribution at " + multiplier);
                check(xpTicks == 4 && xp == 0.4f && completions == 0 && f.Project.baseCost == 100f,
                    "Research speed leaves XP, project costs and unfinished state unchanged");
            }
            Reset(f);
            f.Service.TrySetSpeedMultiplier(20);
            f.Manager.ResearchPerformed(8f, f.Worker);
            check(Close(f.Manager.GetProgress(f.Project), 8f * 0.00825f * 1.5f) && recordCalls == 1,
                "Direct non-work ResearchPerformed stays unscaled even while pawn has a research job");
            var before = f.Manager.GetProgress(f.Project);
            f.Manager.AddProgress(f.Project, 0.25f);
            check(Close(f.Manager.GetProgress(f.Project), before + 0.25f), "Native book AddProgress stays unscaled");

            Reset(f);
            f.Project.techLevel = TechLevel.Industrial;
            f.Crafting.PlayerFaction!.def.techLevel = TechLevel.Neolithic;
            WorkProbe(f.Manager, 4f, f.Worker, 2);
            check(Close(f.Manager.GetProgress(f.Project), 4f * 20f * 0.00825f * 1.5f / f.Project.CostFactor(TechLevel.Neolithic)),
                "Native faction tech-level cost factor is preserved");
            f.Project.techLevel = TechLevel.Undefined;
            f.Crafting.PlayerFaction.def.techLevel = TechLevel.Industrial;
            Reset(f);
            DebugSettings.fastResearch = true;
            WorkProbe(f.Manager, 0.1f, f.Worker, 1);
            check(Close(f.Manager.GetProgress(f.Project), 0.1f * 20f * 0.00825f * 1.5f * 500f),
                "Native fast-research factor composes only on work contributions");
            DebugSettings.fastResearch = false;

            Reset(f);
            var other = new Fixture();
            CraftingChecks.SetFaction(other.Worker, f.Crafting.PlayerFaction);
            WorkProbe(f.Manager, 4f, f.Worker, 2);
            WorkProbe(f.Manager, 6f, other.Worker, 3);
            check(Close(f.Manager.GetProgress(f.Project), (4f + 6f) * 20f * 0.00825f * 1.5f) && recordCalls == 2 && xpTicks == 5,
                "Two simultaneous researchers contribute independently without double scaling");
            Reset(f);
            f.Crafting.SetGuest(GuestStatus.Prisoner);
            WorkProbe(f.Manager, 4f, f.Worker, 2);
            check(Close(f.Manager.GetProgress(f.Project), 4f * 0.00825f * 1.5f), "Patched contribution excludes prisoners");
            f.Worker.guest = null!;
            Reset(f);
            f.Project.knowledgeCategory = CraftingChecks.Empty<KnowledgeCategoryDef>();
            WorkProbe(f.Manager, 4f, f.Worker, 2);
            check(Close(f.Manager.GetProgress(f.Project), 4f * 0.00825f * 1.5f), "Specialized project is never scaled");
            f.Project.knowledgeCategory = null!;
            Reset(f);
            f.Project.baseCost = 2f;
            WorkProbe(f.Manager, 10f, f.Worker, 1);
            check(completions == 1 && completionDialog && ReferenceEquals(finisher, f.Worker) && f.Manager.GetProject() == null &&
                f.Manager.GetProgress(f.Project) == 2f && xpTicks == 1, "Native manager dispatches completion exactly once to the real worker");
            Reset(f);
            f.Service.SetAvailability(true);
            var transpiler = typeof(ResearchSpeedPatch).GetMethod("Transpiler", PrivateStatic)!;
            var invalid = new List<CodeInstruction>();
            var untouched = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { invalid })!;
            check(!f.Service.IsAvailable && untouched.SequenceEqual(invalid) && f.Service.UnavailableReason.Length > 0, "Incompatible research patch disables safely");
            WorkProbe(f.Manager, 4f, f.Worker, 2);
            check(Close(f.Manager.GetProgress(f.Project), 4f * 0.00825f * 1.5f) && recordCalls == 1,
                "Unavailable state preserves native contribution through an existing redirect");
        }
        finally
        {
            harmony.Unpatch(records, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(finish, HarmonyPatchType.Prefix, harmony.Id);
        }
    }

    private static bool CaptureRecord(RecordDef def, float value) { recordCalls++; recordedPoints += value; return false; }
    private static bool CaptureFinish(ResearchManager __instance, ResearchProjectDef proj, bool doCompletionDialog, Pawn researcher)
    {
        completions++;
        completionDialog = doCompletionDialog;
        finisher = researcher;
        // Only completion's UI/world side effects are isolated. ResearchPerformed
        // and its native completion decision run unchanged in the real manager.
        var progress = (Dictionary<ResearchProjectDef, float>)typeof(ResearchManager).GetField("progress", PrivateInstance)!.GetValue(__instance)!;
        progress[proj] = proj.baseCost;
        SetProject(__instance, null);
        return false;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WorkProbe(ResearchManager manager, float amount, Pawn worker, int delta)
    {
        manager.ResearchPerformed(amount, worker);
        xpTicks += delta;
        xp += 0.1f * delta;
    }
    private static void Reset(Fixture f)
    {
        f.Project.baseCost = 100f;
        SetProject(f.Manager, f.Project);
        typeof(ResearchManager).GetField("progress", PrivateInstance)!.SetValue(f.Manager, new Dictionary<ResearchProjectDef, float>());
        xpTicks = recordCalls = completions = 0;
        xp = recordedPoints = 0f;
        completionDialog = false;
        finisher = null;
    }
    private static bool Close(float a, float b) => Math.Abs(a - b) < 0.00001f;
    private static void SetProject(ResearchManager manager, ResearchProjectDef? project) =>
        typeof(ResearchManager).GetField("currentProj", PrivateInstance)!.SetValue(manager, project);
    private static void SetThing(Thing thing, string field, object value) => typeof(Thing).GetField(field, PrivateInstance)!.SetValue(thing, value);

    private sealed class Fixture
    {
        public readonly CraftingChecks.Fixture Crafting = new();
        public Pawn Worker => Crafting.Worker;
        public readonly Building_ResearchBench Bench = CraftingChecks.Empty<Building_ResearchBench>();
        public readonly JobDriver_Research Driver = CraftingChecks.Empty<JobDriver_Research>();
        public readonly ResearchManager Manager = new();
        public readonly ResearchProjectDef Project = CraftingChecks.Empty<ResearchProjectDef>();
        public readonly ResearchSpeedService Service;
        public CustomStackLimitSettings? Durable;
        public Fixture()
        {
            Crafting.PlayerFaction!.def = CraftingChecks.Empty<FactionDef>();
            Crafting.PlayerFaction.def.techLevel = TechLevel.Industrial;
            Worker.records = CraftingChecks.Empty<Pawn_RecordsTracker>();
            Bench.def = CraftingChecks.Empty<ThingDef>();
            SetThing(Bench, "mapIndexOrState", (sbyte)0);
            Crafting.Job.targetA = Bench;
            Driver.job = Crafting.Job;
            Driver.pawn = Worker;
            Worker.jobs.curDriver = Driver;
            Project.baseCost = 100f;
            SetProject(Manager, Project);
            Service = new ResearchSpeedService(Crafting.Settings, candidate =>
            {
                if (Crafting.FailWrites) throw new IOException("Simulated research save failure");
                Durable = candidate.Copy();
            }, Crafting.Service.IsEligibleWorker);
        }
    }
    private sealed class SpecialDriver : JobDriver_Research { }
    private sealed class Scope : IDisposable
    {
        private readonly Game previous = Current.Game;
        private readonly RecordDef previousRecord;
        private readonly bool previousFast = DebugSettings.fastResearch;
        public Scope()
        {
            var binding = typeof(DefOfHelper).GetField("bindingNow", PrivateStatic)!;
            var previousBinding = binding.GetValue(null);
            try { binding.SetValue(null, true); RuntimeHelpers.RunClassConstructor(typeof(RecordDefOf).TypeHandle); }
            finally { binding.SetValue(null, previousBinding); }
            previousRecord = RecordDefOf.ResearchPointsResearched;
            RecordDefOf.ResearchPointsResearched = CraftingChecks.Empty<RecordDef>();
            DebugSettings.fastResearch = false;
            var game = CraftingChecks.Empty<Game>();
            typeof(Game).GetField("maps", PrivateInstance)!.SetValue(game, new List<Map> { CraftingChecks.Empty<Map>() });
            game.storyteller = CraftingChecks.Empty<Storyteller>();
            game.storyteller.difficulty = CraftingChecks.Empty<Difficulty>();
            game.storyteller.difficulty.researchSpeedFactor = 1.5f;
            Current.Game = game;
        }
        public void Dispose()
        {
            Current.Game = previous;
            RecordDefOf.ResearchPointsResearched = previousRecord;
            DebugSettings.fastResearch = previousFast;
        }
    }
}
