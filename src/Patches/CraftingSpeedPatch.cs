using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorldUtilities.Services;
using Verse;
using Verse.AI;

namespace RimWorldUtilities.Patches;

internal static class CraftingSpeedPatch
{
    internal const string OwnerId = "slava.rimworldutilities.crafting.speed";
    private static readonly FieldInfo IntervalAction = typeof(Toil).GetField(nameof(Toil.tickIntervalAction))!;
    private static readonly FieldInfo WorkLeft = typeof(JobDriver_DoBill).GetField(nameof(JobDriver_DoBill.workLeft))!;
    internal static readonly MethodInfo ScaleMethod = typeof(CraftingSpeedPatch).GetMethod(
        nameof(ScaleWork), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static bool attempted;
    private static bool warned;
    private static CraftingService? service;

    internal static bool TryResolveCallback(IReadOnlyList<CodeInstruction> factory,
        out MethodInfo? callback, out string error)
    {
        callback = null;
        var matches = 0;
        for (var i = 2; i < factory.Count; i++)
        {
            if (factory[i].opcode != OpCodes.Stfld || !IntervalAction.Equals(factory[i].operand)) continue;
            matches++;
            if (factory[i - 1].opcode == OpCodes.Newobj && factory[i - 1].operand is ConstructorInfo constructor &&
                constructor.DeclaringType == typeof(Action<int>) && factory[i - 2].opcode == OpCodes.Ldftn &&
                factory[i - 2].operand is MethodInfo method && method.ReturnType == typeof(void) &&
                method.GetParameters() is var parameters && parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
            {
                callback = method;
            }
        }

        if (matches == 1 && callback != null)
        {
            error = string.Empty;
            return true;
        }

        callback = null;
        error = "Could not resolve exactly one interval callback with the expected signature.";
        return false;
    }

    internal static void Install(CraftingService crafting)
    {
        if (attempted) return;
        attempted = true;
        service = crafting;
        try
        {
            // Inspect the factory's assignment, without constructing/running a Toil or
            // relying on a closure name, local slot, or initialized game definitions.
            var factory = typeof(Toils_Recipe).GetMethod(nameof(Toils_Recipe.DoRecipeWork), Type.EmptyTypes);
            if (factory == null)
            {
                SetUnavailable("The recipe work factory was not found.");
                return;
            }

            if (!TryResolveCallback(PatchProcessor.GetOriginalInstructions(factory), out var callback, out var error))
            {
                SetUnavailable(error);
                return;
            }

            new Harmony(OwnerId).Patch(callback, transpiler: new HarmonyMethod(typeof(CraftingSpeedPatch), nameof(Transpiler)));
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not install the crafting speed patch: {exception.GetBaseException().Message}");
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

        service?.SetSpeedAvailability(true);
        return rewritten;
    }

    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        var subtraction = -1;
        var matches = 0;
        var decrements = 0;
        for (var i = 0; i + 1 < original.Count; i++)
        {
            if (original[i].opcode != OpCodes.Sub || original[i + 1].opcode != OpCodes.Stfld ||
                !WorkLeft.Equals(original[i + 1].operand)) continue;
            decrements++;
            // driver; dup; ldfld workLeft; speed; delta; conv.r4; mul; sub; stfld workLeft
            if (i >= 7 && IsLoad(original[i - 7].opcode) && original[i - 6].opcode == OpCodes.Dup &&
                original[i - 5].opcode == OpCodes.Ldfld && WorkLeft.Equals(original[i - 5].operand) &&
                IsLoad(original[i - 4].opcode) && IsArgumentLoad(original[i - 3].opcode) &&
                original[i - 2].opcode == OpCodes.Conv_R4 && original[i - 1].opcode == OpCodes.Mul)
            {
                matches++;
                subtraction = i;
            }
        }

        if (matches != 1 || decrements != 1 || original.Any(i => ScaleMethod.Equals(i.operand)))
        {
            rewritten = original.ToList();
            error = $"Expected exactly one native recipe work decrement; found {matches} matching patterns " +
                $"and {decrements} decrements. The game version or another mod changed this method.";
            return false;
        }

        rewritten = new List<CodeInstruction>(original.Count + 2);
        for (var i = 0; i < original.Count; i++)
        {
            var copy = new CodeInstruction(original[i]);
            if (i == subtraction)
            {
                // Stack: driver, workLeft, nativeWork. Reload the verified receiver;
                // scale only nativeWork before the original subtract/store and callbacks.
                var receiver = original[i - 7];
                var loadDriver = new CodeInstruction(receiver.opcode, receiver.operand);
                loadDriver.labels.AddRange(copy.labels);
                loadDriver.blocks.AddRange(copy.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
                copy.labels.Clear();
                copy.blocks.RemoveAll(b => b.blockType != ExceptionBlockType.EndExceptionBlock);
                rewritten.Add(loadDriver);
                rewritten.Add(new CodeInstruction(OpCodes.Call, ScaleMethod));
            }
            rewritten.Add(copy);
        }

        error = string.Empty;
        return true;
    }

    private static bool IsArgumentLoad(OpCode code) => code == OpCodes.Ldarg || code == OpCodes.Ldarg_S ||
        code == OpCodes.Ldarg_0 || code == OpCodes.Ldarg_1 || code == OpCodes.Ldarg_2 || code == OpCodes.Ldarg_3;

    private static bool IsLoad(OpCode code) => IsArgumentLoad(code) || code == OpCodes.Ldloc || code == OpCodes.Ldloc_S ||
        code == OpCodes.Ldloc_0 || code == OpCodes.Ldloc_1 || code == OpCodes.Ldloc_2 || code == OpCodes.Ldloc_3;

    private static float ScaleWork(float work, JobDriver_DoBill driver) => service?.ScaleWork(work, driver) ?? work;

    private static void SetUnavailable(string reason)
    {
        service?.SetSpeedAvailability(false, reason);
        if (!warned)
        {
            warned = true;
            Log.Warning($"[RimWorldUtilities] Crafting speed is unavailable: {reason}");
        }
    }
}
