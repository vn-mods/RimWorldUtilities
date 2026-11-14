using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Services;
using Verse;

namespace RimWorldUtilities.Patches;

internal static class BiosculpterAgeReversalPatch
{
    internal const string OwnerId = "slava.rimworldutilities.biosculpter.age";
    internal static readonly MethodInfo AgeSetter = AccessTools.PropertySetter(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeBiologicalTicks));
    internal static readonly MethodInfo ResetDemand = AccessTools.DeclaredMethod(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.ResetAgeReversalDemand),
        new[] { typeof(Pawn_AgeTracker.AgeReversalReason), typeof(bool) });
    internal static readonly MethodInfo AdjustTarget = AccessTools.DeclaredMethod(typeof(BiosculpterAgeReversalPatch), nameof(TargetAge));
    private static bool attempted;
    private static bool failed;
    private static BiosculpterCycleService? service;

    internal static MethodInfo? FindTarget() => typeof(CompBiosculpterPod_AgeReversalCycle).GetMethod(
        nameof(CompBiosculpterPod_AgeReversalCycle.CycleCompleted), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
        null, new[] { typeof(Pawn) }, null);

    internal static void Install(BiosculpterCycleService cycles)
    {
        if (attempted) return;
        attempted = true;
        service = cycles;
        var harmony = new Harmony(OwnerId);
        try
        {
            var target = FindTarget();
            if (target?.ReturnType != typeof(void) || target.IsStatic || AgeSetter == null || ResetDemand == null)
                throw new MissingMethodException("The expected biosculpter age-reversal APIs were not found.");
            if (!TryRewrite(PatchProcessor.GetOriginalInstructions(target), out _, out var error))
                throw new InvalidOperationException(error);
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(BiosculpterAgeReversalPatch), nameof(Transpiler)));
            if (failed) throw new InvalidOperationException(cycles.AgeReversalUnavailableReason);
            cycles.SetAgeReversalAvailability(true);
        }
        catch (Exception exception)
        {
            Disable($"Could not install age reversal: {exception.GetBaseException().Message}");
            harmony.UnpatchAll(OwnerId);
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        if (TryRewrite(original, out var rewritten, out var error)) return rewritten;
        Disable(error);
        return original;
    }

    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> original, out List<CodeInstruction> rewritten, out string error)
    {
        var setterIndex = -1;
        var resetIndex = -1;
        for (var i = 0; i < original.Count; i++)
        {
            if (original[i].Calls(AgeSetter)) setterIndex = i;
            if (original[i].Calls(ResetDemand)) resetIndex = i;
        }
        if (original.Count(i => i.Calls(AgeSetter)) != 1 || original.Count(i => i.Calls(ResetDemand)) != 1 ||
            setterIndex >= resetIndex || original.Any(i => i.Calls(AdjustTarget)))
        {
            rewritten = original.ToList();
            error = "Expected one biological-age setter before one age-reversal demand reset. The game version or another mod changed completion.";
            return false;
        }
        rewritten = new List<CodeInstruction>(original.Count + 3);
        foreach (var instruction in original)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.Calls(AgeSetter))
            {
                // Keep the original tracker and proposed age on the stack. Read
                // the original biological age before its sole native assignment,
                // then let native growth/life-stage updates, scheduling and the
                // completion message observe the final value exactly once.
                var loadCycle = new CodeInstruction(OpCodes.Ldarg_0);
                loadCycle.labels.AddRange(copy.labels);
                loadCycle.blocks.AddRange(copy.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
                copy.labels.Clear();
                copy.blocks.RemoveAll(b => b.blockType != ExceptionBlockType.EndExceptionBlock);
                rewritten.Add(loadCycle);
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_1));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AdjustTarget));
            }
            rewritten.Add(copy);
        }
        error = string.Empty;
        return true;
    }

    private static long TargetAge(long proposedAge, CompBiosculpterPod_AgeReversalCycle cycle, Pawn pawn) =>
        service?.AgeReversalTarget(cycle, pawn, proposedAge) ?? proposedAge;

    private static void Disable(string reason)
    {
        service?.SetAgeReversalAvailability(false, reason);
        if (!failed) Log.Warning($"[RimWorldUtilities] Enhanced age reversal is unavailable: {reason}");
        failed = true;
    }
}
