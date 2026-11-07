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

internal static class ResearchSpeedPatch
{
    internal const string OwnerId = "slava.rimworldutilities.research.speed";
    internal static readonly MethodInfo? NativeMethod = typeof(ResearchManager).GetMethod(nameof(ResearchManager.ResearchPerformed),
        new[] { typeof(float), typeof(Pawn) });
    internal static readonly MethodInfo RedirectMethod = typeof(ResearchSpeedPatch).GetMethod(
        nameof(PerformResearch), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static bool attempted;
    private static bool warned;
    private static ResearchSpeedService? service;

    internal static MethodInfo? FindIterator()
    {
        var factory = typeof(JobDriver_Research).GetMethod("MakeNewToils",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return factory != null ? AccessTools.EnumeratorMoveNext(factory) : null;
    }

    internal static void Install(ResearchSpeedService research)
    {
        if (attempted) return;
        attempted = true;
        service = research;
        try
        {
            var iterator = FindIterator();
            if (NativeMethod == null || NativeMethod.IsStatic || NativeMethod.ReturnType != typeof(void) ||
                iterator == null || iterator.ReturnType != typeof(bool) || iterator.GetParameters().Length != 0)
                throw new MissingMethodException("The expected ordinary research methods were not found.");
            if (!CraftingSpeedPatch.TryResolveCallback(PatchProcessor.GetOriginalInstructions(iterator), out var callback, out var error))
            {
                SetUnavailable(error);
                return;
            }
            new Harmony(OwnerId).Patch(callback, transpiler: new HarmonyMethod(typeof(ResearchSpeedPatch), nameof(Transpiler)));
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not install research speed: {exception.GetBaseException().Message}");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        if (!TryRewrite(original, out var rewritten, out var error))
        {
            SetUnavailable(error);
            return original;
        }
        service?.SetAvailability(true);
        return rewritten;
    }

    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        if (NativeMethod == null || original.Count(i => i.Calls(NativeMethod)) != 1 || original.Any(i => i.Calls(RedirectMethod)))
        {
            rewritten = original.ToList();
            error = "Expected exactly one ordinary ResearchPerformed call. The game version or another mod changed the research callback.";
            return false;
        }

        rewritten = new List<CodeInstruction>(original.Count);
        foreach (var instruction in original)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.Calls(NativeMethod))
            {
                // Same stack arguments: manager, amount, actual actor. No changes
                // to rate/delta calculation, XP, comfort or manager completion logic.
                copy.opcode = OpCodes.Call;
                copy.operand = RedirectMethod;
            }
            rewritten.Add(copy);
        }
        error = string.Empty;
        return true;
    }

    private static void PerformResearch(ResearchManager manager, float amount, Pawn researcher) =>
        manager.ResearchPerformed(service?.ScaleWork(amount, manager, researcher) ?? amount, researcher);

    private static void SetUnavailable(string reason)
    {
        service?.SetAvailability(false, reason);
        if (warned) return;
        warned = true;
        Log.Warning($"[RimWorldUtilities] Research speed is unavailable: {reason}");
    }
}
