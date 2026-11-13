using System;
using System.IO;

namespace CoAInstaller
{
    /// <summary>
    /// The name of the game's client exe. AFK Realm ships its own build as AFK64.exe; CoA Custom 2.0 names the
    /// package's client CoACustomRace.exe and 1.9.x used CoA64.exe. Every place that patches, replaces or verifies the
    /// client asks here instead of hard-coding a name.
    /// </summary>
    static class CoaClientExe
    {
        public const string Afk = "AFK64.exe";
        public const string Legacy = "CoA64.exe";
        public const string V2 = "CoACustomRace.exe";

        /// <summary>The exe the game starts: our AFK64.exe when present, else the package's own.</summary>
        public static string Name(string client)
        {
            try
            {
                if (!string.IsNullOrEmpty(client) && Directory.Exists(client))
                {
                    if (File.Exists(Path.Combine(client, Afk))) return Afk;
                    if (File.Exists(Path.Combine(client, V2))) return V2;
                    if (File.Exists(Path.Combine(client, Legacy))) return Legacy;
                }
            }
            catch { }
            return Legacy;
        }

        public static string Full(string client) { return Path.Combine(client ?? "", Name(client)); }

        /// <summary>True for a 2.0 client whose exe is CoACustomRace.exe.</summary>
        public static bool IsV2(string client) { return Name(client).Equals(V2, StringComparison.OrdinalIgnoreCase); }
    }
}
