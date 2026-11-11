using System;
using System.Diagnostics;

namespace CoAInstaller.Web
{
    /// <summary>A desktop notification on Linux (notify-send, part of a normal Ubuntu desktop). Does nothing elsewhere.</summary>
    static class LinuxNotify
    {
        public static void Send(string title, string text)
        {
            if (Os.Windows) return;
            try
            {
                var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false };
                psi.ArgumentList.Add("--app-name=" + title);
                psi.ArgumentList.Add("--expire-time=15000");
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(text);
                using (var p = Process.Start(psi)) p?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
