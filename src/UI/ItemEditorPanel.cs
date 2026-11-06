using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using RimWorldUtilities.Services;
using UnityEngine;
using Verse;

namespace RimWorldUtilities.UI;

internal sealed class ItemEditorPanel : UtilitiesPanel
{
    private static readonly Color ErrorColor = new Color(0.96f, 0.66f, 0.56f);
    private static readonly Color SuccessColor = new Color(0.66f, 0.82f, 0.69f);
    private const string ItemEditorInfo =
        "Stack / HP: Apply to save.\n" +
        "Max / Quality: Applied instantly.\n\n" +
        "Large stacks affect all items of this type.\n" +
        "Reset requires restart and may cause item loss.";

    private Thing? selectedItem;
    private string amountBuffer = string.Empty;
    private string hitPointsBuffer = string.Empty;
    private bool hitPointsEdited;
    private readonly ItemPropertiesService properties = new();
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
            hitPointsBuffer = item != null && ItemPropertiesService.CanEditHitPoints(item)
                ? item.HitPoints.ToString(CultureInfo.InvariantCulture) : string.Empty;
            hitPointsEdited = false;
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
        var y = rect.y + 36f;
        var inputError = string.Empty;
        if (ItemEditorService.CanEditStackCount(item))
        {
            DrawLimitsRow(new Rect(rect.x, y, rect.width, 24f), item, stackLimits);
            y += 36f;
            inputError = DrawStackRow(new Rect(rect.x, y, rect.width, 34f), item);
            y += 42f;
        }

        if (ItemPropertiesService.CanEditHitPoints(item))
        {
            var hpError = DrawHitPointsRow(new Rect(rect.x, y, rect.width, 34f), item);
            if (inputError.Length == 0)
            {
                inputError = hpError;
            }

            y += 42f;
        }

        var quality = ItemPropertiesService.GetEditableQuality(item);
        if (quality != null)
        {
            DrawQualityRow(new Rect(rect.x, y, rect.width, 30f), item, quality);
            y += 38f;
        }

        if (y == rect.y + 36f)
        {
            DrawText(new Rect(rect.x, y, rect.width, 22f), "No editable properties.",
                GameFont.Small, SecondaryTextColor);
            y += 30f;
        }

