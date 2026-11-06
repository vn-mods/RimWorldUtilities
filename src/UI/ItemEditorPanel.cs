using System.Globalization;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ItemEditorPanel : UtilitiesPanel
{
    private static readonly Color ErrorColor = new Color(0.96f, 0.66f, 0.56f);
    private static readonly Color SuccessColor = new Color(0.66f, 0.82f, 0.69f);
    private const string PersistentLimitInfo =
        "Persistent limits apply to every Steel stack and are saved across restarts. " +
        "Enabling the limit does not change quantities.\n\n" +
        "There is no safe disable/reset yet. Removing this mod or deleting its settings " +
        "while large stacks exist can cause resource loss on load.";

    private Thing? selectedItem;
    private string amountBuffer = string.Empty;
    private string feedback = string.Empty;
    private bool feedbackIsError;
    private float feedbackUntil;

    protected override string Title => "Item Editor";

    protected override void DrawContents(Rect rect)
    {
        var item = ItemEditorService.GetSelectedItem(out var selectionError);
        if (!ReferenceEquals(item, selectedItem))
        {
            selectedItem = item;
            amountBuffer = item?.stackCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            ClearFeedback();
            GUI.FocusControl(string.Empty);
        }

        if (item == null)
        {
            GUI.color = SecondaryTextColor;
            Widgets.Label(rect, selectionError);
            return;
        }

        var stackLimits = RimWorldUtilitiesMod.Instance?.StackLimits;
        DrawHeading(rect, item, stackLimits);
        DrawLimitsRow(new Rect(rect.x, rect.y + 36f, rect.width, 24f), item, stackLimits);
        DrawStackRow(new Rect(rect.x, rect.y + 72f, rect.width, 34f), item, stackLimits);
    }

    private static void DrawHeading(Rect rect, Thing item, CustomStackLimitService? stackLimits)
    {
        var labelRect = new Rect(rect.x, rect.y, rect.width - 32f, 32f);
        DrawText(labelRect, item.LabelCapNoCount, GameFont.Medium, Color.white);
        TooltipHandler.TipRegion(labelRect, item.LabelCapNoCount);

        var infoRect = new Rect(rect.xMax - 24f, rect.y + 4f, 24f, 24f);
        var info = $"Current stack: {item.stackCount:N0}\n" +
            "Enter a whole number and press Apply to change the selected stack.";
        if (stackLimits?.IsSupported(item.def) == true)
        {
            info += "\n\n" + (stackLimits.IsEnabled(item.def)
                ? "Persistent Steel limit enabled.\n\n" : "Enable a persistent limit of at least 100,000.\n\n");
            info += PersistentLimitInfo;
        }

        var persistenceError = stackLimits?.IsSupported(item.def) == true
            ? stackLimits.PersistenceError : string.Empty;
        if (persistenceError.Length > 0)
        {
            info += "\n\n" + persistenceError + "\nApply will retry saving before editing.";
        }

        GUI.color = persistenceError.Length > 0 ? ErrorColor : SecondaryTextColor;
        Widgets.DrawTextureFitted(infoRect.ContractedBy(2f), TexButton.Info, 1f);
        TooltipHandler.TipRegion(infoRect, info);
    }

    private void DrawLimitsRow(Rect rect, Thing item, CustomStackLimitService? stackLimits)
    {
        var original = stackLimits?.GetOriginalLimit(item.def) ?? item.def.stackLimit;
        var x = DrawInline(rect.x, rect, "Limit", GameFont.Tiny, SecondaryTextColor) + 6f;
        x = DrawInline(x, rect, item.def.stackLimit.ToString("N0", CultureInfo.InvariantCulture),
            GameFont.Small, Color.white) + 20f;
        x = DrawInline(x, rect, "Original", GameFont.Tiny, SecondaryTextColor) + 6f;
        DrawInline(x, rect, original.ToString("N0", CultureInfo.InvariantCulture),
            GameFont.Small, SecondaryTextColor);

        if (stackLimits?.IsSupported(item.def) == true && !stackLimits.IsEnabled(item.def))
        {
            var enableRect = new Rect(rect.xMax - 144f, rect.y, 144f, rect.height);
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(enableRect, "Enable 100,000", drawBackground: false,
                overrideTextAnchor: TextAnchor.MiddleRight))
            {
                var enabled = stackLimits.TryEnable(item.def, out var result);
                SetFeedback(enabled ? "Limit enabled" : result, !enabled);
            }

            TooltipHandler.TipRegion(enableRect, PersistentLimitInfo);
        }
    }

    private void DrawStackRow(Rect rect, Thing item, CustomStackLimitService? stackLimits)
    {
        DrawText(new Rect(rect.x, rect.y, 48f, rect.height), "Stack", GameFont.Small, SecondaryTextColor);
        var fieldRect = new Rect(rect.x + 56f, rect.y, 160f, rect.height);
        Text.Font = GameFont.Medium;
        GUI.color = Color.white;

        // Keep raw input so incomplete/invalid numbers remain visible for validation.
        var edited = Widgets.TextField(fieldRect, amountBuffer);
        if (edited != amountBuffer)
        {
            amountBuffer = edited;
            ClearFeedback();
        }

        var validAmount = ItemEditorService.TryParseStackCount(amountBuffer, item.def.stackLimit,
            out var desiredAmount, out var inputError);
        var applyRect = new Rect(fieldRect.xMax + 8f, rect.y, 80f, rect.height);
        Text.Font = GameFont.Small;
        var previousEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previousEnabled && validAmount;
            if (Widgets.ButtonText(applyRect, "Apply", active: validAmount))
            {
                if (ItemEditorService.TrySetStackCount(item, amountBuffer, out var result))
                {
                    amountBuffer = item.stackCount.ToString(CultureInfo.InvariantCulture);
                    SetFeedback("Applied", false);
                }
                else
                {
                    SetFeedback(result, true);
                }
            }
        }
        finally
        {
            GUI.enabled = previousEnabled;
        }

        if (!validAmount || desiredAmount != item.stackCount)
        {
            DrawText(new Rect(applyRect.xMax + 12f, rect.y, rect.xMax - applyRect.xMax - 12f, rect.height),
                $"Current {item.stackCount:N0}", GameFont.Tiny, SecondaryTextColor);
        }

        TooltipHandler.TipRegion(fieldRect,
            $"Current stack: {item.stackCount:N0}\nWhole number from 1 to {item.def.stackLimit:N0}. Changes require Apply.");
        var persistenceError = stackLimits?.IsSupported(item.def) == true
            ? stackLimits.PersistenceError : string.Empty;
        DrawFeedback(new Rect(rect.x, rect.yMax + 8f, rect.width, 22f), validAmount, inputError, persistenceError);
    }

    private void DrawFeedback(Rect rect, bool validAmount, string inputError, string persistenceError)
    {
        if (!feedbackIsError && Time.realtimeSinceStartup >= feedbackUntil)
        {
            ClearFeedback();
        }

        var message = feedback.Length > 0 ? feedback : !validAmount ? inputError
            : persistenceError.Length > 0 ? "Settings need attention" : string.Empty;
        if (message.Length == 0)
        {
            return;
        }

        var isError = feedback.Length > 0 ? feedbackIsError : !validAmount || persistenceError.Length > 0;
        DrawText(rect, message, isError ? GameFont.Small : GameFont.Tiny, isError ? ErrorColor : SuccessColor);
        TooltipHandler.TipRegion(rect, feedback.Length == 0 && validAmount ? persistenceError : message);
    }

    private void SetFeedback(string message, bool isError)
    {
        feedback = message;
        feedbackIsError = isError;
        feedbackUntil = Time.realtimeSinceStartup + 3f;
    }

    private void ClearFeedback()
    {
        feedback = string.Empty;
        feedbackIsError = false;
        feedbackUntil = 0f;
    }

    private static float DrawInline(float x, Rect row, string text, GameFont font, Color color)
    {
        Text.Font = font;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        GUI.color = color;
        var width = Text.CalcSize(text).x;
        Widgets.Label(new Rect(x, row.y, width, row.height), text);
        return x + width;
    }

    private static void DrawText(Rect rect, string text, GameFont font, Color color)
    {
        Text.Font = font;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        GUI.color = color;
        Widgets.Label(rect, Text.ClampTextWithEllipsis(rect, text));
    }
}
