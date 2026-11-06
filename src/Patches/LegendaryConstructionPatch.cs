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

internal static class LegendaryConstructionPatch
{
    internal const string OwnerId = "slava.rimworldutilities.construction.legendary";
    private static bool attempted;
    private static bool warned;
    private static CraftingService? service;

    internal static MethodInfo? NativeQualityMethod => LegendaryCraftingPatch.NativeQualityMethod;
    internal static readonly MethodInfo RedirectMethod = typeof(LegendaryConstructionPatch).GetMethod(
        nameof(GenerateQuality), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static MethodInfo? FindTarget() => typeof(Frame).GetMethod(nameof(Frame.CompleteConstruction),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null,
        new[] { typeof(Pawn) }, null);

    internal static void Install(CraftingService crafting)
    {
        if (attempted) return;
        attempted = true;
        service = crafting;
        try
        {
            var target = FindTarget();
            if (target == null || target.ReturnType != typeof(void) || target.IsStatic ||
                NativeQualityMethod?.ReturnType != typeof(QualityCategory))
            {
                SetUnavailable("The expected RimWorld construction quality methods were not found.");
                return;
            }

            new Harmony(OwnerId).Patch(target, transpiler: new HarmonyMethod(
                typeof(LegendaryConstructionPatch), nameof(Transpiler)));
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not install the construction quality patch: {exception.GetBaseException().Message}");
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

        service?.SetConstructionAvailability(true);
        return rewritten;
    }

    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        var matches = original.Count(instruction => instruction.opcode == OpCodes.Call &&
            NativeQualityMethod != null && NativeQualityMethod.Equals(instruction.operand));
        if (matches != 1 || original.Any(instruction => RedirectMethod.Equals(instruction.operand)))
        {
            rewritten = original.ToList();
            error = $"Expected exactly one unmodified construction quality-generation call; found {matches}. " +
                "The game version or another mod changed this method.";
            return false;
        }

        rewritten = new List<CodeInstruction>(original.Count + 1);
        foreach (var instruction in original)
        {
            if (instruction.opcode != OpCodes.Call || !NativeQualityMethod!.Equals(instruction.operand))
            {
                rewritten.Add(new CodeInstruction(instruction));
                continue;
            }

            // Original stack: worker, skill, consumeInspiration. Append this frame;
            // no generated local indices or replacement of SetQuality are needed.
            var frame = new CodeInstruction(OpCodes.Ldarg_0);
            frame.labels.AddRange(instruction.labels);
            frame.blocks.AddRange(instruction.blocks.Where(block =>
                block.blockType != ExceptionBlockType.EndExceptionBlock));
            rewritten.Add(frame);
            var redirect = new CodeInstruction(OpCodes.Call, RedirectMethod);
            redirect.blocks.AddRange(instruction.blocks.Where(block =>
                block.blockType == ExceptionBlockType.EndExceptionBlock));
            rewritten.Add(redirect);
        }

        error = string.Empty;
        return true;
    }

    private static QualityCategory GenerateQuality(Pawn worker, SkillDef relevantSkill, bool consumeInspiration,
        Thing frame)
    {
        // Keep native RNG, ideology effects and inspiration consumption exactly once.
        // Frame.CompleteConstruction retains all assignment, art and notification code.
        var generated = QualityUtility.GenerateQualityCreatedByPawn(worker, relevantSkill, consumeInspiration);
        return service?.ResolveConstructionQuality(generated, frame, worker) ?? generated;
    }

    private static void SetUnavailable(string reason)
    {
        service?.SetConstructionAvailability(false, reason);
        if (!warned)
        {
            warned = true;
            Log.Warning($"[RimWorldUtilities] Legendary construction is unavailable: {reason}");
        }
    }
}
