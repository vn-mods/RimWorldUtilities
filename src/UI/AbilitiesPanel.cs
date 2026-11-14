using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class AbilitiesPanel : UtilitiesPanel
{
    protected override string Title => "Abilities";

    protected override void DrawContents(Rect rect)
    {
        DrawCooldowns(rect);
        DrawBiosculpterCycles(new Rect(rect.x, rect.y + 68f, rect.width, rect.height - 68f));
    }

    private static void DrawCooldowns(Rect rect)
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

    private static void DrawBiosculpterCycles(Rect rect)
    {
        GUI.color = Color.white;
        var service = RimWorldUtilitiesMod.Instance?.BiosculpterCycles;
        var available = service?.IsAvailable == true && service.IdeologyAvailable;
        var enabled = service?.FastCycles == true;
        var checkbox = new Rect(rect.x, rect.y, rect.width, 26f);
        Widgets.CheckboxLabeled(checkbox, "Fast Biosculpter Cycles (1 Hour)", ref enabled, disabled: !available);
        TooltipHandler.TipRegion(checkbox,
            "Set the four vanilla cycles to approximately one hour of powered processing for free player colonists " +
            "inside player-owned pods on player-controlled maps. Overrides cycle-speed bonuses while enabled. " +
            "Age Reversal also halves biological age, with a minimum of 13 or the higher native minimum; " +
            "stronger native rejuvenation is retained. " +
            "Active-cycle estimates update; empty-pod and entry estimates show native durations. Requires Ideology.");
        if (available && service != null && enabled != service.FastCycles)
            service.TrySetFastCycles(enabled);
        GUI.color = SecondaryTextColor;
        var status = new Rect(rect.x, rect.y + 34f, rect.width, 24f);
        if (!string.IsNullOrEmpty(service?.PersistenceError))
        {
            Widgets.Label(status, "Could not save biosculpter settings");
            TooltipHandler.TipRegion(status, service.PersistenceError);
        }
        else if (!available)
        {
            Widgets.Label(status, "Fast biosculpter cycles unavailable");
            TooltipHandler.TipRegion(status, service?.IsAvailable == true && !service.IdeologyAvailable
                ? "Ideology DLC is not active."
                : service?.UnavailableReason ?? "Biosculpter patch has not initialized.");
        }
        else if (service?.AgeReversalAvailable != true)
        {
            Widgets.Label(status, "Enhanced age reversal unavailable");
            TooltipHandler.TipRegion(status, (service?.AgeReversalUnavailableReason ?? "Age-reversal patch has not initialized.") +
                " One-hour processing remains available.");
        }
    }
}
