using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal abstract class UtilitiesPanel
{
    protected static readonly Color SecondaryTextColor = new Color(0.68f, 0.71f, 0.75f);

    protected abstract string Title { get; }

    public void Draw(Rect rect)
    {
        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        var previousWordWrap = Text.WordWrap;
        var previousColor = GUI.color;
        var previousEnabled = GUI.enabled;

        try
        {
            GUI.color = Color.white;
            Widgets.DrawMenuSection(rect);
            var contentRect = rect.ContractedBy(12f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = SecondaryTextColor;
            Widgets.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24f), Title);

            GUI.color = Color.white;
            contentRect.yMin += 28f;
            DrawContents(contentRect);
        }
        finally
        {
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            Text.WordWrap = previousWordWrap;
            GUI.color = previousColor;
            GUI.enabled = previousEnabled;
        }
    }

    protected abstract void DrawContents(Rect rect);
}
