using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

public sealed class MainTabWindow_Utilities : MainTabWindow
{
    private const float PanelGap = 12f;

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

    public override Vector2 RequestedTabSize => new Vector2(720f, 480f);

    public override void DoWindowContents(Rect inRect)
    {
        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        var previousWordWrap = Text.WordWrap;

        try
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;

            var panelHeight = (inRect.height - PanelGap * (panels.Length - 1)) / panels.Length;
            for (var i = 0; i < panels.Length; i++)
            {
                var panelRect = new Rect(inRect.x, inRect.y + i * (panelHeight + PanelGap),
                    inRect.width, panelHeight);
                panels[i].Draw(panelRect);
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
