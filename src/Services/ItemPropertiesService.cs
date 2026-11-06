using System;
using System.Globalization;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal delegate Thing? ItemSelectionResolver(Thing expectedItem, out string error);

internal sealed class ItemPropertiesService
{
    private readonly ItemSelectionResolver resolveSelection;

    public ItemPropertiesService() : this(ItemEditorService.GetSelectedItem)
    {
    }

    internal ItemPropertiesService(ItemSelectionResolver resolveSelection)
    {
        this.resolveSelection = resolveSelection;
    }

    public static bool CanEditHitPoints(Thing item) => ItemEditorService.IsOrdinaryItem(item) && item.def.useHitPoints;

    public static CompQuality? GetEditableQuality(Thing item) =>
        ItemEditorService.IsOrdinaryItem(item) && item is not Book ? item.TryGetComp<CompQuality>() : null;

    public static bool IsValidQuality(QualityCategory quality) =>
        quality >= QualityCategory.Awful && quality <= QualityCategory.Legendary;

    public static bool TryParseHitPoints(string text, int maximum, out int amount, out string error)
    {
        amount = 0;
        if (maximum < 1)
        {
            error = "This item has no valid maximum HP.";
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
        {
            error = "Enter a whole number for HP.";
            return false;
        }

        if (amount < 1 || amount > maximum)
        {
            error = $"Enter HP from 1 to {maximum}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TrySetHitPoints(Thing expectedItem, string text, out string error)
    {
        var item = ResolveExpected(expectedItem, out error);
        if (item == null || !TryGetFreshMaximum(item, out var maximum, out error))
        {
            return false;
        }

        if (!TryParseHitPoints(text, maximum, out var amount, out error))
        {
            return false;
        }

        SetHitPoints(item, amount);
        return true;
    }

    public bool TrySetMaxHitPoints(Thing expectedItem, out string error)
    {
        var item = ResolveExpected(expectedItem, out error);
        if (item == null || !TryGetFreshMaximum(item, out var maximum, out error))
        {
            return false;
        }

        SetHitPoints(item, maximum);
        return true;
    }

    public bool TrySetQuality(Thing expectedItem, QualityCategory quality, out string error)
    {
        var item = ResolveExpected(expectedItem, out error);
        if (item == null)
        {
            return false;
        }

        var comp = GetEditableQuality(item);
        if (comp == null || !IsValidQuality(quality))
        {
            error = "Quality editing is unavailable for this item or value.";
            return false;
        }

        if (comp.Quality == quality)
        {
            error = string.Empty;
            return true;
        }

        var hitPoints = item.HitPoints;
        comp.SetQuality(quality, source: null); // Includes native PostQualitySet callbacks exactly once.
        StatDefOf.MaxHitPoints.Worker.ClearCacheForThing(item);

        // Custom native callbacks can invalidate the target. Do not write HP to a stale selection.
        if (ResolveExpected(expectedItem, out error) == null)
        {
            return false;
        }

        if (CanEditHitPoints(item))
        {
            var maximum = item.MaxHitPoints;
            if (maximum < 1)
            {
                error = "Quality changed, but maximum HP is invalid. HP was not edited.";
                return false;
            }

            var preserved = Math.Clamp(hitPoints, 1, maximum);
            if (item.HitPoints != preserved)
            {
                item.HitPoints = preserved;
            }
        }

        RefreshGraphics(item);
        error = string.Empty;
        return true;
    }

    private Thing? ResolveExpected(Thing expectedItem, out string error)
    {
        var item = resolveSelection(expectedItem, out error);
        if (item != null && !ReferenceEquals(item, expectedItem))
        {
            error = "The selected item changed. Review its values and try again.";
            return null;
        }

        return item;
    }

    private static bool TryGetFreshMaximum(Thing item, out int maximum, out string error)
    {
        maximum = 0;
        if (!CanEditHitPoints(item))
        {
            error = "HP editing is unavailable for this item.";
            return false;
        }

        StatDefOf.MaxHitPoints.Worker.ClearCacheForThing(item);
        maximum = item.MaxHitPoints;
        if (maximum < 1)
        {
            error = "This item has no valid maximum HP.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void SetHitPoints(Thing item, int amount)
    {
        if (item.HitPoints != amount)
        {
            item.HitPoints = amount;
            RefreshGraphics(item);
        }
    }

    private static void RefreshGraphics(Thing item)
    {
        if (item.Spawned)
        {
            item.DirtyMapMesh(item.Map);
        }
    }
}
