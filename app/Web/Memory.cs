using System;
using System.Runtime;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Gives memory back to Windows/Linux after big work. Map extraction, client patching, race imports and
    /// downloads read archives and DBC tables of hundreds of MB; .NET kept that memory reserved afterwards, so
    /// AFK Realm sat at many GB while it did nothing.
    /// </summary>
    static class Memory
    {
        static int pending;

        /// <summary>Collects in the background a moment after a task ended (again only when it is not already planned).</summary>
        public static void TrimSoon()
        {
            if (Interlocked.Exchange(ref pending, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Thread.Sleep(3000); Trim(); }
                catch { }
                finally { Interlocked.Exchange(ref pending, 0); }
            });
        }

        public static void Trim()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            // Aggressive: compacts and hands the free pages back to the system.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        }
    }
}
