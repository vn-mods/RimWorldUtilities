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

internal static class BuildingSpeedPatch
{
    internal const string OwnerId = "slava.rimworldutilities.building.speed";
    private static readonly FieldInfo WorkDone = typeof(Frame).GetField(nameof(Frame.workDone))!;
    private static readonly MethodInfo WorkToBuild = typeof(Frame).GetProperty(nameof(Frame.WorkToBuild))!.GetMethod!;
    private static readonly MethodInfo Stuff = typeof(Thing).GetProperty(nameof(Thing.Stuff))!.GetMethod!;
    private static readonly FieldInfo Speed = typeof(StatDefOf).GetField(nameof(StatDefOf.ConstructionSpeed))!;
    private static readonly FieldInfo MaterialSpeed = typeof(StatDefOf).GetField(nameof(StatDefOf.ConstructionSpeedFactor))!;
    private static readonly MethodInfo GetStat = typeof(StatExtension).GetMethod(nameof(StatExtension.GetStatValue),
        new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) })!;
    private static readonly MethodInfo GetAbstractStat = typeof(StatExtension).GetMethod(nameof(StatExtension.GetStatValueAbstract),
        new[] { typeof(BuildableDef), typeof(StatDef), typeof(ThingDef) })!;
    private static readonly MethodInfo Pow = typeof(UnityEngine.Mathf).GetMethod(nameof(UnityEngine.Mathf.Pow),
        new[] { typeof(float), typeof(float) })!;
    private static readonly MethodInfo Complete = typeof(Frame).GetMethod(nameof(Frame.CompleteConstruction))!;
    internal static readonly MethodInfo ScaleMethod = typeof(BuildingSpeedPatch).GetMethod(
        nameof(ScaleWork), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static bool attempted;
    private static bool warned;
    private static CraftingService? service;

    internal static MethodInfo? FindIterator()
    {
        var factory = typeof(JobDriver_ConstructFinishFrame).GetMethod("MakeNewToils",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return factory != null ? AccessTools.EnumeratorMoveNext(factory) : null;
    }

    internal static void Install(CraftingService crafting)
    {
        if (attempted) return;
        attempted = true;
        service = crafting;
        try
        {
            var iterator = FindIterator();
            if (iterator == null || iterator.ReturnType != typeof(bool) || iterator.GetParameters().Length != 0)
                throw new MissingMethodException("The construction toil iterator was not found.");
            if (!CraftingSpeedPatch.TryResolveCallback(PatchProcessor.GetOriginalInstructions(iterator), out var callback, out var error))
            {
                SetUnavailable(error);
                return;
            }
            new Harmony(OwnerId).Patch(callback,
                transpiler: new HarmonyMethod(typeof(BuildingSpeedPatch), nameof(Transpiler)));
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not install building speed: {exception.GetBaseException().Message}");
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
        service?.SetBuildingSpeedAvailability(true);
        return rewritten;
    }

    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        rewritten = original.ToList();
        error = "Expected exactly one shared construction work calculation after material modifiers and before the botch check. " +
            "The game version or another mod changed this method.";
        if (original.Any(i => Equals(i.operand, ScaleMethod)) ||
            original.Count(i => i.opcode == OpCodes.Stfld && Equals(i.operand, WorkDone)) != 1 ||
            original.Count(i => i.Calls(WorkToBuild)) != 1 || original.Count(i => i.Calls(Pow)) != 1 ||
            original.Count(i => i.Calls(Complete)) != 1) return false;

        var progress = -1;
        var completion = -1;
        for (var i = 5; i < original.Count; i++)
        {
            if (original[i].opcode == OpCodes.Stfld && Equals(original[i].operand, WorkDone) &&
                original[i - 1].opcode == OpCodes.Add && Local(original[i - 2]) >= 0 &&
                original[i - 3].opcode == OpCodes.Ldfld && Equals(original[i - 3].operand, WorkDone) &&
                original[i - 4].opcode == OpCodes.Dup && IsLoad(original[i - 5])) progress = i;
            if (original[i].Calls(Complete) && IsLoad(original[i - 1]) && IsLoad(original[i - 2])) completion = i;
        }
        if (progress < 0 || completion <= progress) return false;
        var frame = original[progress - 5];
        var work = original[progress - 2];
        var actor = original[completion - 1];
        if (!SameLoad(frame, original[completion - 2])) return false;

        // Match the complete native rate/material block. Its common exit must be
        // scaled, including the branch for frames without Stuff. Discover locals
        // from their uses, never from fixed compiler local/closure names.
        var pivot = -1;
        for (var i = 22; i + 2 < progress; i++)
        {
            if (!SameLoad(frame, original[i]) || !original[i + 1].Calls(WorkToBuild) || Local(original[i + 2], true) < 0) continue;
            if (!SameLoad(actor, original[i - 22]) || !original[i - 21].LoadsField(Speed) ||
                original[i - 20].opcode != OpCodes.Ldc_I4_1 || original[i - 19].opcode != OpCodes.Ldc_I4_M1 ||
                !original[i - 18].Calls(GetStat) || original[i - 17].opcode != OpCodes.Ldc_R4 ||
                !Equals(original[i - 17].operand, 1.7f) || original[i - 16].opcode != OpCodes.Mul ||
                original[i - 15].opcode != OpCodes.Ldarg_1 || original[i - 14].opcode != OpCodes.Conv_R4 ||
                original[i - 13].opcode != OpCodes.Mul || Local(original[i - 12], true) != Local(work) ||
                !SameLoad(frame, original[i - 11]) || !original[i - 10].Calls(Stuff) ||
                (original[i - 9].opcode != OpCodes.Brfalse && original[i - 9].opcode != OpCodes.Brfalse_S) ||
                original[i - 9].operand is not Label join || !original[i].labels.Contains(join) ||
                Local(original[i - 8]) != Local(work) || !SameLoad(frame, original[i - 7]) ||
                !original[i - 6].Calls(Stuff) || !original[i - 5].LoadsField(MaterialSpeed) ||
                original[i - 4].opcode != OpCodes.Ldnull || !original[i - 3].Calls(GetAbstractStat) ||
                original[i - 2].opcode != OpCodes.Mul || Local(original[i - 1], true) != Local(work)) return false;

            // The exact same local must feed Pow's work fraction and the final
            // progress addition, with no intervening writes or second scaling.
            var total = Local(original[i + 2], true);
            var probability = -1;
            for (var p = i + 6; p < progress; p++)
                if (original[p].Calls(Pow) && original[p - 1].opcode == OpCodes.Div &&
                    Local(original[p - 2]) == total && Local(original[p - 3]) == Local(work)) probability = p;
            if (probability < 0 || original.Count(x => Local(x, true) == Local(work)) != 2) return false;
            pivot = i;
        }
        if (pivot < 0) return false;

        rewritten = new List<CodeInstruction>(original.Count + 5);
        for (var i = 0; i < original.Count; i++)
        {
            var copy = new CodeInstruction(original[i]);
            if (i == pivot)
            {
                var load = new CodeInstruction(work.opcode, work.operand);
                load.labels.AddRange(copy.labels);
                load.blocks.AddRange(copy.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
                copy.labels.Clear();
                copy.blocks.RemoveAll(b => b.blockType != ExceptionBlockType.EndExceptionBlock);
                rewritten.Add(load);
                rewritten.Add(new CodeInstruction(actor.opcode, actor.operand));
                rewritten.Add(new CodeInstruction(frame.opcode, frame.operand));
                rewritten.Add(new CodeInstruction(OpCodes.Call, ScaleMethod));
                var store = original[pivot - 1];
                rewritten.Add(new CodeInstruction(store.opcode, store.operand));
            }
            rewritten.Add(copy);
        }
        error = string.Empty;
        return true;
    }

    private static bool SameLoad(CodeInstruction a, CodeInstruction b) => a.opcode == b.opcode && Equals(a.operand, b.operand);
    private static bool IsLoad(CodeInstruction i) => Local(i) >= 0 || i.opcode == OpCodes.Ldarg || i.opcode == OpCodes.Ldarg_S ||
        i.opcode == OpCodes.Ldarg_0 || i.opcode == OpCodes.Ldarg_1 || i.opcode == OpCodes.Ldarg_2 || i.opcode == OpCodes.Ldarg_3;

    private static int Local(CodeInstruction i, bool store = false)
    {
        if (i.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
        if (i.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
        if (i.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
        if (i.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
        if (i.opcode != (store ? OpCodes.Stloc : OpCodes.Ldloc) && i.opcode != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
        return i.operand switch
        {
            LocalBuilder local => local.LocalIndex,
            LocalVariableInfo local => local.LocalIndex,
            int index => index,
            byte index => index,
            _ => -1
        };
    }

    private static float ScaleWork(float work, Pawn worker, Frame frame) => service?.ScaleBuildingWork(work, worker, frame) ?? work;

    private static void SetUnavailable(string reason)
    {
        service?.SetBuildingSpeedAvailability(false, reason);
        if (warned) return;
        warned = true;
        Log.Warning($"[RimWorldUtilities] Building speed is unavailable: {reason}");
    }
}
