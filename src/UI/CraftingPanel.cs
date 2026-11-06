using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class CraftingPanel : UtilitiesPanel
{
    protected override string Title => "Crafting";

    protected override void DrawContents(Rect rect)
    {
        Widgets.Label(rect, "Crafting tools will be added here. No crafting adjustments are available yet.");
    }
}
