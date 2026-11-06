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
            service?.Initialize(DefDatabase<ThingDef>.GetNamedSilentFail);
            if (!string.IsNullOrEmpty(service?.PersistenceError))
            {
                Log.Error($"[RimWorldUtilities] {service.PersistenceError} The existing limit was retained; " +
                    "resolve the settings error before editing or changing stack-limit mods.");
            }
        });
    }
}
