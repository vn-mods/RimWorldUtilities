using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class CraftingPanel : UtilitiesPanel
{
    protected override string Title => "Crafting";

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

        var speedAvailable = crafting?.IsSpeedAvailable == true;
        var speedRect = new Rect(rect.xMax - 144f, rect.y, 144f, 28f);
        GUI.color = SecondaryTextColor;
        Widgets.Label(new Rect(speedRect.x - 56f, rect.y + 3f, 48f, 28f), "Speed");
        GUI.color = Color.white;
        var previousEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previousEnabled && speedAvailable;
            if (Widgets.ButtonText(speedRect, SpeedLabel(crafting?.SpeedMultiplier ?? 1)) && crafting != null)
            {
                var options = new List<FloatMenuOption>();
                foreach (var choice in Services.CraftingService.SpeedMultipliers)
                {
                    options.Add(new FloatMenuOption(SpeedLabel(choice), choice == crafting.SpeedMultiplier
                        ? null : () => crafting.TrySetSpeedMultiplier(choice)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        finally { GUI.enabled = previousEnabled; }
        TooltipHandler.TipRegion(speedRect, speedAvailable
            ? "Production work speed for your faction. XP per tick is unchanged."
            : crafting?.SpeedUnavailableReason ?? "Crafting speed has not initialized.");

        GUI.color = SecondaryTextColor;
        var statusRect = new Rect(rect.x, rect.y + 34f, rect.width, 24f);
        if (!string.IsNullOrEmpty(crafting?.PersistenceError))
        {
            Widgets.Label(statusRect, "Could not save settings");
            TooltipHandler.TipRegion(statusRect, crafting.PersistenceError);
        }
        else if (!fullyAvailable || !speedAvailable)
        {
            Widgets.Label(statusRect, !available && !speedAvailable ? "Crafting unavailable" :
                !available ? "Legendary unavailable" : !fullyAvailable ? "Legendary partly unavailable" : "Speed unavailable");
            var reason = !fullyAvailable ? crafting?.LegendaryUnavailableReason ?? "Crafting has not initialized." : string.Empty;
            if (!speedAvailable) reason += "\n" + (crafting?.SpeedUnavailableReason ?? "Crafting speed has not initialized.");
            TooltipHandler.TipRegion(statusRect, reason.Trim());
        }
    }

    private static string SpeedLabel(int multiplier) => multiplier == 1 ? "1× (Normal)" : $"{multiplier}×";
}
