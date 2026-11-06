using Verse;

namespace RimWorldUtilities;

public sealed class RimWorldUtilitiesMod : Mod
{
    public RimWorldUtilitiesMod(ModContentPack content)
        : base(content)
    {
        Log.Message("[RimWorldUtilities] Mod loaded successfully!");
    }
}
