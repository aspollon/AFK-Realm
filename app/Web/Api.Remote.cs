using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Access from other computers, switched in the page.</summary>
    static partial class Api
    {
        static object RemoteJson()
        {
            return new
            {
                enabled = RemoteAccess.Enabled, running = RemoteAccess.Running || Program.Opts.Listen != null, byCommandLine = Program.Opts.Listen != null,
                port = Program.Opts.Listen != null ? WebHost.Port : RemoteAccess.Port, hasPassword = RemoteAccess.HasPassword,
                addresses = RemoteAccess.Addresses(), error = RemoteAccess.Error, windows = Os.Windows
            };
        }

        static void MapRemote(WebApplication app)
        {
            Get(app, "/api/remote", c => RemoteJson());
            Post(app, "/api/remote", a =>
            {
                if (Program.Opts.Listen != null) throw new UserError(L.T("AFK Realm was started with --listen; that start decides about the access from other computers."));
                RemoteAccess.Set(a.Bool("enabled"), a.Str("password"), a.Int("port"));
                return RemoteJson();
            });
        }
    }
}
