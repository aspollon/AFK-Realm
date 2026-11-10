using System;
using System.IO;

namespace CoAInstaller.Web
{
    // The owner's own changes to the server's source code: .patch files in Patches\core and Patches\mod-playerbots of
    // the server folder. The engine puts them into every build after the module patches (Invoke-OwnPatches), so a
    // change made by hand survives updates and "Build again".
    static class OwnPatches
    {
        // Makes the folders (and a short note) when they are missing and returns the Patches folder.
        public static string Prepare(string root)
        {
            string folder = Path.Combine(root, "Patches");
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "core"));
                Directory.CreateDirectory(Path.Combine(folder, "mod-playerbots"));
                string note = Path.Combine(folder, "READ ME.txt");
                if (!File.Exists(note))
                    File.WriteAllText(note, string.Join(Environment.NewLine, new[]
                    {
                        "Your own changes to the server's source code",
                        "",
                        "Put .patch (or .diff) files here; they go into every build, after the patches of the modules:",
                        "  core\\            changes to the core (Dependencies\\Source)",
                        "  mod-playerbots\\  changes to Playerbots (Dependencies\\Source\\modules\\mod-playerbots)",
                        "They are applied in name order (01-first.patch, 02-second.patch ...).",
                        "",
                        "Do not change the files in Dependencies\\Source directly: every build puts the source code back to the",
                        "downloaded version first, so such a change is lost. Make a patch of it instead, for example:",
                        "  cd Dependencies\\Source",
                        "  git diff > ..\\..\\Patches\\core\\my-change.patch",
                        "(make the change, then run git diff, then build: Maintenance -> Build again).",
                        "",
                        "If a patch no longer fits after an update, the build stops and the server stays as it was;",
                        "adapt the patch or move it out of this folder and build again.",
                    }) + Environment.NewLine);
            }
            catch { }
            return folder;
        }
    }
}
