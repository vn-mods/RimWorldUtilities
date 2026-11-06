using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldUtilities.Services;
using Verse;

namespace RimWorldUtilities.Patches;

internal static class ColonistNeedsPatch
{
    internal const string OwnerId = "slava.rimworldutilities.needs.basic";
    private static bool attempted;
    private static ColonistNeedsService? service;

    internal static MethodInfo? FindTarget() => typeof(Pawn_NeedsTracker).GetMethod(
        nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval), new[] { typeof(int) });

    internal static void Install(ColonistNeedsService needs)
    {
        if (attempted) return;
        attempted = true;
        service = needs;
        try
        {
            var target = FindTarget();
            if (target?.ReturnType != typeof(void) || typeof(Pawn_NeedsTracker)
                .GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType != typeof(Pawn))
                throw new MissingMethodException("The expected needs tracker method or pawn field was not found.");

            new Harmony(OwnerId).Patch(target,
                prefix: new HarmonyMethod(typeof(ColonistNeedsPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(ColonistNeedsPatch), nameof(Postfix)));
            needs.SetAvailability(true);
        }
        catch (Exception exception)
        {
            var reason = $"Could not install the needs patch: {exception.GetBaseException().Message}";
            needs.SetAvailability(false, reason);
            Log.Warning($"[RimWorldUtilities] Colonist needs are unavailable: {reason}");
        }
    }

    private static void Prefix(Pawn_NeedsTracker __instance, Pawn ___pawn, int delta, out bool __state)
    {
        __state = service is { IsAvailable: true, HasEnabledNeeds: true } &&
            ReferenceEquals(___pawn.needs, __instance) && ___pawn.IsHashIntervalTick(150, delta) && service.Refill(___pawn);
    }

    private static void Postfix(Pawn ___pawn, bool __state)
    {
        // Re-read live needs and eligibility after native callbacks. State holds no Need references.
        if (__state) service?.Refill(___pawn);
    }
}
