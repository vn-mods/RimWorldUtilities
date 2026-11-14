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

internal static class RitualCooldownPatch
{
    internal static readonly FieldInfo DeadlineField = AccessTools.Field(typeof(Precept_Ritual), nameof(Precept_Ritual.abilityOnCooldownUntilTick));
    internal static readonly MethodInfo CommandTarget = AccessTools.DeclaredMethod(typeof(Command_Ritual), "ValidateDisabledState");
    internal static readonly MethodInfo DialogTarget = AccessTools.DeclaredMethod(typeof(Precept_Ritual), nameof(Precept_Ritual.GetRitualBeginWindow));
    private static readonly FieldInfo CommandTargetInfo = AccessTools.Field(typeof(Command_Ritual), "targetInfo");
    private static AccessTools.FieldRef<Dialog_BeginRitual, Dialog_BeginRitual.ActionCallback> dialogAction = null!;
    private static RitualCooldownService? service;

    internal static MethodInfo? FindOptionsIterator()
    {
        var factory = AccessTools.DeclaredMethod(typeof(FloatMenuOptionProvider_StartRitual), "GetOptionsFor",
            new[] { typeof(Thing), typeof(FloatMenuContext) });
        return factory == null ? null : AccessTools.EnumeratorMoveNext(factory);
    }

    internal static void Install(Harmony harmony, AbilityCooldownService abilities)
    {
        service = new RitualCooldownService(abilities);
        dialogAction = AccessTools.FieldRefAccess<Dialog_BeginRitual, Dialog_BeginRitual.ActionCallback>("action");
        var iterator = FindOptionsIterator() ?? throw new MissingMethodException("Ritual menu iterator not found.");
        if (CommandTargetInfo?.FieldType != typeof(TargetInfo)) throw new MissingFieldException("Ritual command target not found.");
        foreach (var target in new[] { CommandTarget, iterator })
            if (!TryRewrite(target, PatchProcessor.GetOriginalInstructions(target), out _, out var error))
                throw new InvalidOperationException(error);
        harmony.Patch(CommandTarget, transpiler: new HarmonyMethod(typeof(RitualCooldownPatch), nameof(Transpiler)));
        harmony.Patch(iterator, transpiler: new HarmonyMethod(typeof(RitualCooldownPatch), nameof(Transpiler)));
        harmony.Patch(DialogTarget, postfix: new HarmonyMethod(typeof(RitualCooldownPatch), nameof(DialogPostfix)));
    }

    private static int CommandDeadline(Precept_Ritual ritual, TargetInfo target) =>
        service?.DeadlineForPlayerInteraction(ritual, target) ?? ritual.abilityOnCooldownUntilTick;

    private static int MenuDeadline(Precept_Ritual ritual, Thing target, FloatMenuContext context) =>
        context.FirstSelectedPawn is { } pawn
            ? service?.DeadlineForPlayerInteraction(ritual, target, pawn) ?? ritual.abilityOnCooldownUntilTick
            : ritual.abilityOnCooldownUntilTick;

    private static void DialogPostfix(Precept_Ritual __instance, TargetInfo targetInfo, Pawn organizer,
        Pawn selectedPawn, Window __result)
    {
        // Capture only dialogs opened in a verified player context while enabled.
        // The wrapped callback is local to this dialog; no thread/global bypass scope.
        if (__result is not Dialog_BeginRitual dialog || service?.CanBypass(__instance, targetInfo, selectedPawn) != true ||
            (organizer != null && !ReferenceEquals(organizer, RitualCooldownService.SourceRole(__instance)?.ChosenPawnSingle()))) return;
        var original = dialogAction(dialog);
        if (original == null) return;
        dialogAction(dialog) = assignments =>
        {
            var source = RitualCooldownService.SourceAbility(__instance);
            if (service?.CanConfirmPlayerDialog(__instance, targetInfo, assignments,
                    source != null && AbilityCooldownPatch.RawOnCooldown(source)) != true)
            {
                Messages.Message("Ritual cooldown or role eligibility changed. Close and reopen the ritual window.",
                    targetInfo, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return original(assignments);
        };
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        if (TryRewrite(__originalMethod, original, out var rewritten, out var error)) return rewritten;
        AbilityCooldownPatch.Disable(error);
        return original;
    }

    internal static bool TryRewrite(MethodBase target, IReadOnlyList<CodeInstruction> original,
        out List<CodeInstruction> rewritten, out string error)
    {
        rewritten = original.Select(i => new CodeInstruction(i)).ToList();
        var command = target == CommandTarget;
        var menu = target == FindOptionsIterator();
        var redirect = AccessTools.DeclaredMethod(typeof(RitualCooldownPatch), command ? nameof(CommandDeadline) : nameof(MenuDeadline));
        // Resolve the iterator through Harmony's state-machine metadata; identify
        // current input fields by type, never compiler-generated names or ordinals.
        var fields = target.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var clicked = fields.Where(f => f.FieldType == typeof(Thing)).ToArray();
        var context = fields.Where(f => f.FieldType == typeof(FloatMenuContext)).ToArray();
        // Iterators may also store <>3__ input copies; only the fields actually read
        // by MoveNext are relevant. Require exactly one live field of each type.
        clicked = clicked.Where(f => original.Any(i => i.LoadsField(f))).ToArray();
        context = context.Where(f => original.Any(i => i.LoadsField(f))).ToArray();
        if ((!command && !menu) || original.Count(i => i.LoadsField(DeadlineField)) != (command ? 2 : 4) ||
            original.Any(i => i.Calls(redirect)) || (menu && (clicked.Length != 1 || context.Length != 1)))
        {
            error = $"Expected exactly {(command ? 2 : 4)} ritual deadline reads and unambiguous player UI inputs in {target}.";
            return false;
        }
        rewritten = new List<CodeInstruction>();
        foreach (var instruction in original)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.LoadsField(DeadlineField))
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(copy.labels);
                first.blocks.AddRange(copy.blocks);
                copy.labels.Clear();
                copy.blocks.Clear();
                rewritten.Add(first);
                rewritten.Add(new CodeInstruction(OpCodes.Ldfld, command ? CommandTargetInfo : clicked[0]));
                if (menu)
                {
                    rewritten.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    rewritten.Add(new CodeInstruction(OpCodes.Ldfld, context[0]));
                }
                copy.opcode = OpCodes.Call;
                copy.operand = redirect;
            }
            rewritten.Add(copy);
        }
        error = string.Empty;
        return true;
    }
}
