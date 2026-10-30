using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The CoA Custom package: the same file-copy setup the Windows window uses.</summary>
    static partial class Api
    {
        static string DefaultPackage()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Path.Combine(downloads, "COA CUSTOM");
        }

        static string CoaPackage(Args a)
        {
            string p = (a.Str("package") ?? "").Trim();
            if (p.Length == 0) p = DefaultPackage();
            return p;
        }

        static string CoaClient(Args a)
        {
            string c = (a.Str("client") ?? "").Trim();
            if (c.Length == 0) c = ClientAddons.ClientFolder ?? "";
            return c;
        }

        static string CoaServer(Args a)
        {
            string s = (a.Str("server") ?? "").Trim();
            if (s.Length == 0) s = Inst.Root;
            return s;
        }

        static object CoaState(Install inst)
        {
            string package = DefaultPackage();
            string root = null;
            try { root = CoaCustom.PackageRoot(package); } catch { }
            return new
            {
                server = inst.Root,
                package,
                packageRoot = root,
                version = CoaCustom.Installed(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA-Custom")),
                client = ClientAddons.ClientFolder ?? "",
                tokenSet = !string.IsNullOrEmpty(Settings.CoaGitHubToken),
                repository = CoaCustom.Repo
            };
        }

        static void MapCoa(WebApplication app)
        {
            Post(app, "/api/coa/state", a => CoaState(Inst));

            Post(app, "/api/coa/token", a =>
            {
                Settings.CoaGitHubToken = (a.Str("token") ?? "").Trim();
                return new { ok = true, tokenSet = !string.IsNullOrEmpty(Settings.CoaGitHubToken) };
            });

            Post(app, "/api/coa/check", a =>
            {
                var inst = Inst;
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                int code = CoaSetup.Run(new[] { "--package", package, "--server", server, "--client", client, "--check" });
                if (code != 0) throw new UserError(L.T("The package could not be checked."));
                return new { ok = true, message = L.T("The package can be copied.") };
            });

            Post(app, "/api/coa/install", a =>
            {
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                if (!Directory.Exists(package)) throw new UserError(L.F("Package folder not found: {0}", package));
                if (!Directory.Exists(client)) throw new UserError(L.F("Game client folder not found: {0}", client));
                if (!Directory.Exists(server)) throw new UserError(L.F("Server folder not found: {0}", server));
                var inst = new Install(server);
                if (!inst.IsInstalled) throw new UserError(L.F("No complete AFK Realm server in: {0}", server));
                return Jobs.Run(L.T("Installing CoA Custom ..."), (job, st) =>
                {
                    st(L.T("Copying the CoA package and building the client patch ..."));
                    int code = CoaSetup.Run(new[] { "--package", package, "--server", server, "--client", client });
                    if (code != 0) throw new UserError(L.T("CoA Custom setup failed. See the AFK Realm log."));
                    job.Message = L.T("CoA Custom is installed.");
                }, (job, error) =>
                {
                    try { RefreshRaces(inst); } catch { }
                    Manager.CheckClient(false);
                }).Snapshot();
            });

            Post(app, "/api/coa/export", a =>
            {
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                if (!Directory.Exists(package)) throw new UserError(L.F("Package folder not found: {0}", package));
                if (!Directory.Exists(client)) throw new UserError(L.F("Game client folder not found: {0}", client));
                var inst = new Install(server);
                if (!inst.IsInstalled) throw new UserError(L.F("No complete AFK Realm server in: {0}", server));
                string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + ".zip");
                const string name = "CoA-Custom-client.zip";
                return Jobs.Run(L.T("Packing the complete CoA Custom client ..."), (job, st) =>
                {
                    CoaSetup.ExportClientZip(file, package, inst, client, (t, x, y) =>
                    {
                        job.Status = t;
                        if (y > 0) job.Sub = (double)x / y;
                        job.Changed(false);
                    });
                    job.Payload = new { url = Files.Offer(file, name, true), name };
                    job.Message = L.T("The complete CoA Custom client ZIP is ready.");
                }).Snapshot();
            });

            Post(app, "/api/coa/download", a =>
            {
                if (string.IsNullOrEmpty(Settings.CoaGitHubToken))
                    throw new UserError(L.T("A GitHub key for CoA Custom is required before downloading."));
                string target = (a.Str("target") ?? "").Trim();
                if (target.Length == 0) target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA-Custom");
                return Jobs.Run(L.T("Downloading CoA Custom ..."), (job, st) =>
                {
                    string version = CoaCustom.DownloadAndExtract(target, (t, x, y) =>
                    {
                        job.Status = t;
                        if (y > 0) job.Sub = (double)x / y;
                        job.Changed(false);
                    });
                    string root = CoaCustom.PackageRoot(target);
                    job.Payload = new { version, package = root ?? target, target };
                    job.Message = L.F("CoA Custom {0} downloaded.", version);
                }).Snapshot();
            });
        }
    }
}
