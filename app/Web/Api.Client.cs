using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The game client: the add-ons and the client patch of the modules, the ZIP for other players, and the client download.</summary>
    static partial class Api
    {
        class ClientLook
        {
            public string Folder, Client;
            public List<ClientAddon> Addons = new List<ClientAddon>();
            public List<string> Leftovers = new List<string>();
            public ClientPatch.Plan Patch = new ClientPatch.Plan();
            public PatchState PatchState = PatchState.None;
            public string PatchProblem, ReadError;
        }

        static ClientLook LookAtClient(Install inst, string folder)
        {
            var look = new ClientLook { Folder = (folder ?? "").Trim() };
            look.Client = ClientAddons.GameRoot(look.Folder);
            try { look.Addons = ClientAddons.Find(inst, look.Client); look.Leftovers = ClientAddons.Leftovers(inst, look.Addons, look.Client); }
            catch (Exception ex) { look.ReadError = L.T("The modules could not be read:") + " " + ex.Message; }
            try { look.Patch = ClientPatch.Collect(inst); look.PatchState = ClientPatch.StateOf(inst, look.Client, look.Patch, out look.PatchProblem); }
            catch (Exception ex) { look.Patch = new ClientPatch.Plan(); look.PatchState = PatchState.None; look.PatchProblem = ex.Message; }
            return look;
        }

        static object ClientJson(ClientLook k)
        {
            string client = k.Client;
            var rows = new List<object>();
            foreach (var a in k.Addons)
                rows.Add(new
                {
                    name = a.Name, module = a.Module, why = a.Why, warn = client != null && a.State != AddonState.Current,
                    state = client == null ? "-" : a.State == AddonState.Current ? L.T("up to date") : a.State == AddonState.Outdated ? L.T("older version") : L.T("not installed")
                });
            if (k.PatchState != PatchState.None)
                rows.Add(new
                {
                    name = L.F("Client patch ({0})", ClientPatch.ArchiveName), module = k.Patch.Empty ? L.T("(removed)") : string.Join(", ", k.Patch.Modules),
                    why = k.PatchProblem ?? (k.Patch.Empty ? L.T("No module needs it any more.") : string.Join(" ", k.Patch.Why)),
                    warn = client != null && k.PatchState != PatchState.Current,
                    state = client == null ? "-" : k.PatchState == PatchState.Current ? L.T("up to date") : k.PatchState == PatchState.Outdated ? L.T("to be built again") :
                        k.PatchState == PatchState.Leftover ? L.T("will be taken out") : L.T("not installed")
                });
            foreach (var name in k.Leftovers)
                rows.Add(new { name, module = L.T("(removed)"), why = L.T("Its module is no longer installed."), warn = true, state = L.T("will be taken out") });

            bool patchDue = k.PatchState == PatchState.Missing || k.PatchState == PatchState.Outdated || k.PatchState == PatchState.Leftover;
            bool due = k.Addons.Any(a => a.State != AddonState.Current) || k.Leftovers.Count > 0 || patchDue;
            bool anything = k.Addons.Count > 0 || k.Leftovers.Count > 0 || k.PatchState != PatchState.None;
            bool cacheShown = k.Addons.Any(a => a.ClearCache) || k.Patch.ClearCache;
            string state;
            if (k.ReadError != null) state = k.ReadError;
            else if (!anything) state = L.T("None of the installed modules brings an add-on or a client patch for the game client.");
            else if (k.Folder.Length == 0) state = L.T("Choose your game folder, the one with the \"Data\" folder.");
            else if (client == null) state = L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself.");
            else if (k.PatchProblem != null) state = L.T("The client patch cannot be built:") + " " + k.PatchProblem;
            else state = due ? L.T("Not everything is in your game yet. Close the game and its launcher, then choose \"Install\".") : L.T("Your game has everything the installed modules bring.");
            string longPath = Os.Windows && client != null && client.Length > 100
                ? L.F("This game folder path is long ({0} characters). Ascension.exe is not long-path aware, so custom textures may be missing; move the client to a short path such as C:\\CoA.", client.Length)
                : null;
            if (longPath != null) state = longPath + " " + state;
            return new
            {
                folder = k.Folder, client, rows, state, stateWarn = client != null && (due || longPath != null),
                cacheShown, cacheChecked = cacheShown && (k.Addons.Any(a => a.ClearCache && a.State != AddonState.Current) || (k.Patch.ClearCache && patchDue)),
                coa = !string.IsNullOrEmpty(Settings.CoaPackage),
                canExport = k.Addons.Count > 0 || !k.Patch.Empty, canInstall = client != null && anything,
                installText = due || !anything ? L.T("Install") : L.T("Install again"),
                patchNeedsClient = !k.Patch.Empty
            };
        }

        static void MapClient(WebApplication app)
        {
            Post(app, "/api/client/look", a =>
            {
                var inst = Inst;
                string folder = a.Has("folder") ? a.Str("folder") : ClientAddons.ClientFolder ?? "";
                return ClientJson(LookAtClient(inst, folder));
            });

            // The button text: a newer client version, or download / repair.
            Post(app, "/api/client/version", a =>
            {
                string client = ClientAddons.GameRoot(a.Str("folder") ?? ClientAddons.ClientFolder ?? "");
                try
                {
                    var manifest = ClientDownload.Latest();
                    string installed = client == null ? null : ClientDownload.InstalledVersion(client);
                    return new
                    {
                        text = installed != null && installed != manifest.Version ? L.F("Update client to {0} …", manifest.Version)
                            : client != null ? L.T("Update / repair client …") : L.F("Download client {0} …", manifest.Version)
                    };
                }
                catch { return new { text = L.T("Download / update client …") }; }
            });

            Post(app, "/api/client/install", a =>
            {
                var inst = Inst;
                RefreshRaces(inst);
                var look = LookAtClient(inst, a.Str("folder"));
                string client = look.Client;
                if (client == null) throw new UserError(L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives."));
                if (ClientAddons.ClientRunning(client))
                    throw new UserError(L.T("The game or its launcher is running from this folder. Please close it first: add-ons are only loaded at the start, and a running game writes its old cache back when it closes."));
                bool clear = a.Bool("clearCache") && (look.Addons.Any(x => x.ClearCache) || look.Patch.ClearCache);
                return Jobs.Run(L.T("Installing into your game …"), (job, st) =>
                {
                    st(L.T("Copying the add-ons …"));
                    string result = ClientAddons.Install(inst, client, look.Addons, look.Leftovers, false);
                    try
                    {
                        string patched = ClientPatch.Install(inst, client, look.Patch, (t, x, y) => { job.Status = t; job.Sub = y > 0 ? (double)x / y : -1; job.Changed(false); });
                        if (patched.Length > 0) result = (result + "\n" + patched).Trim();
                    }
                    finally
                    {
                        string cache = Os.Resolve(client, "Cache");
                        if (clear && Directory.Exists(cache)) { st(L.T("Clearing the game's cache …")); Directory.Delete(cache, true); result += "\n" + L.T("the client's cache cleared"); }
                    }
                    ClientAddons.ClientFolder = client;
                    job.Message = L.T("Done:") + "\n\n" + result;
                }, (job, error) => Manager.CheckClient(false)).Snapshot();
            });

            Post(app, "/api/client/export", a =>
            {
                var inst = Inst;
                var look = LookAtClient(inst, a.Str("folder"));
                if (look.Addons.Count == 0 && look.Patch.Empty) throw new UserError(L.T("None of the installed modules brings an add-on or a client patch for the game client."));
                if (!look.Patch.Empty && look.Client == null) throw new UserError(L.T("Choose your game folder first: the client patch is built from your own game client."));
                string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + ".zip");
                string name = "Game client files for " + Product.Name + " server.zip";
                return Jobs.Run(L.T("Making the ZIP for the other players …"), (job, st) =>
                {
                    ClientAddons.Export(file, look.Addons, inst, look.Client, look.Patch, (t, x, y) => { job.Status = t; job.Sub = y > 0 ? (double)x / y : -1; job.Changed(false); });
                    job.Payload = new { url = Files.Offer(file, name, true), name };
                    job.Message = L.T("Give it to the other players: they unpack it into their game folder (it says how inside).");
                }).Snapshot();
            });

            // Before a download: where it goes, and what the list says.
            Post(app, "/api/client/download/check", a =>
            {
                string folder = (a.Str("folder") ?? "").Trim();
                string target = ClientAddons.GameRoot(folder) ?? folder;
                if (target.Length == 0 || !Path.IsPathRooted(target)) throw new UserError(L.T("Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it."));
                if (ClientAddons.ClientRunning(target)) throw new UserError(L.T("The game or its launcher is running from this folder. Close it before downloading or updating the client."));
                ClientDownload.Manifest manifest;
                try { manifest = ClientDownload.FetchManifest(); }
                catch (Exception ex) { throw new UserError(L.T("The client list could not be read:") + "\n" + ex.Message); }
                Directory.CreateDirectory(target);
                long free = ClientDownload.FreeSpace(target);
                if (free > 0 && free < manifest.TotalBytes + 1024L * 1024 * 1024 && !ClientAddons.IsClientFolder(target))
                    throw new UserError(L.F("The game client needs {0}. The drive only has {1} free.", ClientDownload.FormatBytes(manifest.TotalBytes), ClientDownload.FormatBytes(free)));
                bool foreign = Directory.EnumerateFileSystemEntries(target).Any() && !ClientAddons.IsClientFolder(target);
                return new
                {
                    target, foreign = foreign ? L.F("The folder {0} already contains files and is not a game folder. Download the client into it anyway?", target) : null,
                    question = L.F("Download or update the game client?\n\nVersion: {0}\nFiles: {1}\nFull size: {2}\n\n" +
                        "The files come from the public CoA development launcher. The client data belongs to its owners; only use it as allowed. " +
                        "Your WTF, Cache, add-ons and realmlist are kept, and game files you changed are backed up as .afk-backup.",
                        manifest.Version, manifest.Files.Count, ClientDownload.FormatBytes(manifest.TotalBytes))
                };
            });

            Post(app, "/api/client/download", a =>
            {
                string target = (a.Str("target") ?? "").Trim();
                if (target.Length == 0 || !Path.IsPathRooted(target)) throw new UserError(L.T("Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it."));
                var job = Jobs.Run(L.T("Download game client"), (j, st) =>
                {
                    st(L.T("Reading the client list ..."));
                    var manifest = ClientDownload.FetchManifest();
                    st(L.T("Preparing the game client ..."));
                    ClientDownload.Result result;
                    try
                    {
                        result = ClientDownload.Update(target, manifest, (p, done, total, current) =>
                        {
                            if (total < 1) total = 1;
                            j.Status = p; j.Action = (current ?? "") + "\n" + ClientDownload.FormatBytes(Math.Max(0, done)) + " / " + ClientDownload.FormatBytes(total);
                            j.Sub = Math.Max(0, Math.Min(1, (double)done / total));
                            j.Changed(false);
                        }, () => j.Stop);
                    }
                    catch (OperationCanceledException) { throw new UserError(L.T("The download was stopped. Everything already downloaded is kept; run it again to continue.")); }
                    ClientAddons.ClientFolder = target;
                    j.Message = L.F("The game client {0} is ready.\n\n{1} files downloaded, {2} already present, {3} kept as yours.\n\nInstall the add-ons and the client patch next.",
                        result.Version, result.Downloaded, result.AlreadyPresent, result.Kept);
                    j.Payload = new { folder = target };
                }, (j, error) => { Manager.CheckDownloadUpdates(); Manager.CheckClient(false); });
                job.Cancellable = true;
                return job.Snapshot();
            });
        }
    }
}
