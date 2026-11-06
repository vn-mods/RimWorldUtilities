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

internal static class LegendaryCraftingPatch
{
    internal const string OwnerId = "slava.rimworldutilities.crafting.legendary";
    private static bool attempted;
    private static bool warned;
    private static CraftingService? service;

    internal static readonly MethodInfo? NativeQualityMethod = typeof(QualityUtility).GetMethod(
        nameof(QualityUtility.GenerateQualityCreatedByPawn), new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) });
    internal static readonly MethodInfo RedirectMethod = typeof(LegendaryCraftingPatch).GetMethod(
        nameof(GenerateQuality), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static MethodInfo? FindTarget() => typeof(GenRecipe).GetMethod("PostProcessProduct",
        BindingFlags.Static | BindingFlags.NonPublic, null,
        new[] { typeof(Thing), typeof(RecipeDef), typeof(Pawn), typeof(Precept_ThingStyle),
            typeof(ThingStyleDef), typeof(int?) }, null);

    internal static void Install(CraftingService crafting)
    {
        if (attempted)
        {
            return;
        }

        attempted = true;
        service = crafting;
        try
        {
            var target = FindTarget();
            if (target == null || target.ReturnType != typeof(Thing) ||
                NativeQualityMethod?.ReturnType != typeof(QualityCategory))
            {
                SetUnavailable("The expected RimWorld recipe quality methods were not found.");
                return;
            }

            new Harmony(OwnerId).Patch(target, transpiler: new HarmonyMethod(
                typeof(LegendaryCraftingPatch), nameof(Transpiler)));
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not install the recipe quality patch: {exception.GetBaseException().Message}");
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
        var matches = original.Count(instruction => instruction.opcode == OpCodes.Call &&
            NativeQualityMethod != null && NativeQualityMethod.Equals(instruction.operand));
        if (matches != 1)
        {
            rewritten = original.ToList();
            error = $"Expected exactly one recipe quality-generation call; found {matches}. " +
                "The game version or another mod changed this method.";
            return false;
        }

        rewritten = new List<CodeInstruction>(original.Count + 2);
        foreach (var instruction in original)
        {
            if (instruction.opcode != OpCodes.Call || !NativeQualityMethod!.Equals(instruction.operand))
            {
                rewritten.Add(new CodeInstruction(instruction));
                continue;
            }

            // Original stack: pawn, skill, consumeInspiration. Add this method's
            // product and recipe arguments; keep branch and exception boundaries intact.
            var product = new CodeInstruction(OpCodes.Ldarg_0);
            product.labels.AddRange(instruction.labels);
            product.blocks.AddRange(instruction.blocks.Where(block =>
                block.blockType != ExceptionBlockType.EndExceptionBlock));
            rewritten.Add(product);
            rewritten.Add(new CodeInstruction(OpCodes.Ldarg_1));
            var redirect = new CodeInstruction(OpCodes.Call, RedirectMethod);
            redirect.blocks.AddRange(instruction.blocks.Where(block =>
                block.blockType == ExceptionBlockType.EndExceptionBlock));
            rewritten.Add(redirect);
        }

        error = string.Empty;
        return true;
    }

    private static QualityCategory GenerateQuality(Pawn worker, SkillDef relevantSkill, bool consumeInspiration,
        Thing product, RecipeDef recipe)
    {
        // Never skip native RNG, ideology modifiers or inspiration consumption.
        var generated = QualityUtility.GenerateQualityCreatedByPawn(worker, relevantSkill, consumeInspiration);
        return service?.ResolveQuality(generated, product, recipe, worker) ?? generated;
    }

    private static void SetUnavailable(string reason)
    {
        service?.SetAvailability(false, reason);
        if (!warned)
        {
            warned = true;
            Log.Warning($"[RimWorldUtilities] Always Legendary is unavailable: {reason}");
        }
    }
}
