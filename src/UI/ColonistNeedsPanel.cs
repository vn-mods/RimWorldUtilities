using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ColonistNeedsPanel : UtilitiesPanel
{
    protected override string Title => "Colonist Needs";

    protected override void DrawContents(Rect rect)
    {
        Widgets.Label(rect, "Colonist needs tools will be added here. No need adjustments are available yet.");
    }
}
