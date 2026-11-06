using System;
using System.IO;
using Verse;

namespace RimWorldUtilities.Services;

internal sealed class CustomStackLimitSettingsStore
{
    private readonly string path;

    public CustomStackLimitSettingsStore(string path)
    {
        this.path = path;
    }

    public void Save(CustomStackLimitSettings settings)
    {
        RequireInactiveScribe();
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Scribe.saver.InitSaving(temporaryPath, "SettingsBlock");
            // Use ExposeData directly: Scribe_Deep swallows some serialization exceptions.
            if (!Scribe.EnterNode("ModSettings"))
            {
                throw new IOException("Could not create the mod settings node.");
            }

            settings.ExposeData();
            Scribe.ExitNode();
            Scribe.saver.FinalizeSaving();

            if (!settings.Matches(Read(temporaryPath)))
            {
                throw new IOException("The saved stack-limit settings failed verification.");
            }

            using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Flush(flushToDisk: true);
            }

            // Same-directory replacement keeps the previous settings intact on write failure.
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                Scribe.ForceStop();
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static CustomStackLimitSettings Read(string path)
    {
        RequireInactiveScribe();
        CustomStackLimitSettings settings = null!;
        Scribe.loader.InitLoading(path);
        try
        {
            Scribe_Deep.Look(ref settings, "ModSettings");
        }
        finally
        {
            Scribe.loader.FinalizeLoading();
        }

        return settings ?? throw new IOException("Could not read the stack-limit settings.");
    }

    private static void RequireInactiveScribe()
    {
        if (Scribe.mode != LoadSaveMode.Inactive)
        {
            throw new InvalidOperationException("A save or load is in progress. Try again when it finishes.");
        }
    }
}
