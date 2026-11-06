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
                    a.Int("maxAccountsPerIp", old.MaxAccountsPerIp),
                    a.Int("windowMinutes", old.WindowMinutes));
                return RegistrationPortal.Status(Inst);
            });
        }
    }
}
