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

internal static class AbilityCooldownPatch
{
    internal const string OwnerId = "slava.rimworldutilities.abilities.cooldowns";
    internal static readonly MethodInfo CanCastGetter = AccessTools.PropertyGetter(typeof(Ability), nameof(Ability.CanCast));
    internal static readonly MethodInfo RemainingGetter = AccessTools.PropertyGetter(typeof(Ability), nameof(Ability.CooldownTicksRemaining));
    internal static readonly MethodInfo OnCooldownGetter = AccessTools.PropertyGetter(typeof(Ability), nameof(Ability.OnCooldown));
    internal static readonly MethodInfo CooldownTick = AccessTools.DeclaredMethod(typeof(Ability), "CooldownTick");
    internal static readonly FieldInfo InCooldownField = AccessTools.Field(typeof(Ability), "inCooldown");
    internal static readonly MethodInfo EffectiveCooldown = AccessTools.DeclaredMethod(typeof(AbilityCooldownPatch), nameof(IsEffectivelyOnCooldown));
    internal static readonly MethodInfo RawRemaining = AccessTools.DeclaredMethod(typeof(AbilityCooldownPatch), nameof(TicksRemainingForTick));
    private static AccessTools.FieldRef<Ability, bool> inCooldown = null!;
    private static AccessTools.FieldRef<Ability, int> cooldownEndTick = null!;
    private static AbilityCooldownService? service;
    private static bool attempted;
    private static bool failed;

    internal static void Install(AbilityCooldownService abilities)
    {
        if (attempted) return;
        attempted = true;
        service = abilities;
        var harmony = new Harmony(OwnerId);
        try
        {
            inCooldown = AccessTools.FieldRefAccess<Ability, bool>("inCooldown");
            cooldownEndTick = AccessTools.FieldRefAccess<Ability, int>("cooldownEndTick");
            foreach (var target in new[] { CanCastGetter, CooldownTick })
                if (!TryRewrite(target, PatchProcessor.GetOriginalInstructions(target), out _, out var error))
                    throw new InvalidOperationException(error);
            harmony.Patch(CanCastGetter, transpiler: new HarmonyMethod(typeof(AbilityCooldownPatch), nameof(Transpiler)));
            harmony.Patch(CooldownTick, transpiler: new HarmonyMethod(typeof(AbilityCooldownPatch), nameof(Transpiler)));
            harmony.Patch(OnCooldownGetter, postfix: new HarmonyMethod(typeof(AbilityCooldownPatch), nameof(OnCooldownPostfix)));
            harmony.Patch(RemainingGetter, postfix: new HarmonyMethod(typeof(AbilityCooldownPatch), nameof(RemainingPostfix)));
            RitualCooldownPatch.Install(harmony, abilities);
            if (failed) throw new InvalidOperationException(abilities.UnavailableReason);
            abilities.SetAvailability(true);
        }
        catch (Exception exception)
        {
            Disable($"Could not install cooldown patches: {exception.GetBaseException().Message}");
            harmony.UnpatchAll(OwnerId);
        }
    }

    internal static void Disable(string reason)
    {
        service?.SetAvailability(false, reason);
        if (!failed) Log.Warning($"[RimWorldUtilities] Abilities are unavailable: {reason}");
        failed = true;
    }

    private static bool IsEffectivelyOnCooldown(Ability ability) =>
        inCooldown(ability) && service?.CanBypass(ability) != true;

    internal static bool RawOnCooldown(Ability ability) => ability.HasCooldown && inCooldown(ability);

    internal static int RawTicksRemaining(Ability ability) =>
        inCooldown(ability) ? Math.Max(cooldownEndTick(ability) - GenTicks.TicksGame, 0) : 0;

    private static int TicksRemainingForTick(Ability ability) => service?.CanBypass(ability) == true
        ? RawTicksRemaining(ability) : ability.CooldownTicksRemaining;

    private static void OnCooldownPostfix(Ability __instance, ref bool __result)
    {
        if (__result && service?.CanBypass(__instance) == true) __result = false;
    }

    private static void RemainingPostfix(Ability __instance, ref int __result)
    {
        if (__result > 0 && service?.CanBypass(__instance) == true) __result = 0;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        if (TryRewrite(__originalMethod, original, out var rewritten, out var error)) return rewritten;
        Disable(error);
        return original;
    }

    internal static bool TryRewrite(MethodBase target, IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        rewritten = original.Select(i => new CodeInstruction(i)).ToList();
        var canCast = target == CanCastGetter;
        var tick = target == CooldownTick;
        bool Matches(CodeInstruction i) => canCast ? i.LoadsField(InCooldownField) : i.Calls(RemainingGetter);
        var redirect = canCast ? EffectiveCooldown : RawRemaining;
        if ((!canCast && !tick) || original.Count(Matches) != 1 || original.Any(i => i.Calls(redirect)))
        {
            error = $"Expected exactly one {(canCast ? "inCooldown read" : "CooldownTicksRemaining call")} in {target}.";
            return false;
        }
        foreach (var instruction in rewritten.Where(Matches))
        {
            instruction.opcode = OpCodes.Call;
            instruction.operand = redirect;
        }
        error = string.Empty;
        return true;
    }
}
