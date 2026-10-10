using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// Looks on GitHub for newer server code (CoA core, Playerbots, installed modules) and a newer AFK Realm release.
    /// Read-only and quiet: without internet it simply reports nothing. Whether something is new is asked of Git (no
    /// limit); the GitHub API, limited to 60 questions an hour without signing in, only adds how many changes it is.
    /// </summary>
    static class UpdateCheck
    {
        public class Result
        {
            public readonly List<string> Server = new List<string>();   // e.g. "CoA core: 12 new changes"
            public string ToolVersion, ToolUrl;                           // newer AFK Realm release, if any
            public string ToolDownload, ToolSha256;                       // its exe (and checksum, when GitHub lists one)
            public DateTime? LimitedUntil;                                // the GitHub API refused: its hourly limit was reached
        }

        public static Result Run(Install inst)
        {
            var r = new Result();
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2 for GitHub
            string git = FindGit(inst);
            string revisions = Path.Combine(inst.Root, "Dependencies", "revisions.txt");
            if (File.Exists(revisions))
            {
                foreach (var line in File.ReadAllLines(revisions))
                {
                    var f = line.Split('|');                              // kind|repo url|branch|revision
                    if (f.Length < 4) continue;
                    var m = Regex.Match(f[1], @"github\.com[/:]([^/]+)/([^/.]+)");
                    if (!m.Success || !Regex.IsMatch(f[3], "^[0-9a-f]{40}$")) continue;
                    Ask(r, git, f[0] == "core" ? "CoA core" : "Playerbots", m.Groups[1].Value + "/" + m.Groups[2].Value, f[2], f[3]);
                }
            }
            // Additional modules: the same question for each one that came from GitHub.
            try
            {
                string modules = Path.Combine(inst.SourceDir, "modules");
                if (Directory.Exists(modules))
                    foreach (var folder in Directory.GetDirectories(modules))
                    {
                        string name = Path.GetFileName(folder);
                        if (name.Equals("mod-playerbots", StringComparison.OrdinalIgnoreCase)) continue;
                        string branch, revision, repo = GitHubRepoOf(folder);
                        if (repo == null || !ReadHead(folder, out branch, out revision)) continue;
                        Ask(r, git, name, repo, branch, revision);
                    }
            }
            catch { }
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
            catch (WebException ex)
            {
                NoteLimit(r, ex);
                // Without the API, the tags of the repository still tell whether there is a newer version; its page
                // is shown instead of an update in place, since the release behind the tag may not be built yet.
                string newest = null;
                foreach (var tag in Tags(git, "https://github.com/" + Product.GitHubRepo + ".git"))
                    if (IsNewer(tag, Product.Version) && (newest == null || IsNewer(tag, newest))) newest = tag;
                if (newest != null)
                {
                    r.ToolVersion = newest;
                    r.ToolUrl = "https://github.com/" + Product.GitHubRepo + "/releases/tag/v" + newest;
                }
            }
            catch { }
            return r;
        }

        /// <summary>
        /// Whether a repository has something newer than the revision built. Git itself answers that (git ls-remote),
        /// which GitHub does not limit; the GitHub API only adds how many changes it is, as long as GitHub answers it
        /// (60 questions an hour without signing in).
        /// </summary>
        static void Ask(Result r, string git, string name, string repo, string branch, string revision)
        {
            string head = RemoteHead(git, "https://github.com/" + repo + ".git", branch);
            if (head != null && head.Equals(revision, StringComparison.OrdinalIgnoreCase)) return;   // current
            if (r.LimitedUntil == null)
            {
                try
                {
                    string json = Get("https://api.github.com/repos/" + repo + "/compare/" + revision + "..." + Uri.EscapeDataString(branch));
                    var ahead = Regex.Match(json, @"""ahead_by""\s*:\s*(\d+)");
                    int n = ahead.Success ? int.Parse(ahead.Groups[1].Value) : 0;
                    if (n > 0) r.Server.Add(name + ": " + n + (n == 1 ? " new change" : " new changes"));
                    return;
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    // An unknown revision (history rewritten upstream) still means the build is not current.
                    if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) { r.Server.Add(name + ": new version"); return; }
                    NoteLimit(r, ex);
                }
                catch { }
            }
            if (head != null) r.Server.Add(name + ": new version");
        }

        /// <summary>A refusal of the GitHub API because of its hourly limit: when it answers again.</summary>
        static void NoteLimit(Result r, WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            if (resp == null || ((int)resp.StatusCode != 403 && (int)resp.StatusCode != 429)) return;
            long reset;
            if (resp.Headers["X-RateLimit-Remaining"] == "0" && long.TryParse(resp.Headers["X-RateLimit-Reset"], out reset))
                r.LimitedUntil = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(reset).ToLocalTime();
            else if (r.LimitedUntil == null)
                r.LimitedUntil = DateTime.Now.AddHours(1);
        }

        /// <summary>git.exe: on PATH, in its usual folders, or the portable copy the engine sets up.</summary>
        static string FindGit(Install inst)
        {
            var candidates = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) candidates.Add(Path.Combine(dir.Trim().Trim('"'), "git.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
            candidates.Add(Path.Combine(inst.Root, "Dependencies", "Git", "cmd", "git.exe"));
            foreach (var c in candidates)
                try { if (File.Exists(c)) return c; } catch { }
            return null;
        }

        /// <summary>The output of a short git command, or null (no git, no network, no answer within 20 seconds).</summary>
        static string RunGit(string git, params string[] args)
        {
            if (git == null) return null;
            try
            {
                var psi = new ProcessStartInfo(git, string.Join(" ", Array.ConvertAll(args, a => "\"" + a + "\"")))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";      // never ask for a sign-in
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return null; }
                    return p.ExitCode == 0 ? output.Result : null;
                }
            }
            catch { return null; }
        }

        /// <summary>The commit a branch of a repository stands at, asked of the repository itself.</summary>
        static string RemoteHead(string git, string url, string branch)
        {
            string output = RunGit(git, "ls-remote", url, "refs/heads/" + branch);
            var m = output == null ? Match.Empty : Regex.Match(output, "^([0-9a-f]{40})\\s", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>The version tags of a repository ("0.8.0-preview" for v0.8.0-preview).</summary>
        static List<string> Tags(string git, string url)
        {
            var tags = new List<string>();
            string output = RunGit(git, "ls-remote", "--tags", url);
            if (output != null)
                foreach (Match m in Regex.Matches(output, @"refs/tags/v(\d+\.\d+(?:\.\d+)?(?:-[\w.]+)?)$", RegexOptions.Multiline))
                    tags.Add(m.Groups[1].Value);
            return tags;
        }

        /// <summary>"owner/repo" of the module's origin on GitHub, or null.</summary>
        static string GitHubRepoOf(string folder)
        {
            try
            {
                string config = Path.Combine(folder, ".git", "config");
                if (!File.Exists(config)) return null;
                var url = Regex.Match(File.ReadAllText(config), @"\[remote ""origin""\][^\[]*?url\s*=\s*(\S+)", RegexOptions.IgnoreCase);
                var m = url.Success ? Regex.Match(url.Groups[1].Value, @"github\.com[/:]([\w.\-]+)/([\w.\-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase) : Match.Empty;
                return m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : null;
            }
            catch { return null; }
        }

        /// <summary>The branch a module's folder is on and the commit it stands at, read from its .git folder.</summary>
        static bool ReadHead(string folder, out string branch, out string revision)
        {
            branch = revision = null;
            try
            {
                string git = Path.Combine(folder, ".git");
                var head = Regex.Match(File.ReadAllText(Path.Combine(git, "HEAD")).Trim(), @"^ref:\s*(refs/heads/(\S+))$");
                if (!head.Success) return false;                          // not on a branch
                branch = head.Groups[2].Value;
                string loose = Path.Combine(git, head.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose)) revision = File.ReadAllText(loose).Trim();
                else if (File.Exists(Path.Combine(git, "packed-refs")))
                    foreach (var line in File.ReadAllLines(Path.Combine(git, "packed-refs")))
                        if (line.EndsWith(" " + head.Groups[1].Value)) revision = line.Substring(0, line.IndexOf(' '));
                return revision != null && Regex.IsMatch(revision, "^[0-9a-f]{40}$");
            }
            catch { return false; }
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
