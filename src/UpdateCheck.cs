using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// Looks on GitHub for newer server code (CoA core, Playerbots) and a newer AFK Realm release.
    /// Read-only and quiet: without internet it simply reports nothing.
    /// </summary>
    static class UpdateCheck
    {
        public class Result
        {
            public readonly List<string> Server = new List<string>();   // e.g. "CoA core: 12 new changes"
            public string ToolVersion, ToolUrl;                           // newer AFK Realm release, if any
            public string ToolDownload, ToolSha256;                       // its exe (and checksum, when GitHub lists one)
        }

        public static Result Run(Install inst)
        {
            var r = new Result();
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2 for GitHub
            string revisions = Path.Combine(inst.Root, "Dependencies", "revisions.txt");
            if (File.Exists(revisions))
            {
                foreach (var line in File.ReadAllLines(revisions))
                {
                    var f = line.Split('|');                              // kind|repo url|branch|revision
                    if (f.Length < 4) continue;
                    var m = Regex.Match(f[1], @"github\.com[/:]([^/]+)/([^/.]+)");
                    if (!m.Success || !Regex.IsMatch(f[3], "^[0-9a-f]{40}$")) continue;
                    string name = f[0] == "core" ? "CoA core" : "Playerbots";
                    try
                    {
                        string json = Get("https://api.github.com/repos/" + m.Groups[1].Value + "/" + m.Groups[2].Value + "/compare/" + f[3] + "..." + f[2]);
                        var ahead = Regex.Match(json, @"""ahead_by""\s*:\s*(\d+)");
                        int n = ahead.Success ? int.Parse(ahead.Groups[1].Value) : 0;
                        if (n > 0) r.Server.Add(name + ": " + n + (n == 1 ? " new change" : " new changes"));
                    }
                    catch (WebException ex)
                    {
                        // An unknown revision (history rewritten upstream) still means the build is not current.
                        var resp = ex.Response as HttpWebResponse;
                        if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) r.Server.Add(name + ": new version");
                    }
                    catch { }
                }
            }
            try
            {
                string json = Get("https://api.github.com/repos/" + Product.GitHubRepo + "/releases?per_page=10");
                var parser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                foreach (var item in (object[])parser.DeserializeObject(json))
                {
                    var rel = (Dictionary<string, object>)item;
                    if (Equals(rel["draft"], true)) continue;
                    string tag = Convert.ToString(rel["tag_name"]);
                    if (IsNewer(tag.TrimStart('v', 'V'), Product.Version))
                    {
                        r.ToolVersion = tag.TrimStart('v', 'V'); r.ToolUrl = Convert.ToString(rel["html_url"]);
                        object assets;
                        if (rel.TryGetValue("assets", out assets) && assets is object[])
                            foreach (var a in (object[])assets)
                            {
                                var asset = (Dictionary<string, object>)a;
                                if (!string.Equals(Convert.ToString(asset["name"]), Product.FileStem + ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                                r.ToolDownload = Convert.ToString(asset["browser_download_url"]);
                                object digest;
                                var m = asset.TryGetValue("digest", out digest) ? Regex.Match(Convert.ToString(digest), "^sha256:([0-9a-fA-F]{64})$") : Match.Empty;
                                if (m.Success) r.ToolSha256 = m.Groups[1].Value;
                            }
                    }
                    break;   // GitHub lists the newest release first
                }
            }
            catch { }
            return r;
        }

        static string Get(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000; req.ReadWriteTimeout = 10000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return rd.ReadToEnd();
        }

        /// <summary>Compares "0.1.2-preview" style versions: numbers first, then a release beats a pre-release.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            Func<string, int[]> nums = v => { var m = Regex.Match(v, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?"); return new[] { m.Success ? int.Parse(m.Groups[1].Value) : 0, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0 }; };
            var a = nums(candidate); var b = nums(current);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            bool aPre = candidate.Contains("-"), bPre = current.Contains("-");
            if (aPre != bPre) return !aPre;
            return string.CompareOrdinal(candidate, current) > 0;
        }
    }
}
