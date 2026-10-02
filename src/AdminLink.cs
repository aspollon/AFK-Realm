using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>The server answered a command with an error (for example "Player not found").</summary>
    class CommandFailedException : Exception
    {
        public CommandFailedException(string message) : base(message) { }
    }

    /// <summary>
    /// The line to the running worldserver: its built-in SOAP service executes GM commands exactly
    /// like the server window does and returns the answer. AFK Realm switches the service on (for
    /// this PC only) and logs in with an administrator account of its own with a random password.
    /// </summary>
    static class AdminLink
    {
        public const string AccountName = "AFKREALMADMIN";

        static string SecretFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "admin-link.txt"); }
        public static int Port(Install inst) { return Conf.GetInt(inst.WorldConf, "SOAP.Port", 7878); }
        public static bool Enabled(Install inst) { return Conf.Get(inst.WorldConf, "SOAP.Enabled") == "1"; }

        static string Password(Install inst, bool create)
        {
            string file = SecretFile(inst);
            if (File.Exists(file))
            {
                string p = File.ReadAllText(file).Trim();
                if (Regex.IsMatch(p, "^[A-Z0-9]{16}$")) return p;
            }
            if (!create) return null;
            // The core compares passwords in upper case, so only capitals and digits are used.
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[16];
            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            string pw = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, pw);
            return pw;
        }

        /// <summary>Creates the administrator account or brings its password and level in line. Needs the database.</summary>
        public static void EnsureAccount(Install inst)
        {
            string pw = Password(inst, true);
            var rows = MySql.Query(inst, DbLogin.FromConfig(inst), "SELECT id FROM acore_auth.account WHERE username = " + MySql.Quote(AccountName) + ";");
            if (rows.Count == 0) { Accounts.Create(inst, AccountName, pw, 3); return; }
            int id = int.Parse(rows[0], CultureInfo.InvariantCulture);
            Accounts.SetPassword(inst, id, AccountName, pw);
            Accounts.SetLevel(inst, id, 3);
        }

        /// <summary>
        /// Called right before the worldserver starts: switches the service on for this PC only,
        /// moves it to a free port if another program holds the configured one (the worldserver
        /// would refuse to start otherwise) and makes sure the account exists.
        /// </summary>
        public static void Prepare(Install inst)
        {
            if (!File.Exists(inst.WorldConf)) return;
            if (!Enabled(inst)) Conf.Set(inst.WorldConf, "SOAP.Enabled", "1");
            if (Conf.Get(inst.WorldConf, "SOAP.IP") != "127.0.0.1") Conf.Set(inst.WorldConf, "SOAP.IP", "\"127.0.0.1\"");
            int port = Port(inst);
            if (Net.PortOpen(port))
            {
                for (int p = 7878; p < 7900; p++)
                    if (!Net.PortOpen(p)) { Conf.Set(inst.WorldConf, "SOAP.Port", p.ToString(CultureInfo.InvariantCulture)); break; }
            }
            EnsureAccount(inst);
        }

        /// <summary>Why commands cannot be sent right now, or null when the line is open.</summary>
        public static string Problem(Install inst, ServerControl ctl)
        {
            if (ctl.World == null) return "The server is not running. Start it to send commands; looking things up works without it.";
            if (!Enabled(inst)) return "The connection to the server is switched on when the server starts through " + Product.Name + ". Stop and start the server once.";
            if (!Net.PortOpen(Port(inst)))
                return Net.PortOpen(ctl.WorldPort)
                    ? "The connection to the server is switched on when the server starts through " + Product.Name + ". Stop and start the server once."
                    : "The worldserver is still loading. Commands work as soon as it is ready.";
            return null;
        }

        /// <summary>Sends one GM command (with or without the leading dot) and returns the server's answer.</summary>
        public static string Run(Install inst, string command)
        {
            command = (command ?? "").Trim().TrimStart('.').Trim();
            if (command.Length == 0) return "";
            try { return Post(inst, command); }
            catch (UnauthorizedAccessException)
            {
                // The account was changed or deleted in the meantime: put it back and try once more.
                EnsureAccount(inst);
                try { return Post(inst, command); }
                catch (UnauthorizedAccessException) { throw new InvalidOperationException("The server rejected the " + Product.Name + " administrator account. Stop and start the server once."); }
            }
        }

        static string Post(Install inst, string command)
        {
            string pw = Password(inst, true);
            string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:ns1=\"urn:AC\">" +
                "<SOAP-ENV:Body><ns1:executeCommand><command>" + System.Security.SecurityElement.Escape(command) + "</command></ns1:executeCommand></SOAP-ENV:Body></SOAP-ENV:Envelope>";
            byte[] data = Encoding.UTF8.GetBytes(body);
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port(inst) + "/");
            req.Method = "POST";
            req.Proxy = null;                                   // never through a system proxy
            req.KeepAlive = false;
            req.ContentType = "text/xml; charset=utf-8";
            req.ServicePoint.Expect100Continue = false;
            req.Timeout = 30000; req.ReadWriteTimeout = 30000;
            req.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(AccountName + ":" + pw));
            req.ContentLength = data.Length;
            try
            {
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Result(rd.ReadToEnd(), "result");
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) throw new InvalidOperationException("The server did not answer (" + ex.Message + "). Is it still running?");
                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();
                    string text;
                    using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = rd.ReadToEnd();
                    string fault = Result(text, "faultstring");
                    throw new CommandFailedException(fault.Length > 0 ? fault : "The server refused the command (" + (int)resp.StatusCode + ").");
                }
            }
        }

        /// <summary>The text of one element of the SOAP answer, as plain lines.</summary>
        public static string Result(string xml, string element)
        {
            var m = Regex.Match(xml ?? "", "<(?:[\\w-]+:)?" + element + "(?:\\s[^>]*)?>(.*?)</(?:[\\w-]+:)?" + element + ">", RegexOptions.Singleline);
            if (!m.Success) return "";
            string text = WebUtility.HtmlDecode(m.Groups[1].Value);
            // Color and link codes of the game chat mean nothing outside the game.
            text = Regex.Replace(text, @"\|c[0-9a-fA-F]{8}|\|r|\|H[^|]*\|h|\|h", "");
            return string.Join(Environment.NewLine, text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd())).Trim();
        }
    }
}
