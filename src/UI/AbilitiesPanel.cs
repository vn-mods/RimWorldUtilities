using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class AbilitiesPanel : UtilitiesPanel
{
    protected override string Title => "Abilities";

    protected override void DrawContents(Rect rect)
    {
        var service = RimWorldUtilitiesMod.Instance?.Abilities;
        var available = service?.IsAvailable == true;
        var enabled = service?.DisableCooldowns == true;
        var checkbox = new Rect(rect.x, rect.y, rect.width, 26f);
        Widgets.CheckboxLabeled(checkbox, "Disable Cooldowns", ref enabled, disabled: !available);
        TooltipHandler.TipRegion(checkbox,
            "Bypass standard ability, Leader Speech and Conversion Ritual cooldowns for free player colonists. " +
            "Charges and externally managed cooldowns are excluded. Ritual repeat-quality penalties still apply.");
        if (available && service != null && enabled != service.DisableCooldowns)
            service.TrySetDisableCooldowns(enabled);
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
            TooltipHandler.TipRegion(status, service?.UnavailableReason ?? "Cooldown patches have not initialized.");
        }
    }
}
