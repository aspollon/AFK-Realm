using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// "Play": the realm list of the game client set to this server, then the game started. The client reads the
    /// address in two places: Data\&lt;locale&gt;\realmlist.wtf, and "SET realmList" in WTF\Config.wtf, which wins when
    /// it is there (the CoA launcher and the login screen write it). Changing only realmlist.wtf therefore often did
    /// nothing. The game started is the 64-bit CoA64.exe; Ascension.exe only when a client has no CoA64.exe.
    /// </summary>
    static class GameLaunch
    {
        public const string LocalAddress = "127.0.0.1";

        /// <summary>The exe "Play" starts: our AFK64.exe, else the package's CoACustomRace.exe (2.0) or CoA64.exe
        /// (1.9.x), else Ascension.exe, else Wow.exe (a normal 3.3.5a client for a stock AzerothCore server); null when
        /// the folder has none of them.</summary>
        public static string GameExe(string client)
        {
            foreach (string name in new[] { CoaClientExe.Afk, CoaClientExe.V2, CoaClientExe.Legacy, "Ascension.exe", "Wow.exe" })
            {
                string file = Os.Resolve(client, name);
                if (File.Exists(file)) return file;
            }
            return null;
        }

        /// <summary>Sets the realm address in every realmlist.wtf of the client and in WTF\Config.wtf. Returns the
        /// files that were changed.</summary>
        public static List<string> SetRealmlist(string client, string address)
        {
            var changed = new List<string>();
            string data = Os.Resolve(client, "Data");
            var lists = new List<string>();
            if (Directory.Exists(data))
            {
                foreach (string dir in Directory.GetDirectories(data))
                    if (Regex.IsMatch(Path.GetFileName(dir), "^[a-z]{2}[A-Z]{2}$")) lists.Add(Path.Combine(dir, "realmlist.wtf"));
                string direct = Os.Resolve(data, "realmlist.wtf");
                if (File.Exists(direct)) lists.Add(direct);
            }
            string top = Os.Resolve(client, "realmlist.wtf");
            if (File.Exists(top)) lists.Add(top);
            foreach (string file in lists)
                if (WriteRealmlistWtf(file, address)) changed.Add(file);

            string config = Os.Resolve(Os.Resolve(client, "WTF"), "Config.wtf");
            if (File.Exists(config) && WriteConfigWtf(config, address)) changed.Add(config);
            return changed;
        }

        /// <summary>"set realmlist ..." replaced (or added); other lines such as a patchlist are kept.</summary>
        static bool WriteRealmlistWtf(string file, string address)
        {
            string old = File.Exists(file) ? File.ReadAllText(file) : "";
            var lines = old.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            string want = "set realmlist " + address;
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
                if (Regex.IsMatch(lines[i], @"^\s*set\s+realmlist\s", RegexOptions.IgnoreCase))
                {
                    if (found) { lines.RemoveAt(i--); continue; }
                    lines[i] = want;
                    found = true;
                }
            if (!found) lines.Insert(0, want);
            string text = string.Join("\r\n", lines) + "\r\n";
            if (text == old) return false;
            File.WriteAllText(file, text, new UTF8Encoding(false));
            return true;
        }

        static bool WriteConfigWtf(string file, string address)
        {
            string old = File.ReadAllText(file);
            string want = "SET realmList \"" + address + "\"";
            var lines = old.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
                if (Regex.IsMatch(lines[i], @"^\s*SET\s+realmList\s", RegexOptions.IgnoreCase))
                {
                    if (found) { lines.RemoveAt(i--); continue; }
                    lines[i] = want;
                    found = true;
                }
            if (!found) lines.Add(want);
            string text = string.Join("\r\n", lines) + "\r\n";
            if (text == old) return false;
            File.WriteAllText(file, text, new UTF8Encoding(false));
            return true;
        }

        /// <summary>Sets the realm list to this PC and starts the game. Returns the exe that was started.</summary>
        public static string Play(string client)
        {
            string exe = GameExe(client);
            if (exe == null) throw new InvalidOperationException(L.F("The game folder {0} has no CoA64.exe, Ascension.exe or Wow.exe.", client));
            if (ClientAddons.ClientRunning(client)) throw new InvalidOperationException(L.T("The game is already running."));
            SetRealmlist(client, LocalAddress);
            var start = new ProcessStartInfo(exe) { WorkingDirectory = client, UseShellExecute = true };
            using (Process.Start(start)) { }
            return exe;
        }

        /// <summary>The realm address the other players use ("Play with others"), or null when none is set.</summary>
        public static string SharedAddress(Install inst)
        {
            try
            {
                string address = (Accounts.GetRealmAddress(inst) ?? "").Trim();
                return address.Length == 0 || address == LocalAddress || address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? null : address;
            }
            catch { return null; }
        }

        /// <summary>A player ZIP gets Data\enUS\realmlist.wtf with the shared address, and a line for its READ ME.</summary>
        public static string AddToZip(ZipArchive zip, Install inst)
        {
            string address = SharedAddress(inst);
            if (address == null) return null;
            var entry = zip.CreateEntry("Data/enUS/realmlist.wtf");
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write("set realmlist " + address + "\r\n");
            return L.F("The realm address of this server is {0}; Data\\enUS\\realmlist.wtf in this ZIP is set to it. If your WTF\\Config.wtf has a line SET realmList, change it to SET realmList \"{0}\" as well (that line wins).", address);
        }
    }
}
