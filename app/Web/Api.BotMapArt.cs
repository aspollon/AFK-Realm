using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The real continent and zone maps, extracted from the player's own game client with the bundled SquidBots
    /// map tools. Nothing is shipped with AFK Realm; the images live under Dependencies\BotMaps and are served locally.
    /// </summary>
    static class BotMapArt
    {
        static readonly object Gate = new object();
        static Process Running;
        static int Done, Total;
        static string State = "", Log = "";
        static DateTime Started;

        public static string Root(Install inst) { return Path.Combine(inst.Root, "Dependencies", "BotMaps"); }
        public static string MapsDir(Install inst) { return Path.Combine(Root(inst), "maps"); }
        public static string ToolsDir(Install inst) { return Path.Combine(Root(inst), "tools"); }

        static string Python(Install inst)
        {
            try { if (File.Exists(inst.PythonExe)) return inst.PythonExe; } catch { }
            return Os.Windows ? "python" : "python3";
        }

        static void EnsureTools(Install inst)
        {
            string root = Root(inst), marker = Path.Combine(root, ".afk-tools-version");
            try { if (File.Exists(marker) && File.ReadAllText(marker).Trim() == Product.Version) return; } catch { }
            Directory.CreateDirectory(root);
            using (var stream = typeof(BotMapArt).Assembly.GetManifestResourceStream("CoAInstaller.botmaps.zip"))
            {
                if (stream == null) throw new InvalidOperationException(L.T("The map extraction tools are missing in this build."));
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                        string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
            }
            File.WriteAllText(marker, Product.Version, new UTF8Encoding(false));
        }

        public static object Status(Install inst)
        {
            string maps = MapsDir(inst);
            var available = new List<string>();
            foreach (string id in new[] { "0", "1", "530", "571" })
                if (File.Exists(Path.Combine(maps, id + ".png"))) available.Add(id);
            var zones = new List<string>();
            try
            {
                string zoneDir = Path.Combine(maps, "zones");
                if (Directory.Exists(zoneDir))
                    foreach (var file in Directory.GetFiles(zoneDir, "*.png")) zones.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch { }
            bool running;
            lock (Gate) running = Running != null && !Running.HasExited;
            string client = ClientAddons.ClientFolder;
            string dbc = Path.Combine(inst.DataDir, "dbc");
            return new
            {
                available = available.Count > 0,
                maps = available,
                zones,
                extracting = running,
                done = Done,
                total = Total,
                progress = Total > 0 ? Math.Round((double)Done / Total, 3) : 0,
                state = State,
                log = Log,
                client = client ?? "",
                dbc,
                hasClient = !string.IsNullOrEmpty(client) && ClientAddons.IsClientFolder(client),
                hasDbc = Directory.Exists(dbc) && File.Exists(Path.Combine(dbc, "ChrClasses.dbc")),
                mapsDir = maps,
                version = MapVersion(inst)
            };
        }

        public static object Extract(Install inst, string folder)
        {
            lock (Gate)
            {
                if (Running != null && !Running.HasExited) return Status(inst);
                string client = string.IsNullOrWhiteSpace(folder) ? ClientAddons.ClientFolder : folder.Trim();
                if (string.IsNullOrEmpty(client) || !ClientAddons.IsClientFolder(client))
                    throw new UserError(L.T("Choose the game client folder (the one holding Data\\common.MPQ) first."));
                if (!string.IsNullOrWhiteSpace(folder)) ClientAddons.ClientFolder = client;
                string dbc = Path.Combine(inst.DataDir, "dbc");
                if (!Directory.Exists(dbc) || !File.Exists(Path.Combine(dbc, "ChrClasses.dbc")))
                    throw new UserError(L.T("The server's DBC folder was not found. Create the map data first."));
                EnsureTools(inst);
                string tool = Path.Combine(ToolsDir(inst), "gen_art.py");
                if (!File.Exists(tool)) throw new UserError(L.T("The bundled map extraction tools are missing."));

                var psi = new ProcessStartInfo(Python(inst))
                {
                    WorkingDirectory = Root(inst), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                psi.ArgumentList.Add("-B");
                psi.ArgumentList.Add(tool);
                psi.ArgumentList.Add("--client");
                psi.ArgumentList.Add(client);
                psi.ArgumentList.Add("--dbc");
                psi.ArgumentList.Add(dbc);
                psi.ArgumentList.Add("--root");
                psi.ArgumentList.Add(Root(inst));

                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => Read(e.Data);
                p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Read(e.Data); };
                p.Exited += (s, e) => { lock (Gate) { State = p.ExitCode == 0 ? L.T("Map images are ready.") : L.F("Map extraction ended with code {0}.", p.ExitCode); } };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                Running = p; Done = 0; Total = 0; State = L.T("Opening the client archives …"); Log = ""; Started = DateTime.Now;
            }
            return Status(inst);
        }

        static void Read(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (Gate)
            {
                Log = (Log + line.Trim() + "\n");
                if (Log.Length > 6000) Log = Log.Substring(Log.Length - 6000);
                var m = Regex.Match(line, @"^STEP\s+(\d+)/(\d+)\s+(.*)$");
                if (m.Success) { int.TryParse(m.Groups[1].Value, out Done); int.TryParse(m.Groups[2].Value, out Total); State = m.Groups[3].Value.Trim(); }
                else if (line.StartsWith("DONE", StringComparison.Ordinal)) State = L.T("Map images are ready.");
                else if (line.StartsWith("ERROR", StringComparison.Ordinal)) State = line.Trim();
            }
        }

        public static string MapFile(Install inst, string name)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (!Regex.IsMatch(name, @"^(0|1|530|571)$")) return null;
            string file = Path.Combine(MapsDir(inst), name + ".png");
            return File.Exists(file) ? file : null;
        }

        public static string ZoneFile(Install inst, string name)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9]+$")) return null;
            string file = Path.Combine(MapsDir(inst), "zones", name + ".png");
            return File.Exists(file) ? file : null;
        }

        public static string MapVersion(Install inst)
        {
            try { return File.GetLastWriteTimeUtc(MapsDir(inst)).Ticks.ToString(); } catch { return "0"; }
        }
    }

    static partial class Api
    {
        static void MapBotMapArt(WebApplication app)
        {
            Get(app, "/api/bots/mapart/status", c => BotMapArt.Status(Inst));
            Post(app, "/api/bots/mapart/extract", a => BotMapArt.Extract(Inst, a.Str("folder")));

            app.MapGet("/api/bots/mapart/{map}", (HttpContext context) =>
            {
                string name = context.Request.RouteValues["map"] as string;
                string file = BotMapArt.MapFile(Inst, name);
                return file == null ? Results.NotFound() : Results.File(file, "image/png", lastModified: File.GetLastWriteTimeUtc(file));
            });

            app.MapGet("/api/bots/mapart/zones/{zone}", (HttpContext context) =>
            {
                string name = context.Request.RouteValues["zone"] as string;
                string file = BotMapArt.ZoneFile(Inst, name);
                return file == null ? Results.NotFound() : Results.File(file, "image/png", lastModified: File.GetLastWriteTimeUtc(file));
            });
        }
    }
}
