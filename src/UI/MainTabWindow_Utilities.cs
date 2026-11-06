using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

public sealed class MainTabWindow_Utilities : MainTabWindow
{
    private const float PanelGap = 12f;
    private const float ItemPanelHeight = 196f;
    private const float PlaceholderMinHeight = 112f;

    private Vector2 scrollPosition;

    private readonly UtilitiesPanel[] panels =
    {
        new ItemEditorPanel(),
        new ColonistNeedsPanel(),
        new CraftingPanel()
    };

    public MainTabWindow_Utilities()
    {
        optionalTitle = "RimWorld Utilities";
        doCloseX = true;
    }

    public override Vector2 RequestedTabSize => new Vector2(660f, 540f);

    public override void DoWindowContents(Rect inRect)
    {
        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        var previousWordWrap = Text.WordWrap;

        try
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;

            var minimumHeight = ItemPanelHeight + (PlaceholderMinHeight + PanelGap) * (panels.Length - 1);
            var contentHeight = Math.Max(inRect.height, minimumHeight);
            var contentRect = new Rect(0f, 0f,
                inRect.width - (contentHeight > inRect.height ? 16f : 0f), contentHeight);

            Widgets.BeginScrollView(inRect, ref scrollPosition, contentRect);
            try
            {
                var placeholderHeight = (contentHeight - ItemPanelHeight - PanelGap * (panels.Length - 1))
                    / (panels.Length - 1);
                var y = 0f;
                for (var i = 0; i < panels.Length; i++)
                {
                    var height = i == 0 ? ItemPanelHeight : placeholderHeight;
                    panels[i].Draw(new Rect(0f, y, contentRect.width, height));
                    y += height + PanelGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }
        finally
        {
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            Text.WordWrap = previousWordWrap;
        }
    }
}
