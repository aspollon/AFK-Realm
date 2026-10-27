using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// A longer task that runs on the server side: starting the server, creating an account, or a whole installation
    /// with the engine. One runs at a time, as in the classic window. The page follows it through events ("job" for its
    /// state, "joblog" for its lines) and finds it again after a reload.
    /// </summary>
    class Job
    {
        public readonly string Id = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Kind = "task";                  // task | engine
        public string Title = "";
        public string Mode;                           // engine: Install, Update, Modules ...
        public volatile bool Running = true;
        public bool Ok;
        public string Message;                        // the end: "ok", "uptodate" or the error
        public string Status = "";                    // the current line
        public string Then;                           // what the page does at the end (e.g. "result")
        public readonly DateTime Started = DateTime.Now;
        public DateTime? Ended;
        public object Payload;                        // a task's result for the page
        public volatile bool Stop;                    // a task that can be stopped asks this
        public bool Cancellable;

        // engine
        public EngineRunner Runner;
        public int Phase = -1;
        public double Sub = -1;
        public string Action = "";
        public readonly List<string> Changes = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool ModuleInstalled;
        public string ServerRoot;

        readonly List<string> log = new List<string>();
        public IList<string> LogLines { get { lock (log) return log.ToList(); } }

        public void Log(string line)
        {
            lock (log) { log.Add(line); if (log.Count > 3000) log.RemoveRange(0, 1000); }
            Events.Send("joblog", new { id = Id, line });
        }

        public void SetStatus(string text)
        {
            Status = text ?? "";
            Changed();
        }

        DateTime lastSent = DateTime.MinValue;
        /// <summary>Tells the pages; progress that changes very often is sent at most four times a second.</summary>
        public void Changed(bool force = true)
        {
            if (!force && (DateTime.UtcNow - lastSent).TotalMilliseconds < 250) return;
            lastSent = DateTime.UtcNow;
            Events.Send("job", Snapshot());
        }

        public object Snapshot()
        {
            return new
            {
                id = Id, kind = Kind, title = Title, mode = Mode, running = Running, ok = Ok, message = Message, status = Status, then = Then,
                started = Started.ToString("o"), elapsed = (int)((Ended ?? DateTime.Now) - Started).TotalSeconds,
                phases = Runner == null ? null : Runner.Phases.Where(p => p.Weight > 0 || p.Id == "vs").Select(p => new { id = p.Id, title = p.Title }).ToArray(),
                phase = Runner == null || Phase < 0 ? null : Runner.Phases[Phase].Id,
                sub = Sub, action = Action,
                overall = Runner == null ? -1 : Runner.Overall(Phase, Math.Max(0, Sub)),
                changes = Changes, notes = Notes, moduleInstalled = ModuleInstalled, payload = Payload,
                serverRoot = ServerRoot, cancellable = Cancellable, stopping = Stop
            };
        }
    }

    static class Jobs
    {
        public static Job Current;
        static readonly object gate = new object();

        public static bool Busy { get { var j = Current; return j != null && j.Running; } }

        static Job Begin(Job job)
        {
            lock (gate)
            {
                if (Busy) throw new BusyException(L.F("{0} is busy with another task: {1}", Product.Name, Current.Title));
                Current = job;
            }
            job.Changed();
            return job;
        }

        /// <summary>
        /// Runs work in the background. Its status lines show on the page; an exception ends it with that message.
        /// <paramref name="after"/> runs at the end, before the pages are told (also after an error).
        /// </summary>
        public static Job Run(string title, Action<Job, Action<string>> work, Action<Job, Exception> after = null)
        {
            var job = Begin(new Job { Title = title });
            var thread = new Thread(() =>
            {
                Exception error = null;
                try { work(job, job.SetStatus); }
                catch (Exception ex) { error = ex; }
                try { if (after != null) after(job, error); } catch (Exception ex) { if (error == null) error = ex; }
                job.Ok = error == null;
                if (error != null) job.Message = Unwrap(error).Message;
                else if (job.Message == null) job.Message = "ok";
                job.Running = false; job.Ended = DateTime.Now;
                job.Changed();
                Manager.Poke();
            }) { IsBackground = true, Name = "job" };
            thread.Start();
            return job;
        }

        static Exception Unwrap(Exception ex)
        {
            while ((ex is AggregateException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        /// <summary>
        /// Runs the engine (Install, Update, Rebuild, Setup, Backup, Restore, RestoreParts, Modules). The server is stopped
        /// first when <paramref name="stopServer"/>. On Linux PowerShell 7 is fetched first if this computer has none.
        /// </summary>
        public static Job RunEngine(Install inst, string mode, string password, int dbPort, string engineArgs, string realmName, bool stopServer, Action<Job> before = null)
        {
            string title = mode == "Update" ? L.T("Updating") : mode == "Setup" ? L.T("Setting up") : mode == "Backup" ? L.T("Backing up")
                : mode == "Restore" || mode == "RestoreParts" ? L.T("Restoring a backup") : mode == "Modules" ? L.T("Changing modules") : L.T("Installing");
            var runner = new EngineRunner(mode);
            var job = Begin(new Job { Kind = "engine", Title = title, Mode = mode, Runner = runner, Then = "result", ServerRoot = inst.Root });
            runner.RealmName = realmName;
            runner.PhaseStarted += i => { job.Phase = i; job.Sub = -1; job.Changed(); };
            runner.SubProgress += (f, t) => { job.Sub = f; if (t != null) job.Action = t; job.Changed(false); };
            runner.LogLine += l =>
            {
                job.Log(l);
                if (job.Phase < 0 || runner.Phases[job.Phase].Id != "compile") { job.Action = l.Length > 140 ? l.Substring(0, 140) + " …" : l; job.Changed(false); }
            };
            runner.Change += c => { lock (job.Changes) job.Changes.Add(c); };
            runner.Note += n => { lock (job.Notes) job.Notes.Add(n); };
            runner.Finished += (ok, text) =>
            {
                job.Ok = ok; job.Message = text; job.ModuleInstalled = runner.ModuleInstalled;
                job.Running = false; job.Ended = DateTime.Now;
                job.Changed();
                Manager.Poke();
                Manager.AfterEngine(mode, ok);
            };
            var thread = new Thread(() =>
            {
                try
                {
                    if (before != null) before(job);
                    if (stopServer)
                    {
                        job.Action = L.T("Stopping the server ...");
                        job.Changed();
                        new ServerControl(inst).StopAll(t => { job.Action = t; job.Changed(); }, true);
                    }
                    if (!Os.Windows) EngineRunner.Shell = Pwsh.Ensure(t => { job.Action = t; job.Changed(); });
                    runner.Start(inst, mode, password, dbPort, engineArgs);
                }
                catch (Exception ex)
                {
                    job.Ok = false; job.Message = L.T("The installation could not be started:") + " " + Unwrap(ex).Message;
                    job.Running = false; job.Ended = DateTime.Now;
                    job.Changed();
                    Manager.Poke();
                }
            }) { IsBackground = true, Name = "engine" };
            thread.Start();
            return job;
        }

        public static void Cancel()
        {
            var j = Current;
            if (j == null || !j.Running) return;
            if (j.Runner != null) j.Runner.Cancel();
            else if (j.Cancellable) { j.Stop = true; j.Changed(); }
        }
    }

    class BusyException : InvalidOperationException
    {
        public BusyException(string message) : base(message) { }
    }
}
