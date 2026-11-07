using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoAInstaller;
using SharpCompress.Archives;
using SharpCompress.Common;
using ZstdSharp;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Reads the account and character dumps from a CoASM backup (7z or zip with auth.sql.zst,
    /// characters.sql.zst and backup.json) and hands them to the normal AFK account transfer.
    /// Playerbots and system accounts are marked so the page can hide them.
    /// </summary>
    static class CoasmImport
    {
        public sealed class Backup : IDisposable
        {
            public string Folder;
            public string AuthSql;
            public string CharactersSql;
            public string Created;
            public string ManagerVersion;
            public string Realm;
            public string BotPrefixes;

            public void Dispose()
            {
                try { if (!string.IsNullOrEmpty(Folder) && Directory.Exists(Folder)) Directory.Delete(Folder, true); } catch { }
            }
        }

        public sealed class InspectInfo
        {
            public string Created = "";
            public string ManagerVersion = "";
            public string Realm = "";
            public List<AccountTransfer.BackupAccount> Accounts = new List<AccountTransfer.BackupAccount>();
        }

        static string Normalize(string path) { return (path ?? "").Replace('\\', '/').TrimStart('/'); }

        /// <summary>True when the archive has a CoASM backup.json, whether it is a .7z or a .zip.</summary>
        public static bool IsCoasm(string file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return false;
                using (var archive = ArchiveFactory.Open(file))
                    return archive.Entries.Any(e => !e.IsDirectory &&
                        Path.GetFileName(Normalize(e.Key)).Equals("backup.json", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>Extracts the auth and characters dumps from the archive and decompresses their zstd streams.</summary>
        public static Backup Read(string file, Action<string> status)
        {
            if (status == null) status = _ => { };
            var backup = new Backup { Folder = Path.Combine(Path.GetTempPath(), "afk-coasm-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(backup.Folder);
            try
            {
                status(L.T("Reading the CoASM backup …"));
                using (var archive = ArchiveFactory.Open(file))
                {
                    var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                    var info = entries.FirstOrDefault(e => Path.GetFileName(Normalize(e.Key)).Equals("backup.json", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(L.T("This is not a CoASM backup: backup.json is missing."));
                    string infoPath = Normalize(info.Key);
                    string root = infoPath.Contains('/') ? infoPath.Substring(0, infoPath.LastIndexOf('/') + 1) : "";
                    ReadInfo(info, backup);

                    var auth = Find(entries, root, "auth.sql.zst", "auth.sql");
                    var chars = Find(entries, root, "characters.sql.zst", "characters.sql");
                    if (auth == null || chars == null)
                        throw new InvalidOperationException(L.T("This CoASM backup contains no auth.sql.zst or characters.sql.zst."));

                    status(L.T("Unpacking the account data …"));
                    backup.AuthSql = Extract(auth, Path.Combine(backup.Folder, "auth.sql"), auth.Key.EndsWith(".zst", StringComparison.OrdinalIgnoreCase));
                    backup.CharactersSql = Extract(chars, Path.Combine(backup.Folder, "characters.sql"), chars.Key.EndsWith(".zst", StringComparison.OrdinalIgnoreCase));

                    var prefixes = new List<string>();
                    foreach (string name in new[] { "playerbots.conf", "mod_coa_playerbots.conf" })
                    {
                        var conf = entries.FirstOrDefault(e => Path.GetFileName(Normalize(e.Key)).Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (conf == null) continue;
                        string text = ReadText(conf);
                        AddPrefix(prefixes, text, "AiPlayerbot.RandomBotAccountPrefix");
                        AddPrefix(prefixes, text, "CoaBots.RandomSpawn.AccountPrefix");
                    }
                    if (prefixes.Count == 0) prefixes.Add("rndbot");
                    backup.BotPrefixes = string.Join(",", prefixes.Distinct(StringComparer.OrdinalIgnoreCase));
                }
                return backup;
            }
            catch
            {
                backup.Dispose();
                throw;
            }
        }

        static IArchiveEntry Find(IEnumerable<IArchiveEntry> entries, string root, string compressed, string plain)
        {
            string wanted = root + compressed, wantedPlain = root + plain;
            return entries.FirstOrDefault(e => Normalize(e.Key).Equals(wanted, StringComparison.OrdinalIgnoreCase)) ??
                   entries.FirstOrDefault(e => Normalize(e.Key).Equals(wantedPlain, StringComparison.OrdinalIgnoreCase));
        }

        static string Extract(IArchiveEntry entry, string target, bool zstd)
        {
            using (var input = entry.OpenEntryStream())
            using (var output = File.Create(target))
            {
                if (zstd)
                    using (var decompressor = new DecompressionStream(input))
                        decompressor.CopyTo(output);
                else
                    input.CopyTo(output);
            }
            return target;
        }

        static string ReadText(IArchiveEntry entry)
        {
            using (var input = entry.OpenEntryStream())
            using (var reader = new StreamReader(input, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        static void AddPrefix(List<string> prefixes, string conf, string key)
        {
            var m = Regex.Match(conf, @"^\s*" + Regex.Escape(key) + @"\s*=\s*""?([^""\r\n]+)""?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            string value = m.Success ? m.Groups[1].Value.Trim() : "";
            if (value.Length > 0) prefixes.Add(value);
        }

        static void ReadInfo(IArchiveEntry entry, Backup backup)
        {
            using (var doc = JsonDocument.Parse(ReadText(entry)))
            {
                var root = doc.RootElement;
                backup.Created = Text(root, "created_at");
                backup.ManagerVersion = Text(root, "manager_version");
                backup.Realm = Text(root, "realm");
            }
        }

        static string Text(JsonElement root, string name)
        {
            JsonElement value;
            return root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }

        public static InspectInfo Inspect(Install inst, string file, Action<string> status)
        {
            using (var backup = Read(file, status))
                return new InspectInfo
                {
                    Created = backup.Created,
                    ManagerVersion = backup.ManagerVersion,
                    Realm = backup.Realm,
                    Accounts = AccountTransfer.InspectSql(inst, backup.AuthSql, backup.CharactersSql, backup.BotPrefixes, status)
                };
        }

        public static AccountTransfer.Summary Import(Install inst, string file, string account, Action<string> status)
        {
            using (var backup = Read(file, status))
                return AccountTransfer.ImportSql(inst, backup.AuthSql, backup.CharactersSql, account, status);
        }
    }
}
