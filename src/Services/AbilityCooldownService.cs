using System;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class AbilityCooldownService
{
    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Faction?> playerFaction;

    public bool IsAvailable { get; private set; }
    public bool DisableCooldowns => settings.DisableAbilityCooldowns;
    internal bool Enabled => IsAvailable && DisableCooldowns;
    public string UnavailableReason { get; private set; } = "Cooldown patches have not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;
    internal Faction? PlayerFaction => playerFaction();

    public AbilityCooldownService(CustomStackLimitSettings settings, Action<CustomStackLimitSettings> persist,
        Func<Faction?>? playerFaction = null)
    {
        this.settings = settings;
        this.persist = persist;
        this.playerFaction = playerFaction ?? (() => Current.Game != null ? Faction.OfPlayer : null);
    }

    internal void SetAvailability(bool available, string reason = "")
    {
        IsAvailable = available;
        UnavailableReason = available ? string.Empty : reason;
    }

    public bool TrySetDisableCooldowns(bool enabled)
    {
        if (enabled && !IsAvailable) return false;
        if (DisableCooldowns == enabled)
        {
            PersistenceError = string.Empty;
            return true;
        }
        var candidate = settings.Copy();
        candidate.DisableAbilityCooldowns = enabled;
        try { persist(candidate); }
        catch (Exception exception)
        {
            PersistenceError = $"Could not save cooldown settings: {exception.Message}";
            return false;
        }
        settings.CopyFrom(candidate);
        PersistenceError = string.Empty;
        return true;
    }

    // IsColonistPlayerControlled alone includes secure slaves and quest lodgers.
    // Deliberately require a spawned, free colonist with current player control.
    internal bool IsEligible(Pawn? pawn) => pawn != null && !pawn.Destroyed && !pawn.Dead &&
        pawn.Faction != null && pawn.Faction == PlayerFaction && !pawn.IsPrisoner && !pawn.IsSlave &&
        pawn.HostFaction == null && pawn.GuestStatus == null && !pawn.Suspended &&
        pawn.IsColonistPlayerControlled && !pawn.RaceProps.IsMechanoid && !pawn.IsQuestLodger();

    internal static bool IsSupported(Ability? ability) => ability?.def != null &&
        (ability.GetType() == typeof(Ability) || ability.GetType() == typeof(Psycast)) &&
        !ability.UsesCharges && ability.def.charges <= 0 && !ability.def.cooldownPerCharge &&
        !ability.def.hasExternallyHandledCooldown;

    internal bool CanBypass(Ability ability) => Enabled && IsSupported(ability) && IsEligible(ability.pawn);
}
