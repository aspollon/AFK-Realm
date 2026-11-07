using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// Moves accounts with all their characters between servers through an .afkaccount file
    /// (a zip with a manifest and two SQL dumps).
    ///
    /// Export copies the account's rows unchanged into empty staging schemas and dumps them.
    /// Import loads the dumps into staging schemas and copies them into the server, giving
    /// every account, character, item, mail, pet and equipment set a free id. Both directions
    /// use the same script (engine/account-transfer.sql), which also copes with tables or
    /// columns that only one of the two servers has.
    /// </summary>
    static class AccountTransfer
    {
        public const string Extension = ".afkaccount";
        const int FormatVersion = 1;
        const string StageAuth = "afk_xfer_auth", StageChars = "afk_xfer_chars", StageWork = "afk_xfer_work";

        public class Summary
        {
            public readonly List<string> Accounts = new List<string>();      // "CHRIS (new account)"
            public readonly List<string> Characters = new List<string>();    // "Thrall" or "Thrall -> rename at next login"
            public int Items, Rows;
        }

        /// <summary>An account found in a CoASM backup, with the number of characters it would bring.</summary>
        public sealed class BackupAccount
        {
            public int Id;
            public string Name = "";
            public int Characters;
            public string LastLogin = "";
            public bool IsBot;
            public bool IsSystem;
        }

        static string Template()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoAInstaller.account-transfer.sql"))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        static string Script(string srcAuth, string srcChars, string dstAuth, string dstChars, string accounts, bool identity)
        {
            return "SET @accounts = " + MySql.Quote(accounts) + "; SET @identity = " + (identity ? "1" : "0") + ";\n" +
                Template().Replace("{SRC_AUTH}", srcAuth).Replace("{SRC_CHARS}", srcChars)
                          .Replace("{DST_AUTH}", dstAuth).Replace("{DST_CHARS}", dstChars).Replace("{WORK}", StageWork);
        }

        static void CreateStaging(Install inst, DbLogin root)
        {
            DropStaging(inst, root);
            MySql.Query(inst, root,
                "CREATE DATABASE " + StageWork + ";\n" +
                "CREATE DATABASE " + StageAuth + " CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;\n" +
                "CREATE DATABASE " + StageChars + " CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }

        static void DropStaging(Install inst, DbLogin root)
        {
            try { MySql.Query(inst, root, "DROP DATABASE IF EXISTS " + StageWork + "; DROP DATABASE IF EXISTS " + StageAuth + "; DROP DATABASE IF EXISTS " + StageChars + ";"); }
            catch { }
        }

        /// <summary>Runs the transfer script and turns a MySQL error into a readable message.</summary>
        static Summary Transfer(Install inst, DbLogin root, string script)
        {
            try { return Parse(MySql.Query(inst, root, script)); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(System.Text.RegularExpressions.Regex.Replace(ex.Message, @"^ERROR \d+ \([0-9A-Z]+\)( at line \d+)?:\s*", ""));
            }
        }

        static Summary Parse(List<string> lines)
        {
            var sum = new Summary();
            foreach (var line in lines)
            {
                var f = line.Split('\t');
                if (f[0] == "ACCOUNT" && f.Length >= 5) sum.Accounts.Add(f[1] + " " + (f[4] == "1" ? L.T("(added to the existing account, password from the file)") : L.T("(new account)")));
                else if (f[0] == "CHARACTER" && f.Length >= 4) sum.Characters.Add(f[3] == "1" ? f[1] + " " + L.T("(name already taken: asks for a new name at the next login)") : f[1]);
                else if (f[0] == "TABLE" && f.Length >= 3)
                {
                    int n; if (!int.TryParse(f[2], out n)) continue;
                    sum.Rows += n;
                    if (f[1] == "item_instance") sum.Items = n;
                }
            }
            return sum;
        }

        static string CoreRevision(Install inst)
        {
            try
            {
                string file = Path.Combine(inst.Root, "Dependencies", "revisions.txt");
                var core = File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("core|"));
                return core != null ? core.Split('|').Last() : "";
            }
            catch { return ""; }
        }

        /// <summary>Exports one account with all of its characters into an .afkaccount file.</summary>
        public static Summary Export(Install inst, string account, string file, Action<string> status)
        {
            var root = DbLogin.FromConfig(inst).AsRoot();
            string temp = Path.Combine(Path.GetTempPath(), "afk-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                status(L.T("Preparing the export …"));
                CreateStaging(inst, root);
                // Empty copies of the tables: the dump then carries the structure of this server.
                var tables = MySql.Query(inst, root, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'acore_characters' AND table_type = 'BASE TABLE';");
                var create = new StringBuilder();
                foreach (var t in tables) create.Append("CREATE TABLE " + StageChars + ".`" + t + "` LIKE acore_characters.`" + t + "`;\n");
                foreach (var t in new[] { "account", "account_access", "realmcharacters" })
                    create.Append("CREATE TABLE " + StageAuth + ".`" + t + "` LIKE acore_auth.`" + t + "`;\n");
                MySql.Query(inst, root, create.ToString());

                status(L.T("Collecting the characters, items and mail …"));
                var sum = Transfer(inst, root, Script("acore_auth", "acore_characters", StageAuth, StageChars, account, true));
                if (sum.Characters.Count == 0 && sum.Accounts.Count == 0) throw new InvalidOperationException(L.F("The account \"{0}\" was not found.", account));

                status(L.T("Writing the file …"));
                MySql.Dump(inst, root, StageAuth, Path.Combine(temp, "auth.sql"));
                MySql.Dump(inst, root, StageChars, Path.Combine(temp, "characters.sql"));
                var manifest = new StringBuilder();
                manifest.AppendLine("format=" + FormatVersion);
                manifest.AppendLine("product=" + Product.Name + " " + Product.Version);
                manifest.AppendLine("created=" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
                manifest.AppendLine("core=" + CoreRevision(inst));
                manifest.AppendLine("accounts=" + account.ToUpperInvariant());
                manifest.AppendLine("characters=" + string.Join(", ", sum.Characters));
                File.WriteAllText(Path.Combine(temp, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false));

                if (File.Exists(file)) File.Delete(file);
                ZipFile.CreateFromDirectory(temp, file, CompressionLevel.Optimal, false);
                return sum;
            }
            finally
            {
                DropStaging(inst, root);
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>Reads the manifest of an .afkaccount file (key=value lines).</summary>
        public static Dictionary<string, string> ReadManifest(string file)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(file))
            {
                var entry = zip.GetEntry("manifest.txt");
                if (entry == null || zip.GetEntry("auth.sql") == null || zip.GetEntry("characters.sql") == null)
                    throw new InvalidOperationException(L.F("This is not an {0} account file.", Product.Name));
                using (var r = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            int format;
            if (!result.ContainsKey("format") || !int.TryParse(result["format"], out format) || format > FormatVersion)
                throw new InvalidOperationException(L.F("This account file was made by a newer {0}. Please update {0} first.", Product.Name));
            if (!result.ContainsKey("accounts") || result["accounts"].Length == 0)
                throw new InvalidOperationException(L.T("The account file names no account."));
            return result;
        }

        /// <summary>Imports an .afkaccount file into this server. The worldserver must not be running.</summary>
        public static Summary Import(Install inst, string file, Action<string> status)
        {
            var manifest = ReadManifest(file);
            string temp = Path.Combine(Path.GetTempPath(), "afk-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                status(L.T("Reading the account file …"));
                ZipFile.ExtractToDirectory(file, temp);
                return ImportSql(inst, Path.Combine(temp, "auth.sql"), Path.Combine(temp, "characters.sql"), manifest["accounts"], status);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>Loads the SQL dumps of a CoASM backup into staging and lists the accounts in it.</summary>
        public static List<BackupAccount> InspectSql(Install inst, string authSql, string charactersSql, string botPrefixes, Action<string> status)
        {
            var root = DbLogin.FromConfig(inst).AsRoot();
            CreateStaging(inst, root);
            try
            {
                status(L.T("Reading the accounts in the CoASM backup …"));
                MySql.RunFile(inst, root, StageAuth, authSql);
                MySql.RunFile(inst, root, StageChars, charactersSql);
                var rows = MySql.Query(inst, root,
                    "SELECT a.id, a.username, COALESCE(DATE_FORMAT(a.last_login, '%Y-%m-%d %H:%i'), ''), " +
                    "(SELECT COUNT(*) FROM " + StageChars + ".characters c WHERE c.account = a.id AND (c.deleteDate IS NULL OR c.deleteDate = 0)) " +
                    "FROM " + StageAuth + ".account a ORDER BY a.username;");
                var prefixes = (botPrefixes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                var list = new List<BackupAccount>();
                foreach (string row in rows)
                {
                    var f = row.Split('\t');
                    int id, characters;
                    if (f.Length < 4 || !int.TryParse(f[0], out id) || !int.TryParse(f[3], out characters)) continue;
                    string name = f[1];
                    list.Add(new BackupAccount
                    {
                        Id = id,
                        Name = name,
                        Characters = characters,
                        LastLogin = f[2],
                        IsBot = prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)),
                        IsSystem = name.Equals("AHBOT", StringComparison.OrdinalIgnoreCase) ||
                                   name.Equals("COAMANAGER", StringComparison.OrdinalIgnoreCase)
                    });
                }
                return list;
            }
            finally
            {
                DropStaging(inst, root);
            }
        }

        /// <summary>Imports the named account(s) from SQL dumps prepared by a CoASM backup.</summary>
        public static Summary ImportSql(Install inst, string authSql, string charactersSql, string accounts, Action<string> status)
        {
            if (string.IsNullOrWhiteSpace(accounts))
                throw new InvalidOperationException(L.T("Choose the account to import."));
            var root = DbLogin.FromConfig(inst).AsRoot();
            CreateStaging(inst, root);
            try
            {
                status(L.T("Reading the account file …"));
                MySql.RunFile(inst, root, StageAuth, authSql);
                MySql.RunFile(inst, root, StageChars, charactersSql);

                status(L.T("Importing the characters with new ids …"));
                return Transfer(inst, root, Script(StageAuth, StageChars, "acore_auth", "acore_characters", accounts, false));
            }
            finally
            {
                DropStaging(inst, root);
            }
        }
    }
}
