using System.IO;
using RimWorldUtilities.Patches;
using RimWorldUtilities.Services;
using Verse;

namespace RimWorldUtilities;

public sealed class RimWorldUtilitiesMod : Mod
{
    private readonly CustomStackLimitSettings settings;
    private readonly CustomStackLimitSettingsStore settingsStore;

    internal static RimWorldUtilitiesMod? Instance { get; private set; }
    internal CustomStackLimitService StackLimits { get; }
    internal CraftingService Crafting { get; }
    internal ColonistNeedsService ColonistNeeds { get; }
    internal ResearchSpeedService Research { get; }

    public RimWorldUtilitiesMod(ModContentPack content)
        : base(content)
    {
        // GetSettings reads synchronously here, before the game's definitions are loaded.
        settings = GetSettings<CustomStackLimitSettings>();
        // Match LoadedModManager.GetSettingsFilename (private in the referenced 1.7 API).
        var filename = GenText.SanitizeFilename($"Mod_{Content.FolderName}_{GetType().Name}.xml");
        settingsStore = new CustomStackLimitSettingsStore(Path.Combine(GenFilePaths.ConfigFolderPath, filename));
        StackLimits = new CustomStackLimitService(settings, settingsStore.Save, RefreshMergeables);
        Crafting = new CraftingService(settings, settingsStore.Save);
        Research = new ResearchSpeedService(settings, settingsStore.Save, Crafting.IsEligibleWorker);
        ColonistNeeds = new ColonistNeedsService(settings, settingsStore.Save);
        Instance = this;
        LegendaryCraftingPatch.Install(Crafting);
        LegendaryConstructionPatch.Install(Crafting);
        CraftingSpeedPatch.Install(Crafting);
        BuildingSpeedPatch.Install(Crafting);
        ResearchSpeedPatch.Install(Research);
        ColonistNeedsPatch.Install(ColonistNeeds);
        Log.Message("[RimWorldUtilities] Mod loaded successfully!");
    }

    public override void WriteSettings() => settingsStore.Save(settings);

    private static void RefreshMergeables(ThingDef definition)
    {
        if (Current.Game == null)
        {
            return;
        }

        // A previously full stack now has merge room. Refresh every affected spawned stack.
        foreach (var map in Find.Maps)
        {
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.def == definition)
                {
                    map.listerMergeables.Notify_ThingStackChanged(thing);
                }
            }
        }
    }
}
