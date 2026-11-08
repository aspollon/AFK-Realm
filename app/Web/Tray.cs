using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The icon of AFK Realm in the Windows tray (next to the clock). With it, closing the window does not end AFK Realm:
    /// it goes on running in the background - the scheduled restart, the planned update, remote access and the
    /// registration portal keep working - and the icon opens the window again (double-click or "Open") or ends AFK Realm
    /// ("Quit"). Windows only; built on Shell_NotifyIcon directly, so the program needs no Windows Forms.
    /// </summary>
    static class Tray
    {
        const int WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_NULL = 0x0000, WM_COMMAND = 0x0111, WM_APP = 0x8000;
        const int WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
        const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
        const int NIIF_INFO = 1;
        const uint MF_STRING = 0, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2;
        const int CallbackMessage = WM_APP + 1, OpenCommand = 1, QuitCommand = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time; public POINT pt; }

        delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr ExtractIcon(IntPtr hInst, string file, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessage(out MSG msg, IntPtr hWnd, int min, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        static WndProc proc;                // kept alive: the window calls it
        static IntPtr window, icon;
        static int taskbarCreated;
        static Action open, quit;
        static Thread thread;
        static bool told;

        /// <summary>Whether the icon is there.</summary>
        public static bool Running { get { return window != IntPtr.Zero; } }

        /// <summary>Puts the icon into the tray. open: show the window again; quit: end AFK Realm.</summary>
        public static void Start(Action onOpen, Action onQuit)
        {
            if (!OperatingSystem.IsWindows() || thread != null) return;
            open = onOpen; quit = onQuit;
            var ready = new ManualResetEvent(false);
            thread = new Thread(() =>
            {
                try { Create(); } catch (Exception ex) { Program.StartLog("tray: " + ex.Message); window = IntPtr.Zero; }
                ready.Set();
                if (window == IntPtr.Zero) return;
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }) { IsBackground = true, Name = "tray" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(5000);
        }

        /// <summary>Takes the icon away (when AFK Realm ends).</summary>
        public static void Stop()
        {
            if (window != IntPtr.Zero) PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Once per run, when the window is closed: a note at the icon that AFK Realm goes on running there.</summary>
        public static void TellOnce()
        {
            if (told || window == IntPtr.Zero) return;
            told = true;
            var data = Data();
            data.uFlags = NIF_INFO;
            data.szInfoTitle = Product.Name;
            data.szInfo = L.F("{0} keeps running here, so planned restarts and updates still happen. Double-click the icon to open it again; right-click it to quit.", Product.Name);
            data.dwInfoFlags = NIIF_INFO;
            try { Shell_NotifyIcon(NIM_MODIFY, ref data); } catch { }
        }

        static void Create()
        {
            proc = Handle;
            IntPtr instance = GetModuleHandle(null);
            var wc = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = proc, hInstance = instance, lpszClassName = "AFKRealmTray" };
            RegisterClassEx(ref wc);
            // a hidden top-level window (never shown): it gets the icon's clicks and owns its menu
            window = CreateWindowEx(0, "AFKRealmTray", Product.Name, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero) return;
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            try { icon = ExtractIcon(instance, Process.GetCurrentProcess().MainModule.FileName, 0); } catch { icon = IntPtr.Zero; }
            if (icon == IntPtr.Zero || icon == (IntPtr)1) icon = LoadIcon(IntPtr.Zero, (IntPtr)32512);     // IDI_APPLICATION
            Add();
        }

        static NOTIFYICONDATA Data()
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)), hWnd = window, uID = 1,
                szTip = "", szInfo = "", szInfoTitle = ""
            };
        }

        static void Add()
        {
            var data = Data();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = CallbackMessage;
            data.hIcon = icon;
            data.szTip = Product.Name;
            Shell_NotifyIcon(NIM_ADD, ref data);
        }

        static IntPtr Handle(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == CallbackMessage)
                {
                    int what = (int)((long)lParam & 0xFFFF);
                    if (what == WM_LBUTTONDBLCLK) Run(open);
                    else if (what == WM_RBUTTONUP || what == WM_CONTEXTMENU) Menu(hWnd);
                    return IntPtr.Zero;
                }
                if (msg == taskbarCreated && taskbarCreated != 0) { Add(); return IntPtr.Zero; }    // Explorer started again
                if (msg == WM_CLOSE) { DestroyWindow(hWnd); return IntPtr.Zero; }
                if (msg == WM_DESTROY)
                {
                    var data = Data();
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                    window = IntPtr.Zero;
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
            }
            catch { }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        static void Menu(IntPtr hWnd)
        {
            IntPtr menu = CreatePopupMenu();
            AppendMenu(menu, MF_STRING, (UIntPtr)OpenCommand, L.F("Open {0}", Product.Name));
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)QuitCommand, L.F("Quit {0}", Product.Name));
            POINT p;
            GetCursorPos(out p);
            // the menu closes when the user clicks elsewhere only if its window is in front
            SetForegroundWindow(hWnd);
            int chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, 0, hWnd, IntPtr.Zero);
            PostMessage(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            DestroyMenu(menu);
            if (chosen == OpenCommand) Run(open);
            else if (chosen == QuitCommand) Run(quit);
        }

        static void Run(Action action)
        {
            if (action == null) return;
            ThreadPool.QueueUserWorkItem(_ => { try { action(); } catch { } });
        }
    }
}
