using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldUtilities.Services;

internal sealed class CraftingService
{
    public static IReadOnlyList<int> SpeedMultipliers { get; } = Array.AsReadOnly(new[] { 1, 2, 3, 5, 10, 20 });
    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Faction?> playerFaction;

    public bool AlwaysLegendary => settings.AlwaysLegendaryCrafting;
    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "Crafting patch has not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;
    public int SpeedMultiplier => IsValidSpeedMultiplier(settings.CraftingSpeedMultiplier)
        ? settings.CraftingSpeedMultiplier : 1;
    public bool IsSpeedAvailable { get; private set; }
    public string SpeedUnavailableReason { get; private set; } = "Crafting speed patch has not initialized.";

    public CraftingService(CustomStackLimitSettings settings, Action<CustomStackLimitSettings> persist,
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

    internal void SetSpeedAvailability(bool available, string reason = "")
    {
        IsSpeedAvailable = available;
        SpeedUnavailableReason = available ? string.Empty : reason;
    }

    public static bool IsValidSpeedMultiplier(int value) => value is 1 or 2 or 3 or 5 or 10 or 20;

    public bool TrySetAlwaysLegendary(bool enabled)
    {
        if (enabled && !IsAvailable)
        {
            return false;
        }

        if (enabled == AlwaysLegendary)
        {
            PersistenceError = string.Empty;
            return true;
        }

        // Copy current shared settings, including any stack-limit changes since startup.
        var candidate = settings.Copy();
        candidate.AlwaysLegendaryCrafting = enabled;
        return TrySave(candidate);
    }

    public bool TrySetSpeedMultiplier(int multiplier)
    {
        if (!IsValidSpeedMultiplier(multiplier) || (!IsSpeedAvailable && multiplier != 1))
        {
            return false;
        }

        if (settings.CraftingSpeedMultiplier == multiplier)
        {
            PersistenceError = string.Empty;
            return true;
        }

        var candidate = settings.Copy();
        candidate.CraftingSpeedMultiplier = multiplier;
        return TrySave(candidate);
    }

    private bool TrySave(CustomStackLimitSettings candidate)
    {
        try
        {
            persist(candidate);
        }
        catch (Exception exception)
        {
            PersistenceError = $"Could not save crafting settings: {exception.Message}";
            return false;
        }

        settings.CopyFrom(candidate);
        PersistenceError = string.Empty;
        return true;
    }

    public QualityCategory ResolveQuality(QualityCategory generated, Thing product, RecipeDef recipe, Pawn worker)
    {
        return IsAvailable && AlwaysLegendary && IsEligible(product, recipe, worker)
            ? QualityCategory.Legendary : generated;
    }

    internal bool IsEligible(Thing product, RecipeDef recipe, Pawn worker)
    {
        if (product == null || product.Destroyed || product is Pawn ||
            product.TryGetComp<CompQuality>() == null || recipe == null || recipe.workSkill == null)
        {
            return false;
        }

        return IsEligibleProduction(recipe, worker);
    }

    public float ScaleWork(float work, JobDriver_DoBill driver)
    {
        var multiplier = SpeedMultiplier;
        return IsSpeedAvailable && multiplier != 1 && IsEligibleProgress(driver) ? work * multiplier : work;
    }

    internal bool IsEligibleProgress(JobDriver_DoBill driver) =>
        driver?.job?.RecipeDef is RecipeDef recipe && IsEligibleProduction(recipe, driver.pawn, driver);

    private bool IsEligibleProduction(RecipeDef recipe, Pawn worker, JobDriver_DoBill? expectedDriver = null)
    {
        if (worker == null || worker.Destroyed || worker.IsPrisoner || worker.Faction == null ||
            worker.Faction != playerFaction())
        {
            return false;
        }

        var job = worker.CurJob;
        if (worker.jobs?.curDriver is not JobDriver_DoBill driver ||
            (expectedDriver != null && !ReferenceEquals(driver, expectedDriver)) ||
            !ReferenceEquals(driver.pawn, worker) || !ReferenceEquals(driver.job, job) ||
            !ReferenceEquals(job?.RecipeDef, recipe) ||
            job.bill is not Bill_Production || job.bill is Bill_Autonomous ||
            job.GetTarget(TargetIndex.A).Thing is not Building_WorkTable table || table.Destroyed ||
            recipe.IsSurgery)
        {
            return false;
        }

        // Includes special-product recipes, while excluding destruction/cremation bills.
        return recipe.products?.Count > 0 || recipe.specialProducts?.Count > 0;
    }
}
