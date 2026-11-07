using System.IO;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Admin settings for the public registration site (the site itself has no admin routes).</summary>
    static partial class Api
    {
        static void MapRegistration(WebApplication app)
        {
            Get(app, "/api/registration", c => RegistrationPortal.Status(Inst));
            Post(app, "/api/registration", a =>
            {
                var old = RegistrationPortal.Load(Inst);
                RegistrationPortal.Apply(Inst,
                    a.Bool("enabled", old.Enabled),
                    a.Int("port", old.Port),
                    a.Has("address") ? a.Str("address") : old.Address,
                    a.Has("publicUrl") ? a.Str("publicUrl") : old.PublicUrl,
                    a.Has("clientUrl") ? a.Str("clientUrl") : old.ClientUrl,
                    a.Bool("allowInternet", old.AllowInternet),
                    a.Int("maxAccountsPerIp", old.MaxAccountsPerIp),
                    a.Int("windowMinutes", old.WindowMinutes));
                return RegistrationPortal.Status(Inst);
            });
            Post(app, "/api/registration/client-pack", a =>
            {
                var inst = Inst;
                return Jobs.Run(L.T("Building player client pack ..."), (job, st) =>
                {
                    RegistrationPortal.BuildClientPack(inst, (text, x, y) =>
                    {
                        job.Status = text;
                        job.Sub = y > 0 ? (double)x / y : -1;
                        job.Changed(false);
                    });
                    job.Status = L.T("Player client pack ready.");
                    var file = new FileInfo(RegistrationPortal.ClientPackFile(inst));
                    job.Message = L.F("Player client pack ready: {0} MB. It includes the module add-ons, client patches and a realmlist.", (file.Length / 1024.0 / 1024.0).ToString("0.0"));
                    job.Payload = new { size = file.Length, at = file.LastWriteTimeUtc.ToString("o") };
                }).Snapshot();
            });
        }
    }
}
