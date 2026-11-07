using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Admin side of the AFK Client launcher builder.</summary>
    static partial class Api
    {
        static void MapLauncher(WebApplication app)
        {
            Get(app, "/api/launcher", c => LauncherBuilder.Status(Inst));
            Post(app, "/api/launcher/build", a =>
            {
                var inst = Inst;
                return Jobs.Run(L.T("Building the AFK Client ..."), (job, st) =>
                {
                    LauncherBuilder.Build(inst, (text, x, y) =>
                    {
                        job.Status = text;
                        job.Sub = y > 0 ? (double)x / y : -1;
                        job.Changed(false);
                    });
                    job.Status = L.T("The AFK Client package is ready.");
                    job.Message = L.T("The AFK Client package is ready.");
                    job.Payload = LauncherBuilder.Status(inst);
                }).Snapshot();
            });
            app.MapGet("/api/launcher/package", ctx => LauncherBuilder.Package(ctx, Inst));
        }
    }
}
