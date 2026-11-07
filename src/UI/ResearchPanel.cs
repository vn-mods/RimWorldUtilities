using System.Collections.Generic;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ResearchPanel : UtilitiesPanel
{
    protected override string Title => "Research";

    protected override void DrawContents(Rect rect)
    {
        var service = RimWorldUtilitiesMod.Instance?.Research;
        var available = service?.IsAvailable == true;
        var multiplier = service?.SpeedMultiplier ?? 1;
        var button = new Rect(rect.xMax - 144f, rect.y, 144f, 28f);
        GUI.color = SecondaryTextColor;
        Widgets.Label(new Rect(rect.x, rect.y + 3f, rect.width - 152f, 28f), "Research Speed");
        GUI.color = Color.white;
        var previousEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previousEnabled && available;
            if (Widgets.ButtonText(button, SpeedLabel(multiplier)) && service != null)
            {
                var options = new List<FloatMenuOption>();
                foreach (var choice in ResearchSpeedService.SpeedMultipliers)
                    options.Add(new FloatMenuOption(SpeedLabel(choice), choice == multiplier
                        ? null : () => service.TrySetSpeedMultiplier(choice)));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        finally { GUI.enabled = previousEnabled; }
        TooltipHandler.TipRegion(button, available ? "Research work speed for your faction. Intellectual XP is unchanged."
            : service?.UnavailableReason ?? "Research speed has not initialized.");

        GUI.color = SecondaryTextColor;
        var status = new Rect(rect.x, rect.y + 34f, rect.width, 24f);
        if (!string.IsNullOrEmpty(service?.PersistenceError))
        {
            Widgets.Label(status, "Could not save settings");
            TooltipHandler.TipRegion(status, service.PersistenceError);
        }
        else if (!available)
        {
            Widgets.Label(status, "Unavailable");
            TooltipHandler.TipRegion(status, service?.UnavailableReason ?? "Research speed has not initialized.");
        }
    }

    private static string SpeedLabel(int multiplier) => multiplier == 1 ? "1× (Normal)" : $"{multiplier}×";
}
