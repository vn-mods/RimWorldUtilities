using System.Collections.Generic;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class CraftingPanel : UtilitiesPanel
{
    protected override string Title => "Craft / Build";

    protected override void DrawContents(Rect rect)
    {
        var crafting = RimWorldUtilitiesMod.Instance?.Crafting;
        var available = crafting?.IsLegendaryAvailable == true;
        var fullyAvailable = crafting?.IsLegendaryFullyAvailable == true;
        var enabled = crafting?.AlwaysLegendary == true;
        var checkboxRect = new Rect(rect.x, rect.y, 240f, 28f);
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        Widgets.CheckboxLabeled(checkboxRect, "Always Legendary", ref enabled, disabled: !available);
        TooltipHandler.TipRegion(checkboxRect,
            "Legendary quality for crafted items and constructed furniture.");
        if (available && crafting != null && enabled != crafting.AlwaysLegendary)
        {
            crafting.TrySetAlwaysLegendary(enabled);
        }

        DrawSpeed(new Rect(rect.x, rect.y + 30f, rect.width, 28f), crafting, building: false);
        DrawSpeed(new Rect(rect.x, rect.y + 60f, rect.width, 28f), crafting, building: true);

        GUI.color = SecondaryTextColor;
        var statusRect = new Rect(rect.x, rect.y + 90f, rect.width, 24f);
        if (!string.IsNullOrEmpty(crafting?.PersistenceError))
        {
            Widgets.Label(statusRect, "Could not save settings");
            TooltipHandler.TipRegion(statusRect, crafting.PersistenceError);
        }
        else if (!fullyAvailable || crafting?.IsSpeedAvailable != true || crafting?.IsBuildingSpeedAvailable != true)
        {
            Widgets.Label(statusRect, "Some features unavailable");
            var reason = !fullyAvailable ? crafting?.LegendaryUnavailableReason ?? "Crafting has not initialized." : string.Empty;
            if (crafting?.IsSpeedAvailable != true) reason += "\n" + (crafting?.SpeedUnavailableReason ?? "Crafting speed has not initialized.");
            if (crafting?.IsBuildingSpeedAvailable != true) reason += "\n" + (crafting?.BuildingSpeedUnavailableReason ?? "Building speed has not initialized.");
            TooltipHandler.TipRegion(statusRect, reason.Trim());
        }
    }

    private static void DrawSpeed(Rect rect, CraftingService? crafting, bool building)
    {
        var available = building ? crafting?.IsBuildingSpeedAvailable == true : crafting?.IsSpeedAvailable == true;
        var multiplier = (building ? crafting?.BuildingSpeedMultiplier : crafting?.SpeedMultiplier) ?? 1;
        var button = new Rect(rect.xMax - 144f, rect.y, 144f, rect.height);
        GUI.color = SecondaryTextColor;
        Widgets.Label(new Rect(rect.x, rect.y + 3f, rect.width - 152f, rect.height), building ? "Building Speed" : "Crafting Speed");
        GUI.color = Color.white;
        var previousEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previousEnabled && available;
            if (Widgets.ButtonText(button, SpeedLabel(multiplier)) && crafting != null)
            {
                var options = new List<FloatMenuOption>();
                foreach (var choice in CraftingService.SpeedMultipliers)
                    options.Add(new FloatMenuOption(SpeedLabel(choice), choice == multiplier ? null : () =>
                    {
                        if (building) crafting.TrySetBuildingSpeedMultiplier(choice);
                        else crafting.TrySetSpeedMultiplier(choice);
                    }));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        finally { GUI.enabled = previousEnabled; }
        TooltipHandler.TipRegion(button, available
            ? (building ? "Frame construction speed for your faction. XP per tick is unchanged."
                : "Production work speed for your faction. XP per tick is unchanged.")
            : (building ? crafting?.BuildingSpeedUnavailableReason : crafting?.SpeedUnavailableReason) ?? "Speed has not initialized.");
    }

    private static string SpeedLabel(int multiplier) => multiplier == 1 ? "1× (Normal)" : $"{multiplier}×";
}
