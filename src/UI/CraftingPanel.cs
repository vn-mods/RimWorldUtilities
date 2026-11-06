using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class CraftingPanel : UtilitiesPanel
{
    protected override string Title => "Crafting";

    protected override void DrawContents(Rect rect)
    {
        var crafting = RimWorldUtilitiesMod.Instance?.Crafting;
        var available = crafting?.IsAvailable == true;
        var enabled = crafting?.AlwaysLegendary == true;
        var checkboxRect = new Rect(rect.x, rect.y, 240f, 28f);
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        Widgets.CheckboxLabeled(checkboxRect, "Always Legendary", ref enabled, disabled: !available);
        TooltipHandler.TipRegion(checkboxRect,
            "Legendary quality for crafted items made by your faction.");
        if (available && crafting != null && enabled != crafting.AlwaysLegendary)
        {
            crafting.TrySetAlwaysLegendary(enabled);
        }

        GUI.color = SecondaryTextColor;
        var statusRect = new Rect(rect.x, rect.y + 34f, rect.width, 24f);
        if (!available)
        {
            Widgets.Label(statusRect, "Unavailable");
            TooltipHandler.TipRegion(statusRect, crafting?.UnavailableReason ?? "Crafting has not initialized.");
        }
        else if (!string.IsNullOrEmpty(crafting?.PersistenceError))
        {
            Widgets.Label(statusRect, "Could not save settings");
            TooltipHandler.TipRegion(statusRect, crafting.PersistenceError);
        }
    }
}
