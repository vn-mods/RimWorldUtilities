using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class ColonistNeedsService
{
    public static IReadOnlyList<string> BasicNeeds { get; } = Array.AsReadOnly(new[]
        { "Food", "Rest", "Joy", "Mood", "Comfort", "Beauty", "Outdoors", "Indoors" });

    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Faction?> playerFaction;
    private readonly Func<IEnumerable<Pawn>> accessiblePawns;

    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "Needs patch has not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;
    public bool HasEnabledNeeds
    {
        get
        {
            if (settings.EnabledColonistNeeds.Count == 0) return false;
            for (var i = 0; i < BasicNeeds.Count; i++)
                if (settings.EnabledColonistNeeds.Contains(BasicNeeds[i])) return true;
            return false;
        }
    }

    public ColonistNeedsService(CustomStackLimitSettings settings, Action<CustomStackLimitSettings> persist,
        Func<Faction?>? playerFaction = null, Func<IEnumerable<Pawn>>? accessiblePawns = null)
    {
        this.settings = settings;
        this.persist = persist;
        this.playerFaction = playerFaction ?? (() => Current.Game != null ? Faction.OfPlayer : null);
        this.accessiblePawns = accessiblePawns ?? (() => Current.Game != null
            ? PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive : Array.Empty<Pawn>());
    }

    internal void SetAvailability(bool available, string reason = "")
    {
        IsAvailable = available;
        UnavailableReason = available ? string.Empty : reason;
    }

    public static bool IsSupported(string id) => id is
        "Food" or "Rest" or "Joy" or "Mood" or "Comfort" or "Beauty" or "Outdoors" or "Indoors";

    public bool IsEnabled(string id) => IsSupported(id) && settings.EnabledColonistNeeds.Contains(id);

    public bool TrySetEnabled(string id, bool enabled)
    {
        if (!IsSupported(id) || (enabled && !IsAvailable)) return false;
        if (IsEnabled(id) == enabled)
        {
            PersistenceError = string.Empty;
            return true;
        }

        var candidate = settings.Copy();
        if (enabled) candidate.EnabledColonistNeeds.Add(id);
        else candidate.EnabledColonistNeeds.Remove(id);
        try { persist(candidate); }
        catch (Exception exception)
        {
            PersistenceError = $"Could not save needs settings: {exception.Message}";
            return false;
        }

        settings.CopyFrom(candidate);
        PersistenceError = string.Empty;
        if (enabled)
        {
            // Only a user enabling a toggle enumerates pawns. Never cache finder lists
            // or Needs; deduplicate map/world transfer overlap in this one-time pass.
            var pawns = new HashSet<Pawn>(accessiblePawns(), ReferenceEqualityComparer.Instance);
            foreach (var pawn in pawns) Refill(pawn, id);
        }
        return true;
    }

    internal bool IsEligible(Pawn pawn) => pawn != null && !pawn.Destroyed && !pawn.Dead &&
        !pawn.IsPrisoner && !pawn.IsSlave && pawn.Faction != null && pawn.Faction == playerFaction() &&
        pawn.HostFaction == null && pawn.GuestStatus == null && pawn.IsColonist &&
        !pawn.RaceProps.IsMechanoid && !pawn.Suspended && !pawn.IsQuestLodger();

    internal bool Refill(Pawn pawn, string? onlyNeed = null)
    {
        if (!IsAvailable || !HasEnabledNeeds || !IsEligible(pawn) || pawn.needs == null) return false;
        var needs = pawn.needs.AllNeeds;
        for (var i = 0; i < needs.Count; i++)
        {
            var need = needs[i];
            var id = need?.def?.defName;
            if (id == null || (onlyNeed != null && id != onlyNeed) || !IsEnabled(id)) continue;
            var maximum = need!.MaxLevel;
            if (float.IsFinite(maximum) && maximum >= 0f && need.CurLevel != maximum)
                need.CurLevel = maximum;
        }
        return true;
    }
}
