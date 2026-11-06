using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal abstract class UtilitiesPanel
{
    protected abstract string Title { get; }

    public void Draw(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        var contentRect = rect.ContractedBy(12f);

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 32f), Title);

        Text.Font = GameFont.Small;
        contentRect.yMin += 40f;
        DrawContents(contentRect);
    }

    protected abstract void DrawContents(Rect rect);
}
