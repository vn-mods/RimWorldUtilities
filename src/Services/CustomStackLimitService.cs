using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class CustomStackLimitService
{
    public const int DefaultCustomLimit = 100_000;

    private readonly CustomStackLimitSettings settings;
    private readonly Action<CustomStackLimitSettings> persist;
    private readonly Action<ThingDef> notifyLimitChanged;
    private readonly Dictionary<string, ThingDef> definitions = new();
    private readonly Dictionary<string, int> originalLimits = new();
    private readonly Dictionary<string, string> persistenceErrors = new();
    // Startup reset markers remain authoritative even if their disk cleanup fails.
    private readonly HashSet<string> resetAtStartup = new();
    private CustomStackLimitSettings persisted;

    public bool IsInitialized { get; private set; }
    public string PersistenceError => string.Join(Environment.NewLine, persistenceErrors.Values);

    public CustomStackLimitService(CustomStackLimitSettings settings,
        Action<CustomStackLimitSettings> persist, Action<ThingDef> notifyLimitChanged)
    {
        this.settings = settings;
        this.persist = persist;
        this.notifyLimitChanged = notifyLimitChanged;
        persisted = settings.Copy();
    }

    public void Initialize(IEnumerable<ThingDef> resolvedDefinitions)
    {
        if (IsInitialized)
        {
            return;
        }

        // Capture resolved baselines before applying any of our overrides. Only opt-in
        // definitions get saved baseline metadata; unrelated definitions are never changed.
        foreach (var definition in resolvedDefinitions)
        {
            definitions.Add(definition.defName, definition);
            originalLimits[definition.defName] =
                settings.OriginalLimits.TryGetValue(definition.defName, out var original) && original > 0
                    ? original : definition.stackLimit;
        }

        // Definitions are freshly resolved at this process boundary. Leave reset types
        // untouched, including higher limits from other mods. Never assign an old baseline.
        resetAtStartup.UnionWith(settings.PendingResets);
        foreach (var name in resetAtStartup)
        {
            var candidate = settings.Copy();
            candidate.EnabledLimits.Remove(name);
            candidate.PendingResets.Remove(name);
            TrySaveSettings(candidate, name, out _);
        }

        // The existing Steel dictionaries/tags are unchanged: old Steel settings are
        // restored by this same path, including values above the initial default.
        foreach (var saved in new List<KeyValuePair<string, int>>(settings.EnabledLimits))
        {
            if (resetAtStartup.Contains(saved.Key) ||
                !definitions.TryGetValue(saved.Key, out var definition) ||
                !IsOrdinaryItemDefinition(definition) || GetOriginalLimit(definition) <= 1)
            {
                continue; // Keep unresolved/unsupported records; never discard their metadata.
            }

            var limit = saved.Value > 1 ? saved.Value : DefaultCustomLimit;
            TryPersistLimit(definition, limit, out _);
            // Restore even if a previously enabled definition is now compressed: do not
            // silently lower its limit. Further edits are blocked by IsSupported below.
            definition.stackLimit = Math.Max(definition.stackLimit, limit);
        }

        IsInitialized = true;
    }

    private bool IsRegistered(ThingDef definition) =>
        definitions.TryGetValue(definition.defName, out var resolved) && ReferenceEquals(resolved, definition);

    public bool IsSupported(ThingDef definition) =>
        IsRegistered(definition) && IsOrdinaryItemDefinition(definition) &&
        GetOriginalLimit(definition) > 1 && definition.stackLimit > 1 && !definition.saveCompressible;

    public bool IsEnabled(ThingDef definition) =>
        IsRegistered(definition) && !resetAtStartup.Contains(definition.defName) &&
        settings.EnabledLimits.ContainsKey(definition.defName);

    public bool IsResetPending(ThingDef definition) =>
        IsEnabled(definition) && settings.PendingResets.Contains(definition.defName);

    public int GetOriginalLimit(ThingDef definition) =>
        IsRegistered(definition) && originalLimits.TryGetValue(definition.defName, out var original)
            ? original : definition.stackLimit;

    public string GetPersistenceError(ThingDef definition) =>
        persistenceErrors.TryGetValue(definition.defName, out var error) ? error : string.Empty;

    public bool TryEnable(ThingDef definition, out string message)
    {
        if (!IsInitialized || !IsSupported(definition))
        {
            message = "Large stacks require an ordinary, non-compressed stackable item.";
            return false;
        }

        if (!TryPersistLimit(definition, DefaultCustomLimit, out message, cancelReset: true))
        {
            return false;
        }

        resetAtStartup.Remove(definition.defName);
        ApplyLimit(definition);
        message = $"Large stacks enabled for {definition.defName}: {definition.stackLimit}. Quantities were not changed.";
        return true;
    }

    public bool TryScheduleReset(ThingDef definition, out string message)
    {
        if (!IsInitialized || !IsEnabled(definition))
        {
            message = "Large stacks are not enabled for this item type.";
            return false;
        }

        var candidate = settings.Copy();
        candidate.PendingResets.Add(definition.defName);
        if (!TrySaveSettings(candidate, definition.defName, out message))
        {
            return false;
        }

        message = "Restart required";
        return true;
    }

    public bool TryCancelReset(ThingDef definition, out string message)
    {
        if (!IsInitialized || !IsResetPending(definition))
        {
            message = "No reset is pending for this item type.";
            return false;
        }

        var candidate = settings.Copy();
        candidate.PendingResets.Remove(definition.defName);
        if (!TrySaveSettings(candidate, definition.defName, out message))
        {
            return false;
        }

        message = "Reset cancelled";
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
            if (!IsSupported(definition))
            {
                error = "This item definition no longer supports persistent large-stack editing.";
                return false;
            }

            // A higher limit from another mod must also be durable before we permit edits.
            var savedLimit = settings.EnabledLimits[definition.defName];
            if (!TryPersistLimit(definition, savedLimit > 1 ? savedLimit : DefaultCustomLimit, out error))
            {
                return false;
            }

            ApplyLimit(definition);
        }

        error = string.Empty;
        return true;
    }

    private bool TryPersistLimit(ThingDef definition, int requestedLimit, out string error,
        bool cancelReset = false)
    {
        var name = definition.defName;
        settings.EnabledLimits.TryGetValue(name, out var savedLimit);
        var limit = Math.Max(definition.stackLimit, Math.Max(savedLimit, requestedLimit));
        var original = GetOriginalLimit(definition);
        if (persisted.EnabledLimits.TryGetValue(name, out var durableLimit) && durableLimit >= limit &&
            persisted.OriginalLimits.TryGetValue(name, out var durableOriginal) && durableOriginal == original &&
            (!cancelReset || !persisted.PendingResets.Contains(name)))
        {
            persistenceErrors.Remove(name);
            error = string.Empty;
            return true;
        }

        var candidate = settings.Copy();
        candidate.EnabledLimits[name] = limit;
        candidate.OriginalLimits[name] = original;
        if (cancelReset)
        {
            candidate.PendingResets.Remove(name);
        }

        return TrySaveSettings(candidate, name, out error);
    }

    private bool TrySaveSettings(CustomStackLimitSettings candidate, string name, out string error)
    {
        if (persisted.Matches(candidate))
        {
            persistenceErrors.Remove(name);
            error = string.Empty;
            return true;
        }

        try
        {
            persist(candidate);
        }
        catch (Exception exception)
        {
            error = $"Could not save large-stack settings for {name}: {exception.Message}";
            persistenceErrors[name] = error;
            return false;
        }

        settings.CopyFrom(candidate);
        persisted = candidate.Copy();
        persistenceErrors.Remove(name);
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

    private static bool IsOrdinaryItemDefinition(ThingDef definition)
    {
        var type = definition.thingClass;
        return definition.category == ThingCategory.Item && type != null &&
            typeof(Thing).IsAssignableFrom(type) && !typeof(Pawn).IsAssignableFrom(type) &&
            !typeof(Building).IsAssignableFrom(type) && !typeof(Corpse).IsAssignableFrom(type) &&
            !typeof(MinifiedThing).IsAssignableFrom(type);
    }
}
