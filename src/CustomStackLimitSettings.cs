using System.Collections.Generic;
using Verse;

namespace RimWorldUtilities;

public sealed class CustomStackLimitSettings : ModSettings
{
    public Dictionary<string, int> EnabledLimits = new();
    public Dictionary<string, int> OriginalLimits = new();

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref EnabledLimits, "enabledStackLimits", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref OriginalLimits, "originalStackLimits", LookMode.Value, LookMode.Value);
        EnabledLimits ??= new();
        OriginalLimits ??= new();
    }

    internal CustomStackLimitSettings Copy() => new()
    {
        EnabledLimits = new Dictionary<string, int>(EnabledLimits),
        OriginalLimits = new Dictionary<string, int>(OriginalLimits)
    };

    internal void CopyFrom(CustomStackLimitSettings other)
    {
        EnabledLimits = new Dictionary<string, int>(other.EnabledLimits);
        OriginalLimits = new Dictionary<string, int>(other.OriginalLimits);
    }

    internal bool Matches(CustomStackLimitSettings other) =>
        Equal(EnabledLimits, other.EnabledLimits) && Equal(OriginalLimits, other.OriginalLimits);

    private static bool Equal(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var value) || value != entry.Value)
            {
                return false;
            }
        }

        return true;
    }
}
