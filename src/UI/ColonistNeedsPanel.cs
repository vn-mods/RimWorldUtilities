using System.Collections.Generic;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ColonistNeedsPanel : UtilitiesPanel
{
    private const float RowHeight = 30f;
    protected override string Title => "Colonist Needs";

    private static float GroupHeight(int count, int columns) => 24f + RowHeight * ((count + columns - 1) / columns);

    internal static float RequiredHeight(float width, bool? biotechActive = null)
    {
        var columns = width - 24f >= 360f ? 2 : 1;
        return 52f + GroupHeight(ColonistNeedsService.BasicNeeds.Count, columns) + 24f +
            ((biotechActive ?? ModsConfig.BiotechActive)
                ? 8f + GroupHeight(ColonistNeedsService.SpecialNeeds.Count, columns) : 0f);
    }

    protected override void DrawContents(Rect rect)
    {
        var service = RimWorldUtilitiesMod.Instance?.ColonistNeeds;
        var available = service?.IsAvailable == true;
        var y = DrawGroup(rect, "Basic", ColonistNeedsService.BasicNeeds, service);
        if (service?.SpecialNeedsAvailable == true)
            y = DrawGroup(new Rect(rect.x, y + 8f, rect.width, rect.height), "Special",
                ColonistNeedsService.SpecialNeeds, service);

        var status = new Rect(rect.x, y, rect.width, 24f);
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

    private float DrawGroup(Rect rect, string label, IReadOnlyList<string> needs, ColonistNeedsService? service)
    {
        var available = service?.IsAvailable == true;
        GUI.color = SecondaryTextColor;
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), label);
        var columns = rect.width >= 360f ? 2 : 1;
        var columnWidth = rect.width / columns;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        for (var i = 0; i < needs.Count; i++)
        {
            var id = needs[i];
            var checkbox = new Rect(rect.x + (i % columns) * columnWidth,
                rect.y + 24f + (i / columns) * RowHeight, columnWidth - 12f, 26f);
            var enabled = service?.IsEnabled(id) == true;
            Widgets.CheckboxLabeled(checkbox, id == "Joy" ? "Recreation" : id, ref enabled, disabled: !available);
            TooltipHandler.TipRegion(checkbox, "Keep full for free player colonists.");
            if (available && service != null && enabled != service.IsEnabled(id)) service.TrySetEnabled(id, enabled);
        }

        return rect.y + GroupHeight(needs.Count, columns);
    }
}
