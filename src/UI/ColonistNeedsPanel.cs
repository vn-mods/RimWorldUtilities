using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ColonistNeedsPanel : UtilitiesPanel
{
    private const float RowHeight = 30f;
    protected override string Title => "Colonist Needs";

    internal static float RequiredHeight(float width) =>
        52f + 24f + RowHeight * (ColonistNeedsService.BasicNeeds.Count / (width - 24f >= 360f ? 2 : 1)) + 24f;

    protected override void DrawContents(Rect rect)
    {
        var service = RimWorldUtilitiesMod.Instance?.ColonistNeeds;
        var available = service?.IsAvailable == true;
        GUI.color = SecondaryTextColor;
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), "Basic");
        var columns = rect.width >= 360f ? 2 : 1;
        var columnWidth = rect.width / columns;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        for (var i = 0; i < ColonistNeedsService.BasicNeeds.Count; i++)
        {
            var id = ColonistNeedsService.BasicNeeds[i];
            var checkbox = new Rect(rect.x + (i % columns) * columnWidth,
                rect.y + 24f + (i / columns) * RowHeight, columnWidth - 12f, 26f);
            var enabled = service?.IsEnabled(id) == true;
            Widgets.CheckboxLabeled(checkbox, id == "Joy" ? "Recreation" : id, ref enabled, disabled: !available);
            TooltipHandler.TipRegion(checkbox, "Keep full for free player colonists.");
            if (available && service != null && enabled != service.IsEnabled(id)) service.TrySetEnabled(id, enabled);
        }

        var status = new Rect(rect.x, rect.y + 24f + (ColonistNeedsService.BasicNeeds.Count / columns) * RowHeight,
            rect.width, 24f);
        GUI.color = SecondaryTextColor;
        if (!available)
        {
            Widgets.Label(status, "Unavailable");
            TooltipHandler.TipRegion(status, service?.UnavailableReason ?? "Needs have not initialized.");
        }
        else if (!string.IsNullOrEmpty(service?.PersistenceError))
        {
            Widgets.Label(status, "Could not save settings");
            TooltipHandler.TipRegion(status, service.PersistenceError);
        }
    }
}
