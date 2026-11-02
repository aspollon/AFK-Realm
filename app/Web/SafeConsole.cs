using System;
using System.IO;
using System.Text;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The console of AFK Realm, made safe. Started from a desktop icon, or restarted by its own update (which keeps the
    /// terminal of the old process), it may have a console that is gone: on Linux every write then fails with "Input/output
    /// error", and a step that only meant to print a line - the CoA Custom install prints all it does - failed with it.
    /// Writes that fail are dropped from then on. Lines can also go to a log file (Tee) while a task runs.
    /// </summary>
    sealed class SafeConsole : TextWriter
    {
        readonly TextWriter inner;
        bool broken;
        public static volatile TextWriter Tee;

        SafeConsole(TextWriter inner) { this.inner = inner; }
        public override Encoding Encoding { get { return inner.Encoding; } }

        public static void Install()
        {
            try
            {
                if (!(Console.Out is SafeConsole)) Console.SetOut(new SafeConsole(Console.Out));
                if (!(Console.Error is SafeConsole)) Console.SetError(new SafeConsole(Console.Error));
            }
            catch { }
        }

        void Do(Action<TextWriter> write)
        {
            var tee = Tee;
            if (tee != null) try { lock (tee) write(tee); } catch { }
            if (broken) return;
            try { write(inner); }
            catch (IOException) { broken = true; }
            catch (ObjectDisposedException) { broken = true; }
        }

        public override void Write(char value) { Do(w => w.Write(value)); }
        public override void Write(string value) { Do(w => w.Write(value)); }
        public override void Write(char[] buffer, int index, int count) { Do(w => w.Write(buffer, index, count)); }
        public override void WriteLine(string value) { Do(w => w.WriteLine(value)); }
        public override void WriteLine() { Do(w => w.WriteLine()); }
        public override void Flush() { Do(w => w.Flush()); }
    }
}
