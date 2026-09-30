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
                if (!created)
                {
                    MessageBox.Show(Product.Name + " is already open.", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => MessageBox.Show("Unexpected error:\n" + e.Exception.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new MainForm());
            }
            return 0;
        }
    }
}
