using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Services;
using Verse;

namespace RimWorldUtilities.Patches;

internal static class BiosculpterCyclePatch
{
    internal const string OwnerId = "slava.rimworldutilities.biosculpter.cycles";
    private static bool attempted;
    private static BiosculpterCycleService? service;

    internal static MethodInfo? FindTarget() => typeof(CompBiosculpterPod).GetMethod(
        nameof(CompBiosculpterPod.GetCycleSpeedFactorForPawn), new[] { typeof(Pawn) });

    internal static void Install(BiosculpterCycleService cycles)
    {
        if (attempted) return;
        attempted = true;
        service = cycles;
        var harmony = new Harmony(OwnerId);
        try
        {
            var target = FindTarget();
            if (target?.ReturnType != typeof(float) || target.IsStatic)
                throw new MissingMethodException("The expected pawn-specific biosculpter speed method was not found.");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(BiosculpterCyclePatch), nameof(Postfix)));
            cycles.SetAvailability(true);
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(OwnerId);
            var reason = $"Could not install the biosculpter patch: {exception.GetBaseException().Message}";
            cycles.SetAvailability(false, reason);
            Log.Warning($"[RimWorldUtilities] Fast biosculpter cycles are unavailable: {reason}");
        }
    }

    private static void Postfix(CompBiosculpterPod __instance, Pawn p, ref float __result) =>
        __result = service?.SpeedFor(__instance, p, __result) ?? __result;
}
