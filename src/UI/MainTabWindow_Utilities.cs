using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

public sealed class MainTabWindow_Utilities : MainTabWindow
{
    private const float PanelGap = 12f;
    private const float ItemPanelHeight = 276f;
    private const float CraftingMinHeight = 112f;

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

    public override Vector2 RequestedTabSize => new Vector2(660f, 620f);

    public override void DoWindowContents(Rect inRect)
    {
        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        var previousWordWrap = Text.WordWrap;

        try
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;

            var width = inRect.width;
            var needsHeight = ColonistNeedsPanel.RequiredHeight(width);
            var minimumHeight = ItemPanelHeight + needsHeight + CraftingMinHeight + PanelGap * 2f;
            if (minimumHeight > inRect.height)
            {
                width -= 16f;
                needsHeight = ColonistNeedsPanel.RequiredHeight(width);
                minimumHeight = ItemPanelHeight + needsHeight + CraftingMinHeight + PanelGap * 2f;
            }
            var contentHeight = Math.Max(inRect.height, minimumHeight);
            var contentRect = new Rect(0f, 0f, width, contentHeight);

            Widgets.BeginScrollView(inRect, ref scrollPosition, contentRect);
            try
            {
                var craftingHeight = contentHeight - ItemPanelHeight - needsHeight - PanelGap * 2f;
                var y = 0f;
                for (var i = 0; i < panels.Length; i++)
                {
                    var height = i == 0 ? ItemPanelHeight : i == 1 ? needsHeight : craftingHeight;
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
