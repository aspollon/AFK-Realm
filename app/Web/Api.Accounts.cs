using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Creating accounts and managing the accounts of real players (password, access level, deleting, moving).</summary>
    static partial class Api
    {
        static void MapAccounts(WebApplication app)
        {
            Post(app, "/api/accounts/create", a =>
            {
                string name = (a.Str("name") ?? "").Trim(), pw = a.Str("password") ?? "", pw2 = a.Str("password2") ?? "";
                int level = Math.Max(0, Math.Min(3, a.Int("level")));
                string err = Accounts.Validate(name, pw, pw2);
                if (err != null) throw new UserError(err);
                var inst = Inst;
                return Jobs.Run(L.T("Create account"), (job, st) =>
                {
                    EnsureDatabase(st); st(L.T("Creating account ..."));
                    try { Accounts.Create(inst, name, pw, level); }
                    catch (Exception ex) { throw new UserError(L.T("The account could not be created:") + "\n" + ex.Message); }
                    job.Message = L.F("Account \"{0}\" was created.", name.ToUpperInvariant());
                }).Snapshot();
            });

            Get(app, "/api/accounts", c =>
            {
                EnsureDatabase(t => { });
                var list = Accounts.ListPlayers(Inst);
                return new
                {
                    accounts = list.Select(x => new { id = x.Id, name = x.Name, level = x.Level, characters = x.Characters, characterCount = x.CharacterCount, lastLogin = x.LastLogin, online = x.Online }).ToArray(),
                    worldRunning = Ctl.World != null,
                    purges = Accounts.PurgesDeletedCharacters(Inst)
                };
            });

            Post(app, "/api/accounts/delete", a =>
            {
                if (Ctl.World != null) throw new UserError(L.T("Stop the server first. Accounts can only be deleted while the worldserver is not running, so no character is still in use."));
                EnsureDatabase(t => { });
                try { Accounts.Delete(Inst, a.Int("id")); }
                catch (Exception ex) { throw new UserError(L.T("The account could not be deleted:") + "\n" + ex.Message); }
                return Ok;
            });

            Post(app, "/api/accounts/password", a =>
            {
                string pw = a.Str("password") ?? "";
                string err = Accounts.Validate("account", pw, a.Str("password2") ?? "");
                if (err != null) throw new UserError(err);
                EnsureDatabase(t => { });
                try { Accounts.SetPassword(Inst, a.Int("id"), a.Str("name") ?? "", pw); }
                catch (Exception ex) { throw new UserError(L.T("The password could not be changed:") + "\n" + ex.Message); }
                return Ok;
            });

            Post(app, "/api/accounts/level", a =>
            {
                EnsureDatabase(t => { });
                try { Accounts.SetLevel(Inst, a.Int("id"), Math.Max(0, Math.Min(3, a.Int("level")))); }
                catch (Exception ex) { throw new UserError(L.T("The access level could not be changed:") + "\n" + ex.Message); }
                return Ok;
            });

            Post(app, "/api/accounts/export", a =>
            {
                string name = a.Str("name") ?? "";
                var inst = Inst;
                return Jobs.Run(L.F("Exporting {0} …", name), (job, st) =>
                {
                    EnsureDatabase(st);
                    string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + AccountTransfer.Extension);
                    var sum = AccountTransfer.Export(inst, name, file, st);
                    job.Payload = new { download = Files.Offer(file, name + AccountTransfer.Extension, true), characters = sum.Characters, items = sum.Items };
                    job.Message = L.F("{0} was exported.", name);
                }).Snapshot();
            });

            Post(app, "/api/accounts/inspect", a =>
            {
                var m = AccountTransfer.ReadManifest(a.Str("path") ?? "");
                string chars, created, accounts;
                m.TryGetValue("characters", out chars); m.TryGetValue("created", out created); m.TryGetValue("accounts", out accounts);
                return new { accounts, characters = chars, created };
            });

            Post(app, "/api/accounts/import", a =>
            {
                if (Ctl.World != null) throw new UserError(L.T("Stop the server first. Characters can only be imported while the worldserver is not running."));
                string file = a.Str("path") ?? "";
                var inst = Inst;
                return Jobs.Run(L.T("Importing …"), (job, st) =>
                {
                    EnsureDatabase(st);
                    var sum = AccountTransfer.Import(inst, file, st);
                    job.Payload = new { accounts = sum.Accounts, characters = sum.Characters, items = sum.Items };
                    job.Message = L.T("Import finished.");
                }).Snapshot();
            });
        }
    }
}
