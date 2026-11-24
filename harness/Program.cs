// Test program for WindowTabs: does a group of windows ever end up taller than
// the work area (its lower edge under the taskbar)?
//
//   harness.exe win TITLE [minHeight]   opens one test window and keeps it open
//   harness.exe run OUTDIR LABEL        runs every scenario against the
//                                       WindowTabs that is already running, and
//                                       writes OUTDIR\results-LABEL.txt and
//                                       screenshots OUTDIR\LABEL-*.png
//
// The test windows are all started from this harness.exe, and WindowTabs is
// told (AutoGroupingPaths in its settings) to put every window of harness.exe
// into one group.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public int W => R - L; public int H => B - T; public override string ToString() => $"({L},{T})-({R},{B}) {W}x{H}"; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMin, ptMax; public RECT rcNormal; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);

    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    const int SW_MAXIMIZE = 3, SW_RESTORE = 9;

    static StringBuilder report = new StringBuilder();
    static string outDir = ".", label = "run";
    static int shot = 0;

    static void Log(string s) { var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s; report.AppendLine(line); Console.WriteLine(line); }

    static IntPtr Find(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => { var t = new StringBuilder(256); GetWindowText(h, t, 256); if (t.ToString() == title) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    static RECT Work(IntPtr h, out RECT monitor)
    {
        var i = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        GetMonitorInfo(MonitorFromWindow(h, 2), ref i);
        monitor = i.rcMonitor;
        return i.rcWork;
    }

    // The visible part of a window: DWMWA_EXTENDED_FRAME_BOUNDS, without the
    // invisible resize borders.
    static RECT Visible(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, 9, out var f, Marshal.SizeOf(typeof(RECT))) == 0 && f.B > f.T) return f;
        GetWindowRect(h, out var r); return r;
    }

    static string Describe(string title, IntPtr h)
    {
        GetWindowRect(h, out var r);
        var v = Visible(h);
        return $"{title}: window {r} visible {v}{(IsZoomed(h) ? " MAXIMIZED" : "")}{(IsIconic(h) ? " MINIMIZED" : "")}{(IsWindowVisible(h) ? "" : " HIDDEN")}";
    }

    static void Shot(string name)
    {
        try
        {
            var b = Screen.PrimaryScreen.Bounds;
            using (var bmp = new Bitmap(b.Width, b.Height))
            using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(b.Location, Point.Empty, b.Size); bmp.Save(Path.Combine(outDir, $"{label}-{++shot:00}-{name}.png"), ImageFormat.Png); }
        }
        catch (Exception e) { Log("screenshot failed: " + e.Message); }
    }

    static void Front(IntPtr h)
    {
        // Alt down/up lets this process move the foreground.
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(h);
    }

    static Dictionary<string, IntPtr> windows = new Dictionary<string, IntPtr>();

    // Checks every test window that is shown and not minimized or maximized:
    // its visible lower edge must not be below the work area's lower edge.
    static bool CheckAll(string scenario, RECT work)
    {
        bool ok = true;
        foreach (var kv in windows)
        {
            var h = kv.Value;
            Log("  " + Describe(kv.Key, h));
            if (!IsWindowVisible(h) || IsIconic(h) || IsZoomed(h)) continue;
            var v = Visible(h);
            bool onScreen = v.R > work.L && v.L < work.R && v.T < work.B;
            if (onScreen && v.B > work.B + 1) { ok = false; Log($"  -> {kv.Key} visible lower edge {v.B} is below the work area's lower edge {work.B} by {v.B - work.B}"); }
        }
        Log($"RESULT {scenario}: {(ok ? "PASS" : "FAIL")}");
        Shot(scenario);
        return ok;
    }

    static void Run()
    {
        var self = Process.GetCurrentProcess().MainModule.FileName;
        var children = new List<Process>();
        foreach (var t in new[] { "W-A", "W-B", "W-C" })
        {
            children.Add(Process.Start(self, "win " + t));
            Thread.Sleep(4000);
        }
        Thread.Sleep(6000);
        foreach (var t in new[] { "W-A", "W-B", "W-C" }) windows[t] = Find(t);
        if (windows.Values.Any(h => h == IntPtr.Zero)) { Log("RESULT setup: FAIL (a test window did not open)"); return; }
        var a = windows["W-A"];
        var work = Work(a, out var monitor);
        Log($"display {monitor}  work area {work}  (top band present: {work.T > monitor.T}, taskbar below: {work.B < monitor.B})");

        // 1. Grouped? Bring W-A to the front, move it, the other two should follow.
        Front(a); Thread.Sleep(2000);
        int x = work.L + 40, y = work.T, w = Math.Min(900, work.W - 80), h = Math.Min(560, work.H - 40);
        SetWindowPos(a, IntPtr.Zero, x, y, w, h, SWP_NOZORDER); Thread.Sleep(5000);
        GetWindowRect(a, out var ra); GetWindowRect(windows["W-B"], out var rb); GetWindowRect(windows["W-C"], out var rc);
        bool grouped = ra.W == rb.W && ra.H == rb.H && ra.W == rc.W && ra.H == rc.H;
        foreach (var kv in windows) Log("  " + Describe(kv.Key, kv.Value));
        Log($"RESULT grouped: {(grouped ? "PASS" : "FAIL")}");
        Shot("grouped");

        // 2. The 11-23 log: maximize the front window; while it is maximized
        //    its remembered normal size becomes taller than the work area; restore.
        Front(a); Thread.Sleep(1000);
        ShowWindow(a, SW_MAXIMIZE); Thread.Sleep(4000);
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
        GetWindowPlacement(a, ref wp);
        // rcNormal is in work-area coordinates: 0 is the work area's top edge.
        wp.rcNormal = new RECT { L = 32, T = 0, R = 32 + w, B = work.H + 200 };
        SetWindowPlacement(a, ref wp); Thread.Sleep(1500);
        ShowWindow(a, SW_RESTORE); Thread.Sleep(6000);
        CheckAll("restore-to-tall-size", work);

        // 3. Another program stretches the front window 150 pixels taller than the work area.
        Front(a); Thread.Sleep(1000);
        GetWindowRect(a, out ra);
        SetWindowPos(a, IntPtr.Zero, ra.L, work.T, ra.W, work.H + 150, SWP_NOZORDER | SWP_NOACTIVATE); Thread.Sleep(6000);
        CheckAll("front-stretched", work);

        // 4. Another program stretches W-B (not in front), then W-B is brought to the front.
        var b = windows["W-B"];
        GetWindowRect(b, out rb);
        SetWindowPos(b, IntPtr.Zero, rb.L, work.T, rb.W, work.H + 250, SWP_NOZORDER | SWP_NOACTIVATE); Thread.Sleep(6000);
        CheckAll("back-window-stretched", work);
        Front(b); Thread.Sleep(6000);
        var fg = new StringBuilder(256); GetWindowText(GetForegroundWindow(), fg, 256);
        Log($"  front window now: {fg}");
        CheckAll("back-window-shown", work);

        // 5. Maximize and restore once more with ordinary sizes.
        Front(a); Thread.Sleep(1000);
        ShowWindow(a, SW_MAXIMIZE); Thread.Sleep(4000);
        ShowWindow(a, SW_RESTORE); Thread.Sleep(6000);
        CheckAll("maximize-restore", work);

        // 6. A window whose program will not let it be shorter than the work
        //    area + 150. It cannot be fitted; WindowTabs must not push it forever.
        children.Add(Process.Start(self, $"win W-D {work.H + 150}"));
        Thread.Sleep(8000);
        windows["W-D"] = Find("W-D");
        Front(windows["W-D"]); Thread.Sleep(15000);
        var wt = Process.GetProcessesByName("WindowTabs").FirstOrDefault();
        var cpu1 = wt?.TotalProcessorTime ?? TimeSpan.Zero; Thread.Sleep(10000);
        wt?.Refresh();
        var cpu2 = wt?.TotalProcessorTime ?? TimeSpan.Zero;
        double busy = (cpu2 - cpu1).TotalMilliseconds / 10000.0 * 100.0;
        Log("  " + Describe("W-D", windows["W-D"]));
        Log($"  WindowTabs CPU over 10 s: {busy:F1}% of one core, still running: {(wt != null && !wt.HasExited)}");
        Log($"RESULT unfittable-window-no-endless-loop: {(wt != null && !wt.HasExited && busy < 30 ? "PASS" : "FAIL")}");
        Shot("unfittable");

        foreach (var p in children) try { p.Kill(); } catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        if (args.Length >= 2 && args[0] == "win")
        {
            var f = new Form { Text = args[1], StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(80, 80, 800, 500) };
            if (args.Length >= 3) f.MinimumSize = new Size(300, int.Parse(args[2]));
            var l = new Label { Text = args[1], Dock = DockStyle.Fill, Font = new Font("Segoe UI", 28), TextAlign = ContentAlignment.MiddleCenter };
            var bottom = new Label { Text = "bottom edge of the window", Dock = DockStyle.Bottom, Height = 40, BackColor = Color.OrangeRed, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter };
            f.Controls.Add(l); f.Controls.Add(bottom);
            Application.Run(f);
            return 0;
        }
        if (args.Length >= 3 && args[0] == "run")
        {
            outDir = args[1]; label = args[2];
            Directory.CreateDirectory(outDir);
            try { Run(); } catch (Exception e) { Log("ERROR " + e); }
            File.WriteAllText(Path.Combine(outDir, $"results-{label}.txt"), report.ToString());
            return 0;
        }
        return 1;
    }
}
