using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Installing Visual Studio, firewall rules and services needs administrator rights.
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", args)) { UseShellExecute = true, Verb = "runas" });
                }
                catch
                {
                    MessageBox.Show(Product.Name + " needs administrator rights (for Visual Studio, the firewall and the database). Please confirm the Windows prompt.",
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            bool created;
            using (var single = new Mutex(true, Product.FileStem + ".SingleInstance", out created))
            {
                // After an update the old version needs a moment to close.
                if (!created && Array.IndexOf(args, SelfUpdate.RestartArgument) >= 0)
                {
                    try { created = single.WaitOne(15000); } catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    MessageBox.Show(Product.Name + " is already open.", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                SelfUpdate.JustUpdated = Array.IndexOf(args, SelfUpdate.RestartArgument) >= 0;
                SelfUpdate.CleanUp();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => MessageBox.Show("Unexpected error:\n" + e.Exception.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new MainForm());
            }
            return 0;
        }
    }
}
