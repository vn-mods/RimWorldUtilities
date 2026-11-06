using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldUtilities.Services;

internal sealed class CraftingService
{
    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Func<Faction?> playerFaction;

    public bool AlwaysLegendary => settings.AlwaysLegendaryCrafting;
    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "Crafting patch has not initialized.";
    public string PersistenceError { get; private set; } = string.Empty;

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
            product.TryGetComp<CompQuality>() == null || recipe == null || recipe.workSkill == null ||
            worker == null || worker.Destroyed || worker.IsPrisoner || worker.Faction == null ||
            worker.Faction != playerFaction())
        {
            return false;
        }

        var job = worker.CurJob;
        if (worker.jobs?.curDriver is not JobDriver_DoBill driver ||
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
