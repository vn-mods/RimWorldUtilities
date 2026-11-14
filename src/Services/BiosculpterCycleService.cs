using System;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class BiosculpterCycleService
{
    internal const int TargetProcessingTicks = GenDate.TicksPerHour;
    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Faction?> playerFaction;
    private readonly Func<bool> ideologyActive;

    public bool IsAvailable { get; private set; }
    public bool FastCycles => settings.FastBiosculpterCycles;
    public bool IdeologyAvailable => ideologyActive();
    internal bool Enabled => IsAvailable && FastCycles && IdeologyAvailable;
    public string UnavailableReason { get; private set; } = "Biosculpter patch has not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;
    public bool AgeReversalAvailable { get; private set; }
    public string AgeReversalUnavailableReason { get; private set; } = "Age-reversal patch has not initialized.";

    public BiosculpterCycleService(CustomStackLimitSettings settings, Action<CustomStackLimitSettings> persist,
        Func<Faction?>? playerFaction = null, Func<bool>? ideologyActive = null)
    {
        this.settings = settings;
        this.persist = persist;
        this.playerFaction = playerFaction ?? (() => Current.Game != null ? Faction.OfPlayer : null);
        this.ideologyActive = ideologyActive ?? (() => ModsConfig.IdeologyActive);
    }

    internal void SetAvailability(bool available, string reason = "")
    {
        IsAvailable = available;
        UnavailableReason = available ? string.Empty : reason;
    }

    internal void SetAgeReversalAvailability(bool available, string reason = "")
    {
        AgeReversalAvailable = available;
        AgeReversalUnavailableReason = available ? string.Empty : reason;
    }

    public bool TrySetFastCycles(bool enabled)
    {
        if (enabled && (!IsAvailable || !IdeologyAvailable)) return false;
        if (FastCycles == enabled)
        {
            PersistenceError = string.Empty;
            return true;
        }
        var candidate = settings.Copy();
        candidate.FastBiosculpterCycles = enabled;
        try { persist(candidate); }
        catch (Exception exception)
        {
            PersistenceError = $"Could not save biosculpter settings: {exception.Message}";
            return false;
        }
        settings.CopyFrom(candidate);
        PersistenceError = string.Empty;
        return true;
    }

    internal bool IsEligible(CompBiosculpterPod? pod, Pawn? pawn)
    {
        var player = playerFaction();
        var parent = pod?.parent;
        // Occupants are despawned and suspended. Verify their actual container
        // instead of borrowing the spawned ability/needs eligibility predicates.
        return player != null && parent != null && !parent.Destroyed && parent.Spawned &&
            parent.Faction == player && parent.Map?.ParentFaction == player &&
            pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Faction == player &&
            !pawn.IsPrisoner && !pawn.IsSlave && pawn.IsColonist && !pawn.RaceProps.IsMechanoid &&
            pawn.HostFaction == null && pawn.GuestStatus == null && pawn.MentalStateDef == null &&
            ReferenceEquals(pod!.Occupant, pawn) && ReferenceEquals(pawn.ParentHolder, pod) &&
            pod.GetDirectlyHeldThings()?.Contains(pawn) == true &&
            !pawn.IsQuestLodger() && pawn.GetExtraHostFaction() == null;
    }

    internal float SpeedFor(CompBiosculpterPod? pod, Pawn? pawn, float original)
    {
        if (!Enabled || !IsEligible(pod, pawn)) return original;
        return SpeedForCycle(pod!.CurrentCycle, original);
    }

    internal static float SpeedForCycle(CompBiosculpterPod_Cycle? cycle, float original)
    {
        // Custom cycles can have different treatment semantics. Opt in only the
        // four verified vanilla implementations, using their live definition data.
        var type = cycle?.GetType();
        if (type != typeof(CompBiosculpterPod_MedicCycle) && type != typeof(CompBiosculpterPod_RegenerationCycle) &&
            type != typeof(CompBiosculpterPod_AgeReversalCycle) && type != typeof(CompBiosculpterPod_PleasureCycle))
            return original;
        if (cycle!.props is not CompProperties_BiosculpterPod_BaseCycle properties) return original;
        var speed = properties.durationDays * GenDate.TicksPerDay / TargetProcessingTicks;
        // Leave invalid/custom definition data to its native path, never emit an
        // infinite or nonpositive rate or modify the stored remaining work.
        return float.IsFinite(speed) && speed > 0f ? speed : original;
    }

    internal static long RejuvenatedAge(long originalAge, long nativeAge, long minimumAge) =>
        Math.Min(originalAge, Math.Min(nativeAge,
            Math.Max(Math.Max(13L * GenDate.TicksPerYear, minimumAge), originalAge / 2)));

    internal long AgeReversalTarget(CompBiosculpterPod_AgeReversalCycle? cycle, Pawn? pawn, long nativeAge)
    {
        if (!AgeReversalAvailable || !Enabled || cycle?.GetType() != typeof(CompBiosculpterPod_AgeReversalCycle) ||
            cycle.props is not CompProperties_BiosculpterPod_BaseCycle { key: "ageReversal" } ||
            pawn?.ageTracker == null) return nativeAge;
        var pod = cycle.parent?.TryGetComp<CompBiosculpterPod>();
        if (!IsEligible(pod, pawn) || !ReferenceEquals(pod!.CurrentCycle, cycle)) return nativeAge;
        var minimumYears = pawn.ageTracker.AdultMinAge;
        if (!float.IsFinite(minimumYears) || minimumYears < 0f) return nativeAge;
        // Use the same minimum-age conversion as vanilla. This runs immediately
        // before its setter, so the current biological ticks are the original age.
        return RejuvenatedAge(pawn.ageTracker.AgeBiologicalTicks, nativeAge,
            (long)(GenDate.TicksPerYear * minimumYears));
    }
}
