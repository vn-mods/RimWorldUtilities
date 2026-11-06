using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class CustomStackLimitService
{
    // Only these definitions can be enabled. Settings entries alone cannot add support.
    private static readonly Dictionary<string, int> SupportedLimits = new()
    {
        ["Steel"] = 100_000
    };

    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Action<ThingDef> notifyLimitChanged;
    private readonly Dictionary<string, ThingDef> definitions = new();
    private CustomStackLimitSettings persisted;

    public bool IsInitialized { get; private set; }
    public string PersistenceError { get; private set; } = string.Empty;

    public CustomStackLimitService(CustomStackLimitSettings settings,
        Action<CustomStackLimitSettings> persist, Action<ThingDef> notifyLimitChanged)
    {
        this.settings = settings;
        this.persist = persist;
        this.notifyLimitChanged = notifyLimitChanged;
        persisted = settings.Copy();
    }

    public void Initialize(Func<string, ThingDef?> findDefinition)
    {
        if (IsInitialized)
        {
            return;
        }

        foreach (var supported in SupportedLimits)
        {
            var definition = findDefinition(supported.Key);
            if (definition == null)
            {
                continue;
            }

            definitions.Add(supported.Key, definition);
            if (!settings.OriginalLimits.TryGetValue(supported.Key, out var original) || original < 1)
            {
                settings.OriginalLimits[supported.Key] = definition.stackLimit;
            }

            if (settings.EnabledLimits.TryGetValue(supported.Key, out var savedLimit))
            {
                // Keep a higher observed limit across later restarts, even without an editor Apply.
                TryPersistLimit(definition, out _);
                // Reapply before maps spawn; never reduce a higher limit from another mod.
                definition.stackLimit = Math.Max(definition.stackLimit, Math.Max(savedLimit, supported.Value));
            }
        }

        IsInitialized = true;
    }

    public bool IsSupported(ThingDef definition) =>
        definitions.TryGetValue(definition.defName, out var resolved) && ReferenceEquals(resolved, definition);

    public bool IsEnabled(ThingDef definition) =>
        IsSupported(definition) && settings.EnabledLimits.ContainsKey(definition.defName);

    public int GetOriginalLimit(ThingDef definition) =>
        IsSupported(definition) && settings.OriginalLimits.TryGetValue(definition.defName, out var original)
            ? original : definition.stackLimit;

    public bool TryEnable(ThingDef definition, out string message)
    {
        if (!IsInitialized || !IsSupported(definition) || definition.category != ThingCategory.Item ||
            definition.stackLimit <= 1 || definition.saveCompressible)
        {
            message = "Persistent custom limits are available only for the supported Steel definition.";
            return false;
        }

        if (!TryPersistLimit(definition, out message))
        {
            return false;
        }

        ApplyLimit(definition);
        message = $"Persistent Steel limit enabled: {definition.stackLimit}. Quantities were not changed.";
        return true;
    }

    public bool TryPrepareForStackEdit(ThingDef definition, out string error)
    {
        if (!IsInitialized)
        {
            error = "Stack-limit initialization has not completed. Editing is unavailable.";
            return false;
        }

        if (IsEnabled(definition))
        {
            if (definition.saveCompressible)
            {
                error = "Steel's definition uses compressed saving. Persistent count editing is unavailable.";
                return false;
            }

            // A higher limit from another mod must also be durable before we permit edits.
            if (!TryPersistLimit(definition, out error))
            {
                return false;
            }

            ApplyLimit(definition);
        }

        error = string.Empty;
        return true;
    }

    private bool TryPersistLimit(ThingDef definition, out string error)
    {
        var name = definition.defName;
        settings.EnabledLimits.TryGetValue(name, out var savedLimit);
        var limit = Math.Max(definition.stackLimit, Math.Max(savedLimit, SupportedLimits[name]));
        var original = GetOriginalLimit(definition);
        if (persisted.EnabledLimits.TryGetValue(name, out var durableLimit) && durableLimit >= limit &&
            persisted.OriginalLimits.TryGetValue(name, out var durableOriginal) && durableOriginal == original)
        {
            PersistenceError = string.Empty;
            error = string.Empty;
            return true;
        }

        var candidate = settings.Copy();
        candidate.EnabledLimits[name] = limit;
        candidate.OriginalLimits[name] = original;
        try
        {
            persist(candidate);
        }
        catch (Exception exception)
        {
            error = $"Could not save persistent stack limits: {exception.Message}";
            PersistenceError = error;
            return false;
        }

        settings.CopyFrom(candidate);
        persisted = candidate.Copy();
        PersistenceError = string.Empty;
        error = string.Empty;
        return true;
    }

    private void ApplyLimit(ThingDef definition)
    {
        var limit = Math.Max(definition.stackLimit, settings.EnabledLimits[definition.defName]);
        if (limit > definition.stackLimit)
        {
            definition.stackLimit = limit;
            notifyLimitChanged(definition);
        }
    }
}
