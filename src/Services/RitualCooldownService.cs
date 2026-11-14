using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class RitualCooldownService
{
    private readonly AbilityCooldownService abilities;

    internal RitualCooldownService(AbilityCooldownService abilities) => this.abilities = abilities;

    internal static bool IsSupported(Precept_Ritual? ritual) => ritual?.def?.defName is "LeaderSpeech" or "Conversion";

    internal static Precept_RoleSingle? SourceRole(Precept_Ritual ritual) =>
        ritual.ideo?.RolesListForReading?.FirstOrDefault(r => r.def == ritual.def.sourcePawnRoleDef) as Precept_RoleSingle;

    internal static Ability? SourceAbility(Precept_Ritual ritual) =>
        SourceRole(ritual)?.AbilitiesForReading?.FirstOrDefault(a => a.def == ritual.def.sourceAbilityDef);

    // Called only from player UI entry points, never as a global precept getter.
    // Ideology membership alone cannot establish ownership of an invocation.
    internal bool CanBypass(Precept_Ritual ritual, TargetInfo target, Pawn? selectedPawn = null)
    {
        if (!abilities.Enabled || !IsSupported(ritual) || !target.IsValid || target.Map == null ||
            target.Map.ParentFaction != abilities.PlayerFaction || abilities.PlayerFaction == null ||
            abilities.PlayerFaction.ideos?.Has(ritual.ideo) != true) return false;
        var holder = SourceRole(ritual)?.ChosenPawnSingle();
        var source = SourceAbility(ritual);
        return abilities.IsEligible(holder) && holder!.Map == target.Map && holder.Ideo == ritual.ideo &&
            source != null && ReferenceEquals(source.pawn, holder) && abilities.CanBypass(source) &&
            (selectedPawn == null || (abilities.IsEligible(selectedPawn) && selectedPawn.Map == target.Map));
    }

    internal int DeadlineForPlayerInteraction(Precept_Ritual ritual, TargetInfo target, Pawn? selectedPawn = null) =>
        CanBypass(ritual, target, selectedPawn) ? -1 : ritual.abilityOnCooldownUntilTick;

    internal bool CanConfirmPlayerDialog(Precept_Ritual ritual, TargetInfo target, RitualRoleAssignments assignments,
        bool sourceOnRawCooldown)
    {
        if (!CanBypass(ritual, target))
            return ritual.abilityOnCooldownUntilTick <= GenTicks.TicksGame && !sourceOnRawCooldown;

        // Dialogs can stay open across role/faction changes. Revalidate the actual
        // assigned cooldown-group role holders immediately before the native action.
        if (!ReferenceEquals(assignments.Ritual, ritual)) return false;
        var ids = ritual.def.useCooldownFromAbilityGroupDef?.ritualRoleIds;
        var holder = SourceRole(ritual)!.ChosenPawnSingle();
        if (ids == null || ids.Count == 0) return false;
        foreach (var id in ids)
        {
            if (assignments.GetRole(id) == null) return false;
            var assigned = assignments.AssignedPawns(id).ToList();
            if (assigned.Count != 1 || !ReferenceEquals(assigned[0], holder) || !abilities.IsEligible(assigned[0]))
                return false;
        }
        // Run the original source-ability restrictions; only its standard timer is bypassed.
        return SourceAbility(ritual)?.GizmoDisabled(out _) == false;
    }
}