        DrawFeedback(new Rect(rect.x, y, rect.width, 22f), inputError.Length == 0, inputError,
            stackLimits?.GetPersistenceError(item.def) ?? string.Empty);
    }

    private static void DrawHeading(Rect rect, Thing item, CustomStackLimitService? stackLimits)
    {
        var pending = stackLimits?.IsResetPending(item.def) == true;
        var labelRect = new Rect(rect.x, rect.y, rect.width - (pending ? 158f : 32f), 32f);
        DrawText(labelRect, item.LabelCapNoCount, GameFont.Medium, Color.white);
        TooltipHandler.TipRegion(labelRect, item.LabelCapNoCount);

        if (pending)
        {
            var statusRect = new Rect(rect.xMax - 154f, rect.y, 122f, 32f);
            DrawText(statusRect, "Restart required", GameFont.Tiny, SecondaryTextColor);
            TooltipHandler.TipRegion(statusRect,
                "Reset is saved. The active limit stays unchanged until a full game restart. " +
                "Re-check Large stacks before quitting to cancel the reset.");
        }

        var infoRect = new Rect(rect.xMax - 24f, rect.y + 4f, 24f, 24f);
        var persistenceError = stackLimits?.GetPersistenceError(item.def) ?? string.Empty;

        GUI.color = persistenceError.Length > 0 ? ErrorColor : SecondaryTextColor;
        Widgets.DrawTextureFitted(infoRect.ContractedBy(2f), TexButton.Info, 1f);
        TooltipHandler.TipRegion(infoRect, ItemEditorInfo);
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

        if (stackLimits != null && (stackLimits.IsSupported(item.def) || stackLimits.IsEnabled(item.def)))
        {
            var checkboxRect = new Rect(rect.xMax - 144f, rect.y, 144f, rect.height);
            var enabled = stackLimits.IsEnabled(item.def);
            var pending = stackLimits.IsResetPending(item.def);
            var checkedState = enabled && !pending;
            var requested = checkedState;
            Text.Font = GameFont.Small;
            GUI.color = SecondaryTextColor;
            Widgets.CheckboxLabeled(checkboxRect, "Large stacks", ref requested);
            if (requested != checkedState)
            {
                if (!requested)
                {
                    ConfirmReset(item.def, stackLimits);
                }
                else if (pending)
                {
                    var saved = stackLimits.TryCancelReset(item.def, out var result);
                    SetFeedback(result, !saved);
                }
                else
                {
                    var saved = stackLimits.TryEnable(item.def, out var result);
                    SetFeedback(saved ? "Large stacks enabled" : result, !saved);
                }
            }

            TooltipHandler.TipRegion(checkboxRect,
                pending ? "Reset on next full restart. Re-check to cancel; the current limit is still active." :
                "Applies to every item of this type. Uncheck to confirm a destructive reset on the next full restart.");
        }
    }

    private void ConfirmReset(ThingDef definition, CustomStackLimitService stackLimits)
    {
        var text = $"Restore {definition.LabelCap} stack limit to {stackLimits.GetOriginalLimit(definition):N0} after restart?\n\n" +
            "Warning: Excess items may be permanently lost.\nAffects all saves.";
        var dialog = Dialog_MessageBox.CreateConfirmation(text, () =>
        {
            // Capture the confirmed definition, never a later selection. No Thing is mutated.
            var saved = stackLimits.TryScheduleReset(definition, out var result);
            if (ReferenceEquals(selectedItem?.def, definition))
            {
                SetFeedback(result, !saved);
            }
        }, destructive: true, title: "Reset Stack Limit");
        Find.WindowStack.Add(dialog);
        // Add initializes the native rectangle; shorten it while keeping it centered.
        dialog.windowRect = new Rect(dialog.windowRect.x,
            dialog.windowRect.y + (dialog.windowRect.height - 260f) / 2f,
            dialog.windowRect.width, 260f);
    }

    private string DrawStackRow(Rect rect, Thing item)
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
        return validAmount ? string.Empty : inputError;
    }

    private string DrawHitPointsRow(Rect rect, Thing item)
    {
        var maximum = item.MaxHitPoints;
        DrawText(new Rect(rect.x, rect.y, 48f, rect.height), "HP", GameFont.Small, SecondaryTextColor);
        var fieldRect = new Rect(rect.x + 56f, rect.y, 160f, rect.height);
        Text.Font = GameFont.Medium;
        GUI.color = Color.white;
        var edited = Widgets.TextField(fieldRect, hitPointsBuffer);
        if (edited != hitPointsBuffer)
        {
            hitPointsBuffer = edited;
            hitPointsEdited = true;
            ClearFeedback();
        }

        var valid = ItemPropertiesService.TryParseHitPoints(hitPointsBuffer, maximum, out _, out var error);
        var applyRect = new Rect(fieldRect.xMax + 8f, rect.y, 80f, rect.height);
        var maxRect = new Rect(applyRect.xMax + 8f, rect.y, 56f, rect.height);
        Text.Font = GameFont.Small;
        var previousEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previousEnabled && valid;
            if (Widgets.ButtonText(applyRect, "Apply", active: valid))
            {
                if (properties.TrySetHitPoints(item, hitPointsBuffer, out var result))
                {
                    RefreshHitPointsInput(item);
                    SetFeedback("HP applied", false);
                }
                else
                {
                    SetFeedback(result, true);
                }
            }

            // Max recalculates in the service instead of trusting the displayed stat cache.
            GUI.enabled = previousEnabled;
            if (Widgets.ButtonText(maxRect, "Max"))
            {
                if (properties.TrySetMaxHitPoints(item, out var result))
                {
                    RefreshHitPointsInput(item);
                    SetFeedback("HP maxed", false);
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

        DrawText(new Rect(maxRect.xMax + 12f, rect.y, rect.xMax - maxRect.xMax - 12f, rect.height),
            $"{item.HitPoints:N0} / {item.MaxHitPoints:N0}", GameFont.Tiny, SecondaryTextColor);
        TooltipHandler.TipRegion(fieldRect, $"Current / maximum HP. Enter a whole number from 1 to {maximum:N0}. " +
            "Apply changes HP; Max immediately restores the current maximum.");
        return valid ? string.Empty : error;
    }

    private void DrawQualityRow(Rect rect, Thing item, CompQuality quality)
    {
        DrawText(new Rect(rect.x, rect.y, 52f, rect.height), "Quality", GameFont.Small, SecondaryTextColor);
        var buttonRect = new Rect(rect.x + 56f, rect.y, 160f, rect.height);
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        var label = ItemPropertiesService.IsValidQuality(quality.Quality)
            ? QualityUtility.GetLabel(quality.Quality) : "Unknown";
        if (Widgets.ButtonText(buttonRect, label))
        {
            var options = new List<FloatMenuOption>();
            foreach (var choice in Enum.GetValues<QualityCategory>())
            {
                options.Add(new FloatMenuOption(QualityUtility.GetLabel(choice),
                    choice == quality.Quality ? null : () => ChooseQuality(item, choice),
                    revalidateClickTarget: item));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        TooltipHandler.TipRegion(buttonRect, "Choose quality. The selected option applies immediately.");
    }

    private void ChooseQuality(Thing item, QualityCategory quality)
    {
        if (properties.TrySetQuality(item, quality, out var error))
        {
            if (ReferenceEquals(item, selectedItem) && !hitPointsEdited && ItemPropertiesService.CanEditHitPoints(item))
            {
                RefreshHitPointsInput(item);
            }

            SetFeedback("Quality applied", false);
        }
        else
        {
            SetFeedback(error, true);
        }
    }

    private void RefreshHitPointsInput(Thing item)
    {
        hitPointsBuffer = item.HitPoints.ToString(CultureInfo.InvariantCulture);
        hitPointsEdited = false;
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
