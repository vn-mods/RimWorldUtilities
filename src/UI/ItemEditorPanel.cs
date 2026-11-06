using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ItemEditorPanel : UtilitiesPanel
{
    protected override string Title => "Item Editor";

    protected override void DrawContents(Rect rect)
    {
        Widgets.Label(rect, "Item editing tools will be added here. No item changes are available yet.");
    }
}
