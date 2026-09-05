// ScreenSwap - swap every window between two monitors with one hotkey.
// Preserves maximized / minimized / normal state. Windows only.
// Built against .NET Framework 4.x with the in-box csc.exe (no SDK needed).
// C# 5 only - no string interpolation, no inline out vars.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenSwap
{
    // ---------------------------------------------------------------- config
    static class Config
    {
        // Hotkey. Change these two lines and re-run build.cmd to rebind.
        public const uint HOTKEY_MODS = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        public const uint HOTKEY_VK = 0x58;            // 'X'  ->  Ctrl + Alt + X
        public const string HOTKEY_TEXT = "Ctrl + Alt + X";

        // true  = a window keeps the same *proportion* of the screen it was on
        //         (a half-screen window stays half-screen on the other monitor).
        // false = a window keeps its exact pixel size and is only moved across.
        public const bool SCALE_TO_FIT = true;

        // Put focus back where it was after the swap.
        public const bool RESTORE_FOREGROUND = true;

        public const string APP_NAME = "ScreenSwap";
        public const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string SETTINGS_KEY = @"Software\ScreenSwap";

        // Which icon to start with before you pick one from the tray menu.
        // 2 = "Monitors with stands". Full list in IconLibrary.Names.
        public const int DEFAULT_ICON_STYLE = 2;
    }

    // ------------------------------------------------------------------ log
    // Small append-only log. Without this, "it didn't start" is unfalsifiable:
    // there is no way to tell a launch that never happened from one that crashed.
    static class Log
    {
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenSwap");

        public static string File_ { get { return Path.Combine(Dir, "screenswap.log"); } }

        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(File_) && new FileInfo(File_).Length > 200000)
                    File.WriteAllText(File_, "(trimmed)" + Environment.NewLine);
                File.AppendAllText(File_,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------- interop
    static class Native
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const int GA_ROOTOWNER = 3;
        public const int DWMWA_CLOAKED = 14;

        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;

        public const int WPF_ASYNCWINDOWPLACEMENT = 0x0004;

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint MONITORINFOF_PRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public static RECT FromXYWH(int x, int y, int w, int h)
            {
                RECT r; r.Left = x; r.Top = y; r.Right = x + w; r.Bottom = y + h; return r;
            }
            public override string ToString()
            {
                return string.Format("({0},{1})-({2},{3}) {4}x{5}", Left, Top, Right, Bottom, Width, Height);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint flags);

        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);

        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        // 32-bit / 64-bit safe GetWindowLong
        public static int GetWindowLong(IntPtr h, int index)
        {
            if (IntPtr.Size == 8) return (int)(long)GetWindowLongPtrW(h, index);
            return GetWindowLongW(h, index);
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLongW(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);

        public static string TextOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }
        public static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassNameW(h, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------- monitors
    class MonitorInfo
    {
        public IntPtr Handle;
        public Native.RECT Bounds;
        public Native.RECT Work;
        public bool Primary;
        public string Device;
        public override string ToString()
        {
            return string.Format("{0}{1} {2}", Device, Primary ? " [primary]" : "", Bounds);
        }
    }

    static class Monitors
    {
        public static List<MonitorInfo> All()
        {
            List<MonitorInfo> list = new List<MonitorInfo>();
            Native.MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, ref Native.RECT r, IntPtr d)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (Native.GetMonitorInfoW(h, ref mi))
                {
                    MonitorInfo m = new MonitorInfo();
                    m.Handle = h;
                    m.Bounds = mi.rcMonitor;
                    m.Work = mi.rcWork;
                    m.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                    m.Device = mi.szDevice;
                    list.Add(m);
                }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                int c = a.Bounds.Left.CompareTo(b.Bounds.Left);
                if (c != 0) return c;
                return a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            return list;
        }

        // WINDOWPLACEMENT.rcNormalPosition is in "workspace" coordinates, which are
        // offset from screen coordinates by the primary monitor's work-area origin.
        // Compute that offset once so the maths is correct on any taskbar setup.
        public static Native.POINT WorkspaceOffset(List<MonitorInfo> mons)
        {
            Native.POINT p; p.X = 0; p.Y = 0;
            foreach (MonitorInfo m in mons)
            {
                if (m.Primary)
                {
                    p.X = m.Work.Left - m.Bounds.Left;
                    p.Y = m.Work.Top - m.Bounds.Top;
                    break;
                }
            }
            return p;
        }
    }

    // --------------------------------------------------------------- engine
    static class Swapper
    {
        public class Result
        {
            public int Moved;
            public int Failed;
            public int Skipped;
            public string Message;
        }

        static bool IsManageable(IntPtr h, IntPtr self)
        {
            if (h == IntPtr.Zero || h == self) return false;
            if (!Native.IsWindow(h)) return false;
            if (!Native.IsWindowVisible(h)) return false;
            if (h == Native.GetShellWindow()) return false;

            int style = Native.GetWindowLong(h, Native.GWL_STYLE);
            if ((style & Native.WS_CHILD) != 0) return false;

            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;

            // Hidden UWP shells report as visible; DWM knows they are cloaked.
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            string cls = Native.ClassOf(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow" ||
                cls == "MultitaskingViewFrame" || cls == "ForegroundStaging" ||
                cls == "XamlExplorerHostIslandWindow" || cls == "TaskListThumbnailWnd")
                return false;

            // Keep real app windows and their dialogs; drop tooltips and dropdowns.
            bool isRootOwner = Native.GetAncestor(h, Native.GA_ROOTOWNER) == h;
            bool hasCaption = (style & Native.WS_CAPTION) == Native.WS_CAPTION;
            if (!isRootOwner && !hasCaption) return false;

            if (Native.TextOf(h).Length == 0 && !isRootOwner) return false;

            return true;
        }

        static MonitorInfo MonitorOf(IntPtr h, Native.WINDOWPLACEMENT wp, Native.POINT wsOff, List<MonitorInfo> mons)
        {
            // A minimized window's live rect is off in the weeds (-32000), so use the
            // stored restore rect instead to work out where it actually belongs.
            IntPtr hm;
            if (wp.showCmd == Native.SW_SHOWMINIMIZED || Native.IsIconic(h))
            {
                Native.RECT r = wp.rcNormalPosition;
                r.Left += wsOff.X; r.Right += wsOff.X;
                r.Top += wsOff.Y; r.Bottom += wsOff.Y;
                hm = Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST);
            }
            else
            {
                hm = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
            }
            foreach (MonitorInfo m in mons) if (m.Handle == hm) return m;
            return null;
        }

        static Native.RECT MapRect(Native.RECT r, Native.RECT src, Native.RECT dst)
        {
            int w, hgt, x, y;
            if (Config.SCALE_TO_FIT)
            {
                double sx = (double)dst.Width / src.Width;
                double sy = (double)dst.Height / src.Height;
                w = (int)Math.Round(r.Width * sx);
                hgt = (int)Math.Round(r.Height * sy);
                x = dst.Left + (int)Math.Round((r.Left - src.Left) * sx);
                y = dst.Top + (int)Math.Round((r.Top - src.Top) * sy);
            }
            else
            {
                w = r.Width;
                hgt = r.Height;
                x = dst.Left + (r.Left - src.Left);
                y = dst.Top + (r.Top - src.Top);
            }

            if (w > dst.Width) w = dst.Width;
            if (hgt > dst.Height) hgt = dst.Height;
            if (w < 80) w = 80;
            if (hgt < 60) hgt = 60;
            if (x + w > dst.Right) x = dst.Right - w;
            if (y + hgt > dst.Bottom) y = dst.Bottom - hgt;
            if (x < dst.Left) x = dst.Left;
            if (y < dst.Top) y = dst.Top;
            return Native.RECT.FromXYWH(x, y, w, hgt);
        }

        public static Result Swap(IntPtr self, TextWriter log, bool dryRun)
        {
            Result res = new Result();
            List<MonitorInfo> mons = Monitors.All();

            if (mons.Count < 2)
            {
                res.Message = "Only one monitor detected - nothing to swap.";
                return res;
            }
            if (mons.Count > 2)
            {
                res.Message = string.Format("{0} monitors detected. ScreenSwap swaps two; using the leftmost and rightmost.", mons.Count);
            }

            MonitorInfo a = mons[0];
            MonitorInfo b = mons[mons.Count - 1];
            Native.POINT wsOff = Monitors.WorkspaceOffset(mons);

            if (log != null)
            {
                log.WriteLine("Monitors:");
                foreach (MonitorInfo m in mons) log.WriteLine("  " + m + "  work=" + m.Work);
                log.WriteLine(string.Format("Swapping A={0}  <->  B={1}", a.Device, b.Device));
                log.WriteLine(string.Format("Workspace offset for placement rects: ({0},{1})", wsOff.X, wsOff.Y));
                log.WriteLine("");
            }

            List<IntPtr> targets = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (IsManageable(h, self)) targets.Add(h);
                return true;
            }, IntPtr.Zero);

            IntPtr fg = Native.GetForegroundWindow();

            foreach (IntPtr h in targets)
            {
                Native.WINDOWPLACEMENT wp = new Native.WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                if (!Native.GetWindowPlacement(h, ref wp)) { res.Failed++; continue; }

                MonitorInfo from = MonitorOf(h, wp, wsOff, mons);
                if (from == null) { res.Skipped++; continue; }

                MonitorInfo to;
                if (from.Handle == a.Handle) to = b;
                else if (from.Handle == b.Handle) to = a;
                else { res.Skipped++; continue; }   // on a third monitor - leave alone

                // rcNormalPosition -> screen coords -> mapped -> back to workspace coords
                Native.RECT normScreen = wp.rcNormalPosition;
                normScreen.Left += wsOff.X; normScreen.Right += wsOff.X;
                normScreen.Top += wsOff.Y; normScreen.Bottom += wsOff.Y;

                Native.RECT mapped = MapRect(normScreen, from.Work, to.Work);

                string title = Native.TextOf(h);
                string state = wp.showCmd == Native.SW_SHOWMAXIMIZED ? "max"
                             : wp.showCmd == Native.SW_SHOWMINIMIZED ? "min" : "normal";

                bool ok;
                if (dryRun)
                {
                    ok = true;
                }
                else if (wp.showCmd == Native.SW_SHOWMAXIMIZED)
                {
                    ok = MoveMaximized(h, wp, mapped, wsOff, to);
                }
                else if (wp.showCmd == Native.SW_SHOWMINIMIZED || Native.IsIconic(h))
                {
                    // Leave it minimized; just relocate where it will restore to.
                    Native.WINDOWPLACEMENT np = wp;
                    np.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                    np.flags |= Native.WPF_ASYNCWINDOWPLACEMENT;
                    np.rcNormalPosition = Native.RECT.FromXYWH(
                        mapped.Left - wsOff.X, mapped.Top - wsOff.Y, mapped.Width, mapped.Height);
                    ok = Native.SetWindowPlacement(h, ref np);
                }
                else
                {
                    ok = Native.SetWindowPos(h, IntPtr.Zero, mapped.Left, mapped.Top,
                        mapped.Width, mapped.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                }

                if (ok) res.Moved++; else res.Failed++;

                if (log != null)
                {
                    uint pid; Native.GetWindowThreadProcessId(h, out pid);
                    log.WriteLine(string.Format("{0}  [{1}]  {2} -> {3}  {4}  pid={5}  \"{6}\"",
                        dryRun ? "WOULD " : (ok ? "MOVED " : "FAILED"), state, from.Device, to.Device, mapped, pid,
                        title.Length > 60 ? title.Substring(0, 60) : title));
                }
            }

            if (!dryRun && Config.RESTORE_FOREGROUND && fg != IntPtr.Zero && Native.IsWindow(fg))
                Native.SetForegroundWindow(fg);

            return res;
        }

        // Maximized windows are pinned to a monitor. Update the restore rect so Windows
        // re-maximizes on the target; verify, and fall back to restore/move/maximize.
        static bool MoveMaximized(IntPtr h, Native.WINDOWPLACEMENT wp, Native.RECT mapped,
                                  Native.POINT wsOff, MonitorInfo to)
        {
            Native.WINDOWPLACEMENT np = wp;
            np.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
            np.showCmd = Native.SW_SHOWMAXIMIZED;
            np.rcNormalPosition = Native.RECT.FromXYWH(
                mapped.Left - wsOff.X, mapped.Top - wsOff.Y, mapped.Width, mapped.Height);

            bool ok = Native.SetWindowPlacement(h, ref np);

            IntPtr landed = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
            if (landed == to.Handle) return ok;

            // Fallback: unmaximize, move, re-maximize.
            Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetWindowPos(h, IntPtr.Zero, mapped.Left, mapped.Top, mapped.Width, mapped.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            Native.ShowWindow(h, Native.SW_MAXIMIZE);

            return Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST) == to.Handle;
        }
    }

    // --------------------------------------------------------- icon library
    static class IconLibrary
    {
        public static readonly string[] Names = {
            "Two screens + arrows",     // 0
            "Circular swap",            // 1
            "Monitors with stands",     // 2
            "Double arrow",             // 3
            "Split panels",             // 4
            "Diagonal swap arrows",     // 5
            "A / B boxes",              // 6
            "Facing chevrons",          // 7
            "Numbered screens",         // 8
            "Diagonal split square",    // 9
            "Vertical swap",            // 10
            "Cycle loop",               // 11
            "Split rectangle",          // 12
            "Shuffle paths",            // 13
            "Bold X",                   // 14
            "Arrows in boxes"           // 15
        };

        public static Bitmap Draw(int style, Color fg, int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                g.ScaleTransform(s, s);
                Paint(g, style, fg);
            }
            return bmp;
        }

        static Pen ArrowPen(Color c, float w)
        {
            Pen p = new Pen(c, w);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.ArrowAnchor;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        // GDI's ArrowAnchor line cap is far too small on curves at tray size,
        // so arrowheads get drawn as explicit triangles instead.
        static void Head(Graphics g, Brush b, float x, float y, float angleDeg, float size)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(angleDeg);
            g.FillPolygon(b, new PointF[] {
                new PointF(0f, 0f),
                new PointF(-size, -size * 0.62f),
                new PointF(-size, size * 0.62f) });
            g.Restore(st);
        }

        static void ArcHead(Graphics g, Pen p, Brush b, float cx, float cy, float r,
                            float startDeg, float sweepDeg, float headSize)
        {
            g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, startDeg, sweepDeg);
            double end = (startDeg + sweepDeg) * Math.PI / 180.0;
            float ex = cx + r * (float)Math.Cos(end);
            float ey = cy + r * (float)Math.Sin(end);
            Head(g, b, ex, ey, startDeg + sweepDeg + (sweepDeg >= 0 ? 90f : -90f), headSize);
        }

        static void LineHead(Graphics g, Pen p, Brush b, float x1, float y1, float x2, float y2, float headSize)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            // stop the shaft inside the head so the join is seamless
            float sx = x2 - dx / len * headSize * 0.7f;
            float sy = y2 - dy / len * headSize * 0.7f;
            g.DrawLine(p, x1, y1, sx, sy);
            Head(g, b, x2, y2, (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI), headSize);
        }

        static void CenterText(Graphics g, string t, Color c, RectangleF box, float px)
        {
            using (Font f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(t, f, b, box, sf);
            }
        }

        static void Paint(Graphics g, int style, Color fg)
        {
            using (Pen thin = new Pen(fg, 2.2f))
            using (SolidBrush solid = new SolidBrush(fg))
            using (SolidBrush ghost = new SolidBrush(Color.FromArgb(90, fg)))
            {
                thin.LineJoin = LineJoin.Round;

                switch (style)
                {
                    case 0: // two screens above, swap arrows below
                        g.DrawRectangle(thin, 2, 3, 11, 9);
                        g.DrawRectangle(thin, 19, 3, 11, 9);
                        using (Pen a = ArrowPen(fg, 2.6f))
                        {
                            g.DrawLine(a, 6, 20, 26, 20);
                            g.DrawLine(a, 26, 27, 6, 27);
                        }
                        break;

                    case 1: // two arcs forming a swap ring
                        using (Pen a = new Pen(fg, 3f))
                        {
                            a.StartCap = LineCap.Round;
                            ArcHead(g, a, solid, 16, 16, 10, 35, 130, 5.6f);
                            ArcHead(g, a, solid, 16, 16, 10, 215, 130, 5.6f);
                        }
                        break;

                    case 2: // two monitors with stands
                        g.DrawRectangle(thin, 2, 5, 11, 8);
                        g.DrawLine(thin, 7.5f, 13, 7.5f, 17);
                        g.DrawLine(thin, 4, 17, 11, 17);
                        g.DrawRectangle(thin, 19, 5, 11, 8);
                        g.DrawLine(thin, 24.5f, 13, 24.5f, 17);
                        g.DrawLine(thin, 21, 17, 28, 17);
                        using (Pen a = ArrowPen(fg, 2.4f))
                        {
                            g.DrawLine(a, 11, 24, 21, 24);
                            g.DrawLine(a, 21, 29, 11, 29);
                        }
                        break;

                    case 3: // one big double-headed arrow
                        using (Pen a = new Pen(fg, 4f))
                        {
                            a.StartCap = LineCap.ArrowAnchor;
                            a.EndCap = LineCap.ArrowAnchor;
                            g.DrawLine(a, 5, 16, 27, 16);
                        }
                        break;

                    case 4: // two panels, one solid one ghosted
                        g.FillRectangle(solid, 2, 7, 12, 18);
                        g.FillRectangle(ghost, 18, 7, 12, 18);
                        break;

                    case 5: // two offset diagonal arrows
                        using (Pen a = new Pen(fg, 2.8f))
                        {
                            a.StartCap = LineCap.Round;
                            LineHead(g, a, solid, 6, 21, 25, 7, 5.6f);
                            LineHead(g, a, solid, 26, 11, 7, 25, 5.6f);
                        }
                        break;

                    case 6: // A / B boxes
                        g.DrawRectangle(thin, 2, 8, 12, 16);
                        g.DrawRectangle(thin, 18, 8, 12, 16);
                        CenterText(g, "A", fg, new RectangleF(2, 8, 12, 16), 11f);
                        CenterText(g, "B", fg, new RectangleF(18, 8, 12, 16), 11f);
                        break;

                    case 7: // facing chevrons
                        using (Pen a = new Pen(fg, 3f))
                        {
                            a.LineJoin = LineJoin.Round; a.StartCap = LineCap.Round; a.EndCap = LineCap.Round;
                            g.DrawLines(a, new PointF[] { new PointF(13, 7), new PointF(5, 16), new PointF(13, 25) });
                            g.DrawLines(a, new PointF[] { new PointF(19, 7), new PointF(27, 16), new PointF(19, 25) });
                        }
                        break;

                    case 8: // numbered screens
                        g.DrawRectangle(thin, 2, 8, 12, 16);
                        g.DrawRectangle(thin, 18, 8, 12, 16);
                        CenterText(g, "1", fg, new RectangleF(2, 8, 12, 16), 11f);
                        CenterText(g, "2", fg, new RectangleF(18, 8, 12, 16), 11f);
                        break;

                    case 9: // square split on the diagonal, one half filled
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddPolygon(new PointF[] { new PointF(4, 4), new PointF(28, 4), new PointF(4, 28) });
                            g.FillPath(solid, gp);
                        }
                        g.DrawRectangle(thin, 4, 4, 24, 24);
                        break;

                    case 10: // stacked monitors, vertical swap arrows
                        g.DrawRectangle(thin, 2, 3, 13, 9);
                        g.DrawRectangle(thin, 2, 20, 13, 9);
                        using (Pen a = new Pen(fg, 2.6f))
                        {
                            a.StartCap = LineCap.Round;
                            LineHead(g, a, solid, 22, 25, 22, 5, 5.2f);
                            LineHead(g, a, solid, 29, 7, 29, 27, 5.2f);
                        }
                        break;

                    case 11: // single cycle loop
                        using (Pen a = new Pen(fg, 3.2f))
                        {
                            a.StartCap = LineCap.Round;
                            ArcHead(g, a, solid, 16, 16, 10, 55, 285, 6f);
                        }
                        break;

                    case 12: // one rectangle split down the middle
                        g.FillRectangle(solid, 3, 7, 12, 18);
                        g.DrawRectangle(thin, 17, 7, 12, 18);
                        break;

                    case 13: // shuffle paths
                        using (Pen a = new Pen(fg, 2.6f))
                        {
                            a.StartCap = LineCap.Round;
                            g.DrawBezier(a, 3, 8, 13, 8, 17, 23, 25, 23);
                            Head(g, solid, 29, 23, 0f, 5.6f);
                            g.DrawBezier(a, 3, 24, 13, 24, 17, 9, 25, 9);
                            Head(g, solid, 29, 9, 0f, 5.6f);
                        }
                        break;

                    case 14: // bold X (matches the hotkey)
                        using (Pen a = new Pen(fg, 4.5f))
                        {
                            a.StartCap = LineCap.Round; a.EndCap = LineCap.Round;
                            g.DrawLine(a, 6, 6, 26, 26);
                            g.DrawLine(a, 26, 6, 6, 26);
                        }
                        break;

                    case 15: // arrows inside boxes
                        g.DrawRectangle(thin, 1, 9, 13, 14);
                        g.DrawRectangle(thin, 18, 9, 13, 14);
                        using (Pen a = ArrowPen(fg, 2.4f))
                        {
                            g.DrawLine(a, 4, 16, 12, 16);
                            g.DrawLine(a, 28, 16, 20, 16);
                        }
                        break;

                    default:
                        goto case 0;
                }
            }
        }

        // A contact sheet of every option, on both a dark and a light strip,
        // at real tray size and enlarged. Written by ScreenSwap.exe /icons
        public static void WriteSheet(string path)
        {
            int cols = 4, rows = (Names.Length + cols - 1) / cols;
            int cw = 210, ch = 168, top = 54;
            int w = cols * cw, h = top + rows * ch;

            using (Bitmap sheet = new Bitmap(w, h))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(24, 24, 27));

                using (Font tf = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Color.White))
                    g.DrawString("ScreenSwap tray icon options  -  pick by number", tf, tb, 16, 18);

                for (int i = 0; i < Names.Length; i++)
                {
                    int cx = (i % cols) * cw;
                    int cy = top + (i / cols) * ch;

                    Rectangle dark = new Rectangle(cx + 10, cy + 6, cw - 20, 62);
                    Rectangle light = new Rectangle(cx + 10, cy + 68, cw - 20, 62);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(32, 32, 36))) g.FillRectangle(b, dark);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(238, 238, 240))) g.FillRectangle(b, light);
                    using (Pen p = new Pen(Color.FromArgb(70, 70, 76))) g.DrawRectangle(p, dark);

                    using (Bitmap big = Draw(i, Color.White, 48))
                        g.DrawImage(big, dark.Left + 18, dark.Top + 7);
                    using (Bitmap small = Draw(i, Color.White, 16))
                        g.DrawImage(small, dark.Left + 96, dark.Top + 23);
                    using (Bitmap small2 = Draw(i, Color.White, 24))
                        g.DrawImage(small2, dark.Left + 130, dark.Top + 19);

                    Color ink = Color.FromArgb(28, 28, 30);
                    using (Bitmap big = Draw(i, ink, 48))
                        g.DrawImage(big, light.Left + 18, light.Top + 7);
                    using (Bitmap small = Draw(i, ink, 16))
                        g.DrawImage(small, light.Left + 96, light.Top + 23);
                    using (Bitmap small2 = Draw(i, ink, 24))
                        g.DrawImage(small2, light.Left + 130, light.Top + 19);

                    using (Font f = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.DrawString(i + ".  " + Names[i], f, b, cx + 12, cy + 137);

                    using (Font f = new Font("Segoe UI", 11f, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 150, 158)))
                        g.DrawString("48px          16px   24px", f, b, cx + 14, cy + 152);
                }

                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    // ------------------------------------------------------------ tray host

    // A bare message-sink window. A hidden Form is no good here: if the form is
    // never shown, Windows never creates its handle, and RegisterHotKey needs one.
    class HotkeyWindow : NativeWindow, IDisposable
    {
        public const int HOTKEY_ID = 0xB0B;
        public event EventHandler Pressed;
        public bool Registered;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());   // forces a real HWND immediately
            Registered = Native.RegisterHotKey(Handle, HOTKEY_ID, Config.HOTKEY_MODS, Config.HOTKEY_VK);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (Pressed != null) Pressed(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Registered) Native.UnregisterHotKey(Handle, HOTKEY_ID);
            DestroyHandle();
        }
    }

    class TrayContext : ApplicationContext
    {
        NotifyIcon tray;
        ToolStripMenuItem startupItem;
        HotkeyWindow hotkey;

        int iconStyle;
        int iconColour;                 // 0 = auto (match taskbar), 1 = white, 2 = black
        IntPtr currentHicon = IntPtr.Zero;
        ToolStripMenuItem[] styleItems;
        ToolStripMenuItem[] colourItems;

        public TrayContext()
        {
            iconStyle = ReadSetting("IconStyle", Config.DEFAULT_ICON_STYLE);
            iconColour = ReadSetting("IconColour", 0);
            if (iconStyle < 0 || iconStyle >= IconLibrary.Names.Length) iconStyle = 0;

            hotkey = new HotkeyWindow();
            hotkey.Pressed += delegate { DoSwap(false); };
            Log.Write(hotkey.Registered
                ? "hotkey " + Config.HOTKEY_TEXT + " registered"
                : "hotkey " + Config.HOTKEY_TEXT + " NOT registered (taken by another app)");

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem swapItem = new ToolStripMenuItem("Swap screens now  (" + Config.HOTKEY_TEXT + ")");
            swapItem.Font = new Font(swapItem.Font, FontStyle.Bold);
            swapItem.Click += delegate { DoSwap(false); };
            menu.Items.Add(swapItem);

            ToolStripMenuItem diagItem = new ToolStripMenuItem("Swap and show report...");
            diagItem.Click += delegate { DoSwap(true); };
            menu.Items.Add(diagItem);

            menu.Items.Add(new ToolStripSeparator());

            // ---- Icon picker: every option previewed at its real drawing ----
            ToolStripMenuItem iconMenu = new ToolStripMenuItem("Tray icon");
            styleItems = new ToolStripMenuItem[IconLibrary.Names.Length];
            for (int i = 0; i < IconLibrary.Names.Length; i++)
            {
                int captured = i;
                ToolStripMenuItem it = new ToolStripMenuItem(i + ".  " + IconLibrary.Names[i]);
                it.Image = IconLibrary.Draw(i, GlyphColour(), 20);
                it.ImageScaling = ToolStripItemImageScaling.None;
                it.Checked = (i == iconStyle);
                it.Click += delegate
                {
                    iconStyle = captured;
                    WriteSetting("IconStyle", iconStyle);
                    ApplyIcon();
                    RefreshChecks();
                };
                styleItems[i] = it;
                iconMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(iconMenu);

            ToolStripMenuItem colourMenu = new ToolStripMenuItem("Icon colour");
            string[] colourNames = { "Auto (match taskbar)", "Always white", "Always black" };
            colourItems = new ToolStripMenuItem[colourNames.Length];
            for (int i = 0; i < colourNames.Length; i++)
            {
                int captured = i;
                ToolStripMenuItem it = new ToolStripMenuItem(colourNames[i]);
                it.Checked = (i == iconColour);
                it.Click += delegate
                {
                    iconColour = captured;
                    WriteSetting("IconColour", iconColour);
                    ApplyIcon();
                    RefreshChecks();
                };
                colourItems[i] = it;
                colourMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(colourMenu);

            ToolStripMenuItem sheetItem = new ToolStripMenuItem("Show all icons side by side...");
            sheetItem.Click += delegate { ShowSheet(); };
            menu.Items.Add(sheetItem);

            menu.Items.Add(new ToolStripSeparator());

            startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.CheckOnClick = true;
            startupItem.Checked = IsStartupEnabled();
            startupItem.CheckedChanged += delegate { SetStartup(startupItem.Checked); };
            menu.Items.Add(startupItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem logItem = new ToolStripMenuItem("Open log file...");
            logItem.Click += delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.File_ + "\""); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "ScreenSwap"); }
            };
            menu.Items.Add(logItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Log.Write("exit (tray menu)"); ExitThread(); };
            menu.Items.Add(exitItem);

            tray = new NotifyIcon();
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.Text = "ScreenSwap - " + Config.HOTKEY_TEXT;
            tray.DoubleClick += delegate { DoSwap(false); };
            ApplyIcon();
            Log.Write("tray icon visible (style " + iconStyle + ", colour mode " + iconColour + ") - ready");

            // Redraw the glyph if you switch Windows between light and dark.
            SystemEvents.UserPreferenceChanged += OnPrefChanged;

            // So a shutdown/logoff kill is distinguishable from a silent death.
            SystemEvents.SessionEnding += OnSessionEnding;

            if (!hotkey.Registered)
            {
                tray.ShowBalloonTip(8000, "ScreenSwap",
                    Config.HOTKEY_TEXT + " is already taken by another app. " +
                    "Use the tray icon, or change the hotkey in ScreenSwap.cs and rebuild.",
                    ToolTipIcon.Warning);
            }
        }

        void DoSwap(bool report)
        {
            StringWriter sw = report ? new StringWriter() : null;
            Swapper.Result r;
            try
            {
                r = Swapper.Swap(hotkey.Handle, sw, false);
            }
            catch (Exception ex)
            {
                Log.Write("SWAP FAILED " + ex);
                tray.ShowBalloonTip(6000, "ScreenSwap failed", ex.Message, ToolTipIcon.Error);
                return;
            }
            Log.Write(string.Format("swap: moved {0}, failed {1}, skipped {2}", r.Moved, r.Failed, r.Skipped));

            if (report)
            {
                string path = Path.Combine(Path.GetTempPath(), "screenswap-report.txt");
                string header = string.Format(
                    "ScreenSwap report{0}{1}{0}Moved: {2}   Failed: {3}   Skipped: {4}{0}{5}{0}{0}",
                    Environment.NewLine, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.Moved, r.Failed, r.Skipped,
                    r.Failed > 0 ? "Failed windows are usually running as administrator. Run ScreenSwap as administrator to move those too." : "");
                File.WriteAllText(path, header + sw.ToString());
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                return;
            }

            if (!string.IsNullOrEmpty(r.Message))
                tray.ShowBalloonTip(4000, "ScreenSwap", r.Message, ToolTipIcon.Info);
            else if (r.Failed > 0)
                tray.ShowBalloonTip(5000, "ScreenSwap",
                    string.Format("Moved {0} windows. {1} would not move - those are running as administrator. Run ScreenSwap as administrator to include them.", r.Moved, r.Failed),
                    ToolTipIcon.Warning);
        }

        // Autostart runs from a Task Scheduler logon task, not the Run key.
        // The Run key was observed writing correctly and then silently never
        // firing at logon, so it is not trustworthy for this.
        //
        // This uses the Task Scheduler COM API rather than schtasks.exe: the
        // command-line tool refuses to create an ONLOGON task without elevation
        // ("Access is denied"), while COM happily registers one scoped to the
        // current user with an interactive token.
        static dynamic TaskFolder()
        {
            Type t = Type.GetTypeFromProgID("Schedule.Service");
            if (t == null) return null;
            dynamic svc = Activator.CreateInstance(t);
            svc.Connect();
            return svc.GetFolder("\\");
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                dynamic folder = TaskFolder();
                if (folder == null) return false;
                return folder.GetTask(Config.APP_NAME) != null;   // throws when absent
            }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            if (on)
            {
                try
                {
                    Type t = Type.GetTypeFromProgID("Schedule.Service");
                    dynamic svc = Activator.CreateInstance(t);
                    svc.Connect();
                    dynamic folder = svc.GetFolder("\\");
                    dynamic td = svc.NewTask(0);

                    td.RegistrationInfo.Description = "Starts ScreenSwap at logon.";
                    td.RegistrationInfo.Author = Environment.UserName;

                    td.Settings.Enabled = true;
                    td.Settings.StartWhenAvailable = true;
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;
                    td.Settings.ExecutionTimeLimit = "PT0S";   // none; the default kills it after 3 days
                    td.Settings.MultipleInstances = 2;         // TASK_INSTANCES_IGNORE_NEW

                    dynamic trig = td.Triggers.Create(9);      // TASK_TRIGGER_LOGON
                    trig.Delay = "PT15S";                      // let the taskbar tray exist first
                    trig.UserId = Environment.UserDomainName + "\\" + Environment.UserName;

                    // Self-heal: re-check every 5 minutes, forever. Combined with
                    // MultipleInstances=IgnoreNew this is a no-op while the app is
                    // alive and a relaunch if it ever dies.
                    trig.Repetition.Interval = "PT5M";
                    trig.Repetition.Duration = "";             // empty = indefinite
                    trig.Repetition.StopAtDurationEnd = false;

                    dynamic act = td.Actions.Create(0);        // TASK_ACTION_EXEC
                    act.Path = Application.ExecutablePath;
                    act.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);

                    // TASK_CREATE_OR_UPDATE(6), TASK_LOGON_INTERACTIVE_TOKEN(3)
                    folder.RegisterTaskDefinition(Config.APP_NAME, td, 6, null, null, 3);
                    Log.Write("autostart enabled (logon task created)");
                }
                catch (Exception ex)
                {
                    Log.Write("autostart FAILED: " + ex.Message);
                    MessageBox.Show(
                        "Could not create the startup task.\r\n\r\n" + ex.Message +
                        "\r\n\r\nScreenSwap still works, it just will not start automatically.",
                        "ScreenSwap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                try
                {
                    dynamic folder = TaskFolder();
                    if (folder != null) folder.DeleteTask(Config.APP_NAME, 0);
                    Log.Write("autostart disabled (logon task deleted)");
                }
                catch (Exception ex) { Log.Write("autostart delete failed: " + ex.Message); }
            }

            // Remove the old Run key mechanism if a previous version left one behind.
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(Config.RUN_KEY, true);
                if (k != null)
                    using (k) { if (k.GetValue(Config.APP_NAME) != null) k.DeleteValue(Config.APP_NAME); }
            }
            catch { }
        }

        // ---- icon plumbing ----

        static bool TaskbarIsLight()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme", 0);
                return v != null && Convert.ToInt32(v) != 0;
            }
            catch { return false; }
        }

        Color GlyphColour()
        {
            if (iconColour == 1) return Color.White;
            if (iconColour == 2) return Color.FromArgb(24, 24, 26);
            return TaskbarIsLight() ? Color.FromArgb(24, 24, 26) : Color.White;
        }

        void ApplyIcon()
        {
            // Tray icons are drawn at 16px logical, which is ~32px at your 196% scaling.
            using (Bitmap bmp = IconLibrary.Draw(iconStyle, GlyphColour(), 32))
            {
                IntPtr h = bmp.GetHicon();
                using (Icon tmp = Icon.FromHandle(h))
                {
                    tray.Icon = (Icon)tmp.Clone();
                }
                // Icon.FromHandle does not own the handle, so free the previous one by hand.
                if (currentHicon != IntPtr.Zero) Native.DestroyIcon(currentHicon);
                currentHicon = h;
            }

            if (styleItems != null)
            {
                Color c = GlyphColour();
                for (int i = 0; i < styleItems.Length; i++)
                {
                    Image old = styleItems[i].Image;
                    styleItems[i].Image = IconLibrary.Draw(i, c, 20);
                    if (old != null) old.Dispose();
                }
            }
        }

        void RefreshChecks()
        {
            if (styleItems != null)
                for (int i = 0; i < styleItems.Length; i++) styleItems[i].Checked = (i == iconStyle);
            if (colourItems != null)
                for (int i = 0; i < colourItems.Length; i++) colourItems[i].Checked = (i == iconColour);
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Log.Write("session ending (" + e.Reason + ") - Windows is closing this process");
        }

        void OnPrefChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.VisualStyle ||
                e.Category == UserPreferenceCategory.Color)
            {
                if (iconColour == 0) ApplyIcon();
            }
        }

        void ShowSheet()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "screenswap-icons.png");
                IconLibrary.WriteSheet(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not build the icon sheet: " + ex.Message, "ScreenSwap");
            }
        }

        // ---- settings ----

        static int ReadSetting(string name, int fallback)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(Config.SETTINGS_KEY, false);
                if (k == null) return fallback;
                using (k)
                {
                    object v = k.GetValue(name);
                    return v == null ? fallback : Convert.ToInt32(v);
                }
            }
            catch { return fallback; }
        }

        static void WriteSetting(string name, int value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Config.SETTINGS_KEY))
                    if (k != null) k.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnPrefChanged;
                SystemEvents.SessionEnding -= OnSessionEnding;
                if (hotkey != null) { hotkey.Dispose(); hotkey = null; }
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                if (currentHicon != IntPtr.Zero) { Native.DestroyIcon(currentHicon); currentHicon = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------- main
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("FATAL " + (e.ExceptionObject == null ? "(null)" : e.ExceptionObject.ToString()));
            };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Write("UI EXCEPTION " + e.Exception);
            };

            Log.Write("start  args=[" + string.Join(" ", args) + "]  exe=" + Application.ExecutablePath);
            Application.ApplicationExit += delegate { Log.Write("application exit (clean)"); };

            // Belt and braces: the manifest already asks for PerMonitorV2.
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { }

            bool once = false, report = false, dryRun = false;
            foreach (string a in args)
            {
                string s = a.TrimStart('-', '/').ToLowerInvariant();
                if (s == "once" || s == "swap") once = true;
                if (s == "report") { once = true; report = true; }
                // dryrun: list what would move, change nothing, no popup. For scripting.
                if (s == "dryrun" || s == "list" || s == "test") { once = true; dryRun = true; }
                if (s == "autostart-on" || s == "autostart-off" || s == "autostart-status")
                {
                    if (s == "autostart-on") TrayContext.SetStartup(true);
                    else if (s == "autostart-off") TrayContext.SetStartup(false);
                    Log.Write("autostart status: " + (TrayContext.IsStartupEnabled() ? "ENABLED" : "disabled"));
                    return;
                }
                // /icons     -> build the sheet and open it
                // /iconsheet -> build it silently (for scripting)
                if (s == "icons" || s == "iconsheet")
                {
                    string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "icon-options.png");
                    IconLibrary.WriteSheet(p);
                    if (s == "icons")
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
                    return;
                }
            }

            if (once)
            {
                StringWriter sw = (report || dryRun) ? new StringWriter() : null;
                Swapper.Result r = Swapper.Swap(IntPtr.Zero, sw, dryRun);
                if (sw != null)
                {
                    string path = Path.Combine(Path.GetTempPath(), "screenswap-report.txt");
                    File.WriteAllText(path, string.Format("{4}Windows: {0}   failed: {1}   skipped: {2}{3}{3}",
                        r.Moved, r.Failed, r.Skipped, Environment.NewLine,
                        dryRun ? "DRY RUN - nothing was moved." + Environment.NewLine : "") + sw.ToString());
                    if (!dryRun) System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                }
                return;
            }

            bool created;
            using (System.Threading.Mutex mtx = new System.Threading.Mutex(true, "ScreenSwap.SingleInstance", out created))
            {
                if (!created)
                {
                    // Exit quietly. A modal dialog here would be poisonous if
                    // anything ever relaunches this on a timer.
                    Log.Write("already running - this instance is exiting");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
                GC.KeepAlive(mtx);
            }
        }
    }
}
