using Verse;

namespace RimWorldUtilities;

[StaticConstructorOnStartup]
internal static class CustomStackLimitStartup
{
    static CustomStackLimitStartup()
    {
        // Append after startup constructors; the queue drains before the next load event.
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            var service = RimWorldUtilitiesMod.Instance?.StackLimits;
            service?.Initialize(DefDatabase<ThingDef>.AllDefsListForReading);
            if (!string.IsNullOrEmpty(service?.PersistenceError))
            {
                Log.Error($"[RimWorldUtilities] {service.PersistenceError} " +
                    "Saved reset markers still take effect; other saved limits were retained. " +
                    "Resolve the settings error before changing stack-limit mods.");
            }
        });
    }
}
