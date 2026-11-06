using System.Globalization;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldUtilities.Services;

internal static class ItemEditorService
{
    public static Thing? GetSelectedItem(out string error)
    {
        if (Find.CurrentMap == null || WorldRendererUtility.WorldSelected)
        {
            error = "Open a colony map to edit an item.";
            return null;
        }

        // MainTabsRoot.ToggleTab preserves the selector for GameUI windows in 1.7.
        var selector = Find.Selector;
        if (selector.NumSelected != 1)
        {
            error = selector.NumSelected == 0
                ? "Select one stackable item on the map, then open Utilities."
                : "Select exactly one item. Multiple selections cannot be edited.";
            return null;
        }

        var item = selector.SingleSelectedThing;
        if (item == null)
        {
            error = "This selection is not an item. Select one stackable item on the map.";
            return null;
        }

        return IsEditableItem(item, out error) ? item : null;
    }

    public static bool TryParseStackCount(string text, int stackLimit, out int amount, out string error)
    {
        amount = 0;
        if (stackLimit <= 1)
        {
            error = "This item is not stackable.";
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
        {
            error = "Enter a whole number.";
            return false;
        }

        if (amount < 1 || amount > stackLimit)
        {
            error = $"Enter an amount from 1 to {stackLimit}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TrySetStackCount(Thing expectedItem, string text, out string message)
    {
        // Re-read the live selection so a stale UI cannot edit another object.
        var item = GetSelectedItem(out message);
        if (item == null)
        {
            return false;
        }

        if (!ReferenceEquals(item, expectedItem))
        {
            message = "The selected item changed. Review its amount and try again.";
            return false;
        }

        if (!TryParseStackCount(text, item.def.stackLimit, out var amount, out message))
        {
            return false;
        }

        var stackLimits = RimWorldUtilitiesMod.Instance?.StackLimits;
        if (stackLimits == null)
        {
            message = "The persistent stack-limit service is unavailable.";
            return false;
        }

        if (!stackLimits.TryPrepareForStackEdit(item.def, out message))
        {
            return false;
        }

        if (item.stackCount != amount)
        {
            var map = item.Map;
            item.stackCount = amount;

            // Match the mesh/merge bookkeeping used by Thing's native stack operations.
            item.DirtyMapMesh(map);
            map.listerMergeables.Notify_ThingStackChanged(item);
            map.resourceCounter.CheckUpdateResource(item);
        }

        message = $"Stack count set to {item.stackCount}.";
        return true;
    }

    private static bool IsEditableItem(Thing item, out string error)
    {
        if (item.Destroyed || !item.Spawned || item.Map != Find.CurrentMap)
        {
            error = "This item is no longer available on the current map. Select another item.";
            return false;
        }

        if (item is Pawn or Building or Corpse or MinifiedThing || item.def == null ||
            item.def.category != ThingCategory.Item || item.def.stackLimit <= 1)
        {
            error = "Only ordinary stackable items can be edited. This selection is unsupported.";
            return false;
        }

        if (item.stackCount < 1)
        {
            error = "This stack has no items and cannot be edited.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
