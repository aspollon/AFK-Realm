using System;
using System.IO;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    static partial class Api
    {
        /// <summary>"Get help": the logs, crash reports and dumps of server and game client in one ZIP (SupportBundle).</summary>
        static void MapSupport(WebApplication app)
        {
            Post(app, "/api/support/bundle", a =>
            {
                var inst = Inst;
                string client = ClientAddons.ClientFolder;
                return Jobs.Run(L.T("Collecting the files for help …"), (job, st) =>
                {
                    var r = SupportBundle.Make(inst, client != null && Directory.Exists(client) ? client : null, s => st(s));
                    job.Payload = new
                    {
                        zip = r.Zip, name = Path.GetFileName(r.Zip), download = "/api/files/download?k=" + Files.Offer(r.Zip, Path.GetFileName(r.Zip), false),
                        dumpsDownload = r.Dumps == null ? null : "/api/files/download?k=" + Files.Offer(r.Dumps, Path.GetFileName(r.Dumps), false), folder = r.Folder, mb = Math.Round(r.ZipBytes / 1048576.0, 1), files = r.Files,
                        dumps = r.Dumps, dumpsName = r.Dumps == null ? null : Path.GetFileName(r.Dumps), dumpsMb = Math.Round(r.DumpBytes / 1048576.0, 1), dumpFiles = r.DumpFiles
                    };
                    job.Message = r.Zip;
                }).Snapshot();
            });
        }
    }
}
