using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldUtilities.Services;

internal sealed class ResearchSpeedService
{
    public static IReadOnlyList<int> SpeedMultipliers => CraftingService.SpeedMultipliers;
    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Pawn, bool> eligibleWorker;

    public int SpeedMultiplier => CraftingService.IsValidSpeedMultiplier(settings.ResearchSpeedMultiplier)
        ? settings.ResearchSpeedMultiplier : 1;
    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "Research speed patch has not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;

    public ResearchSpeedService(CustomStackLimitSettings settings, Action<CustomStackLimitSettings> persist,
        Func<Pawn, bool> eligibleWorker)
    {
        this.settings = settings;
        this.persist = persist;
        this.eligibleWorker = eligibleWorker;
    }

    internal void SetAvailability(bool available, string reason = "")
    {
        IsAvailable = available;
        UnavailableReason = available ? string.Empty : reason;
    }

    public bool TrySetSpeedMultiplier(int multiplier)
    {
        if (!CraftingService.IsValidSpeedMultiplier(multiplier) || (!IsAvailable && multiplier != 1)) return false;
        if (settings.ResearchSpeedMultiplier == multiplier)
        {
            PersistenceError = string.Empty;
            return true;
        }

        var candidate = settings.Copy();
        candidate.ResearchSpeedMultiplier = multiplier;
        try { persist(candidate); }
        catch (Exception exception)
        {
            PersistenceError = $"Could not save research speed: {exception.Message}";
            return false;
        }
        settings.CopyFrom(candidate);
        PersistenceError = string.Empty;
        return true;
    }

    public float ScaleWork(float amount, ResearchManager manager, Pawn worker)
    {
        var multiplier = SpeedMultiplier;
        return IsAvailable && multiplier != 1 && IsEligible(manager, worker) ? amount * multiplier : amount;
    }

    internal bool IsEligible(ResearchManager manager, Pawn worker) =>
        eligibleWorker(worker) && manager?.GetProject() is { baseCost: > 0f, knowledgeCategory: null, knowledgeCost: 0f } &&
        worker.jobs?.curDriver is JobDriver_Research driver && driver.GetType() == typeof(JobDriver_Research) &&
        ReferenceEquals(driver.pawn, worker) && driver.job != null && ReferenceEquals(driver.job, worker.CurJob) &&
        driver.job.GetTarget(TargetIndex.A).Thing is Building_ResearchBench bench && !bench.Destroyed && bench.Spawned;
}
